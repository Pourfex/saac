// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Graphs
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Minimal bindable Poc composition: declares <see cref="PortRoleIds.SelectModule"/> and counts messages.
    /// No Logigramme decision logic — Story 1.4 wiring proof only.
    /// </summary>
    public sealed class PocBindableComposition : IBindableComposition
    {
        private static readonly IReadOnlyList<string> RequiredRoles =
            new string[] { PortRoleIds.SelectModule };

        private readonly Pipeline pipeline;
        private readonly HashSet<string> connectedRoles;
        private int messageCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="PocBindableComposition"/> class.
        /// </summary>
        /// <param name="pipeline">Owning analysis pipeline.</param>
        /// <param name="participant">Participant this instance serves (AD-3).</param>
        public PocBindableComposition(Pipeline pipeline, ParticipantId participant)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            this.pipeline = pipeline;
            this.Participant = participant;
            this.connectedRoles = new HashSet<string>(StringComparer.Ordinal);
            this.Name = "Poc-" + participant;
            this.SelectModuleIn = this.pipeline.CreateReceiver<string>(
                this,
                this.ProcessSelectModule,
                this.Name + "-SelectModuleIn");
            this.MessageCountOut = this.pipeline.CreateEmitter<int>(
                this,
                this.Name + "-MessageCountOut");
        }

        /// <inheritdoc/>
        public string GraphId => "Poc";

        /// <inheritdoc/>
        public ParticipantId Participant { get; }

        /// <inheritdoc/>
        public IReadOnlyList<string> RequiredPortRoles => RequiredRoles;

        /// <summary>
        /// Gets the component name (unique per participant instance).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the SelectModule input receiver (catalog type <c>System.String</c>).
        /// </summary>
        public Receiver<string> SelectModuleIn { get; }

        /// <summary>
        /// Gets a trivial count emitter so both branches prove they received data.
        /// </summary>
        public Emitter<int> MessageCountOut { get; }

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

            if (string.Equals(roleId, PortRoleIds.SelectModule, StringComparison.Ordinal))
            {
                IProducer<string> typed = producer as IProducer<string>;
                if (typed == null)
                {
                    throw new InvalidOperationException(
                        "Role '" + PortRoleIds.SelectModule + "' on " + this.Name
                        + " requires IProducer<string>, got " + producer.GetType().FullName + ".");
                }

                typed.PipeTo(this.SelectModuleIn);
                this.connectedRoles.Add(roleId);
                return;
            }

            throw new ArgumentException(
                "Unknown port role '" + roleId + "' for Poc composition (required: "
                + PortRoleIds.SelectModule + ").",
                nameof(roleId));
        }

        /// <inheritdoc/>
        public override string ToString() => this.Name;

        private void ProcessSelectModule(string value, Envelope envelope)
        {
            this.messageCount++;
            this.MessageCountOut.Post(this.messageCount, envelope.OriginatingTime);
        }
    }
}
