// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Replay.Services
{
    using System;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// One dual-user branch after bind, with its exportable Core emitter.
    /// </summary>
    public sealed class BoundBranchDescriptor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BoundBranchDescriptor"/> class.
        /// </summary>
        /// <param name="graphId">Graph id (e.g. <c>Poc</c>).</param>
        /// <param name="participant">Participant branch.</param>
        /// <param name="messageCountOut">Exportable marker/count emitter.</param>
        public BoundBranchDescriptor(string graphId, ParticipantId participant, Emitter<int> messageCountOut)
        {
            if (string.IsNullOrWhiteSpace(graphId))
            {
                throw new ArgumentException("graphId must be non-empty.", nameof(graphId));
            }

            this.GraphId = graphId;
            this.Participant = participant;
            this.MessageCountOut = messageCountOut ?? throw new ArgumentNullException(nameof(messageCountOut));
        }

        /// <summary>
        /// Gets the graph id.
        /// </summary>
        public string GraphId { get; }

        /// <summary>
        /// Gets the participant branch.
        /// </summary>
        public ParticipantId Participant { get; }

        /// <summary>
        /// Gets the Core marker/count emitter for store + CSV export.
        /// </summary>
        public Emitter<int> MessageCountOut { get; }
    }
}
