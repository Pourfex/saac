// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using Microsoft.Psi;

    /// <summary>
    /// Node 2: DoorClosed <strong>level</strong> ∧ (SelectModule change | Validation rising).
    /// The 2 s miss window and immediate open→miss are enforced by <c>Logigramme1NodeStepper</c> from node entry.
    /// </summary>
    public static class PostDoorSelectOrValidationFilter
    {
        /// <summary>
        /// Emits true when door is closed (level) and SelectModule changes or Validation rises.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="doorClosed">Paired generator DoorClosed level stream.</param>
        /// <param name="selectModule">Catalog SelectModule stream.</param>
        /// <param name="validation">Catalog Validation stream.</param>
        /// <param name="deliveryPolicy">Optional delivery policy for primary inputs.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Hit pulses for node 2.</returns>
        public static IProducer<bool> Apply(
            Pipeline pipeline,
            IProducer<bool> doorClosed,
            IProducer<string> selectModule,
            IProducer<bool> validation,
            DeliveryPolicy? deliveryPolicy = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (doorClosed == null)
            {
                throw new ArgumentNullException(nameof(doorClosed));
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
                ? nameof(PostDoorSelectOrValidationFilter)
                : name;

            GateDetector detector = new GateDetector(pipeline, operatorName);
            doorClosed.PipeTo(detector.DoorClosedIn, deliveryPolicy ?? DeliveryPolicy.Unlimited);
            selectModule.PipeTo(detector.SelectModuleIn, DeliveryPolicy.Unlimited);
            validation.PipeTo(detector.ValidationIn, DeliveryPolicy.Unlimited);
            return detector.Out;
        }

        private sealed class GateDetector
        {
            private readonly Emitter<bool> output;
            private bool? doorClosed;
            private string? previousSelect;
            private bool previousValidation;
            private DateTime lastEmittedOt = DateTime.MinValue;

            public GateDetector(Pipeline pipeline, string name)
            {
                this.output = pipeline.CreateEmitter<bool>(this, name + "-Out");
                this.DoorClosedIn = pipeline.CreateReceiver<bool>(this, this.ReceiveDoorClosed, name + "-DoorIn");
                this.SelectModuleIn = pipeline.CreateReceiver<string>(this, this.ReceiveSelectModule, name + "-SelectIn");
                this.ValidationIn = pipeline.CreateReceiver<bool>(this, this.ReceiveValidation, name + "-ValIn");
            }

            public Receiver<bool> DoorClosedIn { get; }

            public Receiver<string> SelectModuleIn { get; }

            public Receiver<bool> ValidationIn { get; }

            public Emitter<bool> Out => this.output;

            private void ReceiveDoorClosed(bool closed, Envelope envelope)
            {
                this.doorClosed = closed;
                this.Emit(false, envelope.OriginatingTime);
            }

            private void ReceiveSelectModule(string module, Envelope envelope)
            {
                if (string.IsNullOrEmpty(module))
                {
                    this.previousSelect = null;
                    this.Emit(false, envelope.OriginatingTime);
                    return;
                }

                bool changed = this.previousSelect != null
                    && !string.Equals(this.previousSelect, module, StringComparison.Ordinal);

                this.previousSelect = module;

                bool hit = changed && this.doorClosed == true;
                this.Emit(hit, envelope.OriginatingTime);
            }

            private void ReceiveValidation(bool pressed, Envelope envelope)
            {
                bool rising = pressed && !this.previousValidation;
                this.previousValidation = pressed;
                bool hit = rising && this.doorClosed == true;
                this.Emit(hit, envelope.OriginatingTime);
            }

            private void Emit(bool value, DateTime originatingTime)
            {
                DateTime emitOt = originatingTime;
                if (emitOt < this.lastEmittedOt)
                {
                    emitOt = this.lastEmittedOt.AddTicks(1);
                }

                if (emitOt == this.lastEmittedOt)
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
