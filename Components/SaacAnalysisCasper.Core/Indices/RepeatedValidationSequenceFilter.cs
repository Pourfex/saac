// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using Microsoft.Psi;
    using Microsoft.Psi.Components;

    /// <summary>
    /// Alpha-path derived filter: repeated validation sequence from SelectModule + Validation (speech omitted).
    /// </summary>
    /// <remarks>
    /// Counts false→true Validation edges (not sustained true samples).
    /// On SelectModule change, resets accumulation so counts do not cross modules.
    /// Threshold: ≥ <see cref="IndexFilterAssumptions.AlphaValidationCountThreshold"/> edges
    /// inside <see cref="IndexFilterAssumptions.AlphaValidationWindowMs"/>.
    /// </remarks>
    public static class RepeatedValidationSequenceFilter
    {
        /// <summary>
        /// Emits true when the validation-edge count threshold is met inside the aggregation window.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="selectModule">Catalog SelectModule stream.</param>
        /// <param name="validation">Catalog Validation stream.</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>True pulses for Alpha-path repeated validation.</returns>
        public static IProducer<bool> Apply(
            Pipeline pipeline,
            IProducer<string> selectModule,
            IProducer<bool> validation,
            DeliveryPolicy? deliveryPolicy = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (selectModule == null)
            {
                throw new ArgumentNullException(nameof(selectModule));
            }

            if (validation == null)
            {
                throw new ArgumentNullException(nameof(validation));
            }

            string operatorName = string.IsNullOrWhiteSpace(name)
                ? nameof(RepeatedValidationSequenceFilter)
                : name;

            ValidationSequenceCounter counter = new ValidationSequenceCounter(pipeline, operatorName);
            selectModule.PipeTo(counter.SelectModuleIn, DeliveryPolicy.Unlimited);
            validation.PipeTo(counter.ValidationIn, deliveryPolicy ?? DeliveryPolicy.Unlimited);
            return counter.Out;
        }

        private sealed class ValidationSequenceCounter
        {
            private readonly Emitter<bool> output;
            private readonly int threshold;
            private readonly TimeSpan window;
            private readonly System.Collections.Generic.List<DateTime> edgeTimes;
            private string? currentModule;
            private bool previousValidation;
            private DateTime lastEmittedOt = DateTime.MinValue;
            private bool lastEmittedValue;

            public ValidationSequenceCounter(Pipeline pipeline, string name)
            {
                this.threshold = IndexFilterAssumptions.AlphaValidationCountThreshold;
                this.window = TimeSpan.FromMilliseconds(IndexFilterAssumptions.AlphaValidationWindowMs);
                this.edgeTimes = new System.Collections.Generic.List<DateTime>();
                this.output = pipeline.CreateEmitter<bool>(this, name + "-Out");
                this.SelectModuleIn = pipeline.CreateReceiver<string>(this, this.ReceiveSelectModule, name + "-SelectIn");
                this.ValidationIn = pipeline.CreateReceiver<bool>(this, this.ReceiveValidation, name + "-ValIn");
            }

            public Receiver<string> SelectModuleIn { get; }

            public Receiver<bool> ValidationIn { get; }

            public Emitter<bool> Out => this.output;

            private void ReceiveSelectModule(string module, Envelope envelope)
            {
                if (!string.Equals(this.currentModule, module, StringComparison.Ordinal))
                {
                    this.currentModule = module;
                    this.edgeTimes.Clear();
                    this.previousValidation = false;
                    this.Emit(false, envelope.OriginatingTime);
                }
            }

            private void ReceiveValidation(bool pressed, Envelope envelope)
            {
                bool rising = pressed && !this.previousValidation;
                this.previousValidation = pressed;

                if (rising)
                {
                    this.edgeTimes.Add(envelope.OriginatingTime);
                }

                DateTime cutoff = envelope.OriginatingTime - this.window;
                int write = 0;
                for (int i = 0; i < this.edgeTimes.Count; i++)
                {
                    if (this.edgeTimes[i] >= cutoff)
                    {
                        this.edgeTimes[write++] = this.edgeTimes[i];
                    }
                }

                if (write < this.edgeTimes.Count)
                {
                    this.edgeTimes.RemoveRange(write, this.edgeTimes.Count - write);
                }

                bool met = this.edgeTimes.Count >= this.threshold;
                this.Emit(met, envelope.OriginatingTime);
            }

            /// <summary>
            /// Posts with Psi strictly-increasing OT. SelectModule and Validation often share OT on
            /// inject/catalog ticks; coalesce same-value collisions and nudge one tick only when the
            /// emitted bool must change at that shared OT.
            /// </summary>
            private void Emit(bool value, DateTime originatingTime)
            {
                DateTime emitOt = originatingTime;
                if (emitOt < this.lastEmittedOt)
                {
                    return;
                }

                if (emitOt == this.lastEmittedOt)
                {
                    if (value == this.lastEmittedValue)
                    {
                        return;
                    }

                    emitOt = this.lastEmittedOt.AddTicks(1);
                }

                this.output.Post(value, emitOt);
                this.lastEmittedOt = emitOt;
                this.lastEmittedValue = value;
            }
        }
    }
}
