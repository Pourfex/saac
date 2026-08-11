// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using Microsoft.Psi;

    /// <summary>
    /// Node 8 (Beta): different SelectModule <strong>then</strong> Validation contiguous
    /// (no other Select between), inside the 5 s window from node entry
    /// (call <see cref="Component.Reset"/> on entry).
    /// Same-value Select republishes must not clear pending.
    /// </summary>
    public static class DifferentGeneratorButtonFilter
    {
        /// <summary>
        /// Creates a resettable Beta-path detector component.
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
                ? nameof(DifferentGeneratorButtonFilter)
                : name;

            return new Component(pipeline, operatorName);
        }

        /// <summary>
        /// Emits true when a different SelectModule is followed contiguously by Validation.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="selectModule">Catalog SelectModule stream.</param>
        /// <param name="validation">Catalog Validation stream.</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>True pulses on different-button then Validation.</returns>
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

            selectModule.PipeTo(component.SelectModuleIn, deliveryPolicy ?? DeliveryPolicy.Unlimited);
            validation.PipeTo(component.ValidationIn, DeliveryPolicy.Unlimited);
            return component.Out;
        }

        /// <summary>
        /// Resettable contiguous different-Select→Validation detector.
        /// </summary>
        public sealed class Component
        {
            private readonly Emitter<bool> output;
            private readonly TimeSpan window;
            private string? previousSelect;
            private bool pendingDifferentSelect;
            private bool previousValidation;
            private DateTime lastEmittedOt = DateTime.MinValue;
            private DateTime? windowOrigin;

            internal Component(Pipeline pipeline, string name)
            {
                this.window = TimeSpan.FromMilliseconds(IndexFilterAssumptions.SequenceWindowMs);
                this.output = pipeline.CreateEmitter<bool>(this, name + "-Out");
                this.SelectModuleIn = pipeline.CreateReceiver<string>(this, this.ReceiveSelectModule, name + "-SelectIn");
                this.ValidationIn = pipeline.CreateReceiver<bool>(this, this.ReceiveValidation, name + "-ValIn");
            }

            /// <summary>SelectModule input.</summary>
            public Receiver<string> SelectModuleIn { get; }

            /// <summary>Validation input.</summary>
            public Receiver<bool> ValidationIn { get; }

            /// <summary>Hit output.</summary>
            public Emitter<bool> Out => this.output;

            /// <summary>
            /// Clears pending/previous and starts a fresh node-entry 5 s window.
            /// </summary>
            public void Reset(DateTime originatingTime)
            {
                this.previousSelect = null;
                this.pendingDifferentSelect = false;
                // Suppress spurious rising from Validation already held true at node entry.
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
                    this.previousSelect = null;
                    this.pendingDifferentSelect = false;
                    this.Emit(false, envelope.OriginatingTime);
                    return;
                }

                bool changed = this.previousSelect != null
                    && !string.Equals(this.previousSelect, module, StringComparison.Ordinal);

                if (changed)
                {
                    // Contiguous: a different Select arms (and replaces) pending.
                    this.pendingDifferentSelect = true;
                    this.previousSelect = module;
                }
                else if (this.previousSelect == null)
                {
                    // First non-empty after reset seeds without arming.
                    this.previousSelect = module;
                }

                // Same-value republish: do not clear pending; leave previousSelect as-is.
                this.Emit(false, envelope.OriginatingTime);
            }

            private void ReceiveValidation(bool pressed, Envelope envelope)
            {
                if (!this.IsInsideWindow(envelope.OriginatingTime))
                {
                    this.previousValidation = pressed;
                    this.Emit(false, envelope.OriginatingTime);
                    return;
                }

                bool rising = pressed && !this.previousValidation;
                this.previousValidation = pressed;

                bool hit = rising && this.pendingDifferentSelect;
                if (hit)
                {
                    this.pendingDifferentSelect = false;
                }

                this.Emit(hit, envelope.OriginatingTime);
            }

            private bool IsInsideWindow(DateTime originatingTime)
            {
                if (!this.windowOrigin.HasValue)
                {
                    return false;
                }

                TimeSpan elapsed = originatingTime - this.windowOrigin.Value;
                return elapsed >= TimeSpan.Zero && elapsed <= this.window;
            }

            private void Emit(bool value, DateTime originatingTime)
            {
                DateTime emitOt = originatingTime;
                if (emitOt < this.lastEmittedOt)
                {
                    emitOt = this.lastEmittedOt.AddTicks(1);
                }
                else if (emitOt == this.lastEmittedOt)
                {
                    if (!value)
                    {
                        return;
                    }

                    emitOt = this.lastEmittedOt.AddTicks(1);
                }

                this.output.Post(value, emitOt);
                this.lastEmittedOt = emitOt;
            }
        }
    }
}
