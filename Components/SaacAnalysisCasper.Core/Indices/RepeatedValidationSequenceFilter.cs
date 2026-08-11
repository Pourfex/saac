// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;

    /// <summary>
    /// Node 7 (Alpha): Validation×3 <strong>or</strong> (SelectModule→Validation)×3 counted inside
    /// the 5 s window starting at node entry (call <see cref="Component.Reset"/> on entry).
    /// Same-value Select republishes do not inflate Select→Val pairs.
    /// </summary>
    public static class RepeatedValidationSequenceFilter
    {
        /// <summary>
        /// Creates a resettable Alpha-path counter component.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Component with Select/Validation inputs, Out, and synchronous Reset.</returns>
        public static Component Create(Pipeline pipeline, string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            string operatorName = string.IsNullOrWhiteSpace(name)
                ? nameof(RepeatedValidationSequenceFilter)
                : name;

            return new Component(pipeline, operatorName);
        }

        /// <summary>
        /// Emits true when either Alpha arm meets the count threshold inside the aggregation window.
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
            Component component = Create(pipeline, name);
            if (selectModule == null)
            {
                throw new ArgumentNullException(nameof(selectModule));
            }

            if (validation == null)
            {
                throw new ArgumentNullException(nameof(validation));
            }

            selectModule.PipeTo(component.SelectModuleIn, DeliveryPolicy.Unlimited);
            validation.PipeTo(component.ValidationIn, deliveryPolicy ?? DeliveryPolicy.Unlimited);
            return component.Out;
        }

        /// <summary>
        /// Resettable Validation×3 / (Select→Val)×3 counter.
        /// </summary>
        public sealed class Component
        {
            private readonly Emitter<bool> output;
            private readonly int threshold;
            private readonly TimeSpan window;
            private readonly List<DateTime> validationEdgeTimes;
            private readonly List<DateTime> selectThenValPairTimes;
            private string? lastSelectValue;
            private bool awaitingValidationAfterSelect;
            private bool previousValidation;
            private DateTime lastEmittedOt = DateTime.MinValue;
            private bool lastEmittedValue;
            private DateTime? windowOrigin;

            internal Component(Pipeline pipeline, string name)
            {
                this.threshold = IndexFilterAssumptions.AlphaValidationCountThreshold;
                this.window = TimeSpan.FromMilliseconds(IndexFilterAssumptions.SequenceWindowMs);
                this.validationEdgeTimes = new List<DateTime>();
                this.selectThenValPairTimes = new List<DateTime>();
                this.output = pipeline.CreateEmitter<bool>(this, name + "-Out");
                this.SelectModuleIn = pipeline.CreateReceiver<string>(this, this.ReceiveSelectModule, name + "-SelectIn");
                this.ValidationIn = pipeline.CreateReceiver<bool>(this, this.ReceiveValidation, name + "-ValIn");
            }

            /// <summary>SelectModule input.</summary>
            public Receiver<string> SelectModuleIn { get; }

            /// <summary>Validation input.</summary>
            public Receiver<bool> ValidationIn { get; }

            /// <summary>Met-level output.</summary>
            public Emitter<bool> Out => this.output;

            /// <summary>
            /// Clears counters/pending and starts a fresh node-entry 5 s window.
            /// </summary>
            public void Reset(DateTime originatingTime)
            {
                this.validationEdgeTimes.Clear();
                this.selectThenValPairTimes.Clear();
                this.awaitingValidationAfterSelect = false;
                this.lastSelectValue = null;
                // Suppress spurious rising from a Validation already held true at node entry.
                this.previousValidation = true;
                this.windowOrigin = originatingTime;
                this.Emit(false, originatingTime);
            }

            private void ReceiveSelectModule(string module, Envelope envelope)
            {
                if (!this.IsInsideWindow(envelope.OriginatingTime))
                {
                    this.Emit(false, envelope.OriginatingTime);
                    return;
                }

                if (string.IsNullOrEmpty(module))
                {
                    this.awaitingValidationAfterSelect = false;
                    this.lastSelectValue = null;
                    this.Emit(this.IsMet(envelope.OriginatingTime), envelope.OriginatingTime);
                    return;
                }

                // Same-value republish: do not re-arm / inflate Select→Val pairs.
                bool isNewSelect = this.lastSelectValue == null
                    || !string.Equals(this.lastSelectValue, module, StringComparison.Ordinal);
                this.lastSelectValue = module;

                if (isNewSelect)
                {
                    this.awaitingValidationAfterSelect = true;
                }

                this.Emit(this.IsMet(envelope.OriginatingTime), envelope.OriginatingTime);
            }

            private void ReceiveValidation(bool pressed, Envelope envelope)
            {
                if (!this.IsInsideWindow(envelope.OriginatingTime))
                {
                    bool risingOutside = pressed && !this.previousValidation;
                    this.previousValidation = pressed;
                    if (risingOutside)
                    {
                        // Ignore edges outside the node-entry window.
                    }

                    this.Emit(false, envelope.OriginatingTime);
                    return;
                }

                bool rising = pressed && !this.previousValidation;
                this.previousValidation = pressed;

                if (rising)
                {
                    this.validationEdgeTimes.Add(envelope.OriginatingTime);

                    if (this.awaitingValidationAfterSelect)
                    {
                        this.selectThenValPairTimes.Add(envelope.OriginatingTime);
                        this.awaitingValidationAfterSelect = false;
                    }
                }

                this.Emit(this.IsMet(envelope.OriginatingTime), envelope.OriginatingTime);
            }

            private bool IsInsideWindow(DateTime originatingTime)
            {
                if (!this.windowOrigin.HasValue)
                {
                    // Not yet armed by node entry — ignore (session-long arming forbidden).
                    return false;
                }

                TimeSpan elapsed = originatingTime - this.windowOrigin.Value;
                return elapsed >= TimeSpan.Zero && elapsed <= this.window;
            }

            private bool IsMet(DateTime originatingTime)
            {
                if (!this.IsInsideWindow(originatingTime))
                {
                    return false;
                }

                return this.validationEdgeTimes.Count >= this.threshold
                    || this.selectThenValPairTimes.Count >= this.threshold;
            }

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
