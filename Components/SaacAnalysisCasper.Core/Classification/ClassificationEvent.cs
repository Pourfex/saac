// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Classification
{
    using System;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Single-label Piaget classification product (AD-7 / AD-13 Core analysis DTO).
    /// OriginatingTime policy: when posted or exported, time lives on the Psi envelope —
    /// not as a payload <see cref="System.DateTime"/> / Timestamp field.
    /// Wire labels are Alpha/Beta/Gamma only; sticky E→Gamma emit and D→no-emit
    /// (see <c>Classification/OptionC.md</c>).
    /// </summary>
    public sealed class ClassificationEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClassificationEvent"/> class.
        /// </summary>
        /// <param name="label">Classification label (Alpha, Beta, or Gamma).</param>
        /// <param name="participant">Participant branch that produced this outcome.</param>
        /// <param name="graphId">Graph id attribution (e.g. <c>Logigramme1</c>).</param>
        public ClassificationEvent(ClassificationLabel label, ParticipantId participant, string graphId)
        {
            if (!Enum.IsDefined(typeof(ClassificationLabel), label))
            {
                throw new ArgumentOutOfRangeException(nameof(label));
            }

            if (!Enum.IsDefined(typeof(ParticipantId), participant))
            {
                throw new ArgumentOutOfRangeException(nameof(participant));
            }

            if (string.IsNullOrWhiteSpace(graphId))
            {
                throw new ArgumentException("Graph id must be non-empty.", nameof(graphId));
            }

            this.Label = label;
            this.Participant = participant;
            this.GraphId = graphId;
        }

        /// <summary>
        /// Gets the classification label (Alpha, Beta, or Gamma).
        /// </summary>
        public ClassificationLabel Label { get; }

        /// <summary>
        /// Gets the participant branch that produced this outcome.
        /// </summary>
        public ParticipantId Participant { get; }

        /// <summary>
        /// Gets the graph id attribution for this outcome.
        /// </summary>
        public string GraphId { get; }
    }
}
