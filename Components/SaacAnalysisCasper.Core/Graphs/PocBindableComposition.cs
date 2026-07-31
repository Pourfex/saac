// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Graphs
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Mapping;
    using SaacAnalysisCasper.Core.Poc;
    using SaacAnalysisCasper.Core.Windowing;

    /// <summary>
    /// Thin <see cref="IBindableComposition"/> adapter over Core <see cref="PocCoincidenceOperator"/>.
    /// Inject-only by default (empty <see cref="RequiredPortRoles"/>); catalog SelectModule remains optional via <see cref="Connect"/>.
    /// </summary>
    public sealed class PocBindableComposition : IBindableComposition, IPocExportSurface
    {
        private static readonly IReadOnlyList<string> NoRequiredRoles = Array.Empty<string>();

        private readonly Pipeline pipeline;
        private readonly HashSet<string> connectedRoles;
        private readonly Emitter<PocTaggedEvent> injectBridge;
        private readonly IProducer<PocCoincidenceC> coincidenceOut;

        /// <summary>
        /// Initializes a new instance of the <see cref="PocBindableComposition"/> class.
        /// </summary>
        /// <param name="pipeline">Owning analysis pipeline.</param>
        /// <param name="participant">Participant this instance serves (AD-3).</param>
        /// <param name="windowMs">Window length W from AD-8 (closed over; not hard-coded).</param>
        public PocBindableComposition(Pipeline pipeline, ParticipantId participant, int windowMs)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            HoppingWindowPolicy.ValidateWindowMs(windowMs, nameof(windowMs));

            this.pipeline = pipeline;
            this.Participant = participant;
            this.WindowMs = windowMs;
            this.connectedRoles = new HashSet<string>(StringComparer.Ordinal);
            this.Name = "Poc-" + participant + "-W" + windowMs;

            // Host-facing inject receiver; internal bridge feeds the Core operator.
            this.InjectIn = this.pipeline.CreateReceiver<PocTaggedEvent>(
                this,
                this.ProcessInject,
                this.Name + "-InjectIn");
            this.injectBridge = this.pipeline.CreateEmitter<PocTaggedEvent>(
                this,
                this.Name + "-InjectBridge");

            this.coincidenceOut = PocCoincidenceOperator.Apply(
                this.injectBridge,
                windowMs,
                DeliveryPolicy.Unlimited,
                this.Name + "-Coincidence");
        }

        /// <inheritdoc/>
        public string GraphId => "Poc";

        /// <inheritdoc/>
        public ParticipantId Participant { get; }

        /// <inheritdoc/>
        public int WindowMs { get; }

        /// <inheritdoc/>
        public IReadOnlyList<string> RequiredPortRoles => NoRequiredRoles;

        /// <summary>
        /// Gets the component name (unique per participant × W instance).
        /// </summary>
        public string Name { get; }

        /// <inheritdoc/>
        public Receiver<PocTaggedEvent> InjectIn { get; }

        /// <inheritdoc/>
        public IProducer<PocCoincidenceC> CoincidenceOut => this.coincidenceOut;

        /// <inheritdoc/>
        public void Connect(string roleId, object producer)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                throw new ArgumentException("Port role id must be non-empty.", nameof(roleId));
            }

            if (producer == null)
            {
                throw new ArgumentNullException(nameof(producer));
            }

            if (this.connectedRoles.Contains(roleId))
            {
                throw new InvalidOperationException(
                    "Port role '" + roleId + "' is already connected on " + this.Name + ".");
            }

            // Optional/secondary catalog path — inject-only proof does not require it.
            if (string.Equals(roleId, PortRoleIds.SelectModule, StringComparison.Ordinal))
            {
                IProducer<string> typed = producer as IProducer<string>;
                if (typed == null)
                {
                    throw new InvalidOperationException(
                        "Role '" + PortRoleIds.SelectModule + "' on " + this.Name
                        + " requires IProducer<string>, got " + producer.GetType().FullName + ".");
                }

                // Catalog SelectModule is accepted but not part of A∧B⇒C science path.
                typed.Do((_, __) => { }, DeliveryPolicy.LatestMessage);
                this.connectedRoles.Add(roleId);
                return;
            }

            throw new ArgumentException(
                "Unknown port role '" + roleId + "' for Poc composition (inject-only; optional catalog: "
                + PortRoleIds.SelectModule + ").",
                nameof(roleId));
        }

        /// <inheritdoc/>
        public override string ToString() => this.Name;

        private void ProcessInject(PocTaggedEvent value, Envelope envelope)
        {
            if (value == null)
            {
                return;
            }

            this.injectBridge.Post(value, envelope.OriginatingTime);
        }
    }
}
