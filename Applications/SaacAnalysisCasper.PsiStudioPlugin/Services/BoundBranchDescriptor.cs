// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.PsiStudioPlugin.Services
{
    using System;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Mapping;
    using SaacAnalysisCasper.Core.Poc;

    /// <summary>
    /// One dual-user × W branch after Plugin bind, with its exportable Core C emitter.
    /// </summary>
    public sealed class BoundBranchDescriptor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BoundBranchDescriptor"/> class.
        /// </summary>
        /// <param name="graphId">Graph id (e.g. <c>Poc</c>).</param>
        /// <param name="participant">Participant branch.</param>
        /// <param name="windowMs">Window length W closed over by this branch's POC.</param>
        /// <param name="coincidenceOut">Exportable coincidence C producer.</param>
        /// <param name="expectCoincidenceRows">
        /// When true, successful runs require ≥1 CSV data row; when false, zero C rows is valid science (Δ &gt; W).
        /// </param>
        public BoundBranchDescriptor(
            string graphId,
            ParticipantId participant,
            int windowMs,
            IProducer<PocCoincidenceC> coincidenceOut,
            bool expectCoincidenceRows)
        {
            if (string.IsNullOrWhiteSpace(graphId))
            {
                throw new ArgumentException("graphId must be non-empty.", nameof(graphId));
            }

            this.GraphId = graphId;
            this.Participant = participant;
            this.WindowMs = windowMs;
            this.CoincidenceOut = coincidenceOut ?? throw new ArgumentNullException(nameof(coincidenceOut));
            this.ExpectCoincidenceRows = expectCoincidenceRows;
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
        /// Gets the window length W for this branch.
        /// </summary>
        public int WindowMs { get; }

        /// <summary>
        /// Gets the Core coincidence C producer for store + CSV export.
        /// </summary>
        public IProducer<PocCoincidenceC> CoincidenceOut { get; }

        /// <summary>
        /// Gets a value indicating whether this branch's CSV must contain ≥1 data row on success.
        /// </summary>
        public bool ExpectCoincidenceRows { get; }
    }
}
