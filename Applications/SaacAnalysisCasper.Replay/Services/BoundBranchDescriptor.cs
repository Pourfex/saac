// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Replay.Services
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Classification;
    using SaacAnalysisCasper.Core.Mapping;
    using SaacAnalysisCasper.Core.Poc;

    /// <summary>
    /// One dual-user × W branch after bind, with optional Poc coincidence and/or classification exporters.
    /// </summary>
    public sealed class BoundBranchDescriptor
    {
        private static readonly ClassificationLabel[] EmptyClassificationLabels = Array.Empty<ClassificationLabel>();

        /// <summary>
        /// Initializes a new instance of the <see cref="BoundBranchDescriptor"/> class.
        /// </summary>
        /// <param name="graphId">Graph id (e.g. <c>Poc</c>, <c>Logigramme1</c>).</param>
        /// <param name="participant">Participant branch.</param>
        /// <param name="windowMs">Window length W closed over by this branch.</param>
        /// <param name="coincidenceOut">Exportable coincidence C producer (null for classification-only graphs).</param>
        /// <param name="expectCoincidenceRows">
        /// When true and <paramref name="coincidenceOut"/> is non-null, successful runs require ≥1 CSV data row;
        /// when false, zero C rows is valid science (Δ &gt; W). Ignored when coincidence is null.
        /// </param>
        /// <param name="classificationOut">Optional classification product (Logigramme1); null for Poc.</param>
        /// <param name="expectClassificationRows">
        /// When true and <paramref name="classificationOut"/> is non-null, successful known-trace hits require ≥1
        /// Classification CSV data row (fail-closed on zero). Ignored when classification is null.
        /// </param>
        /// <param name="expectedClassificationLabels">
        /// When non-empty with <paramref name="expectClassificationRows"/>, export gate requires every listed label
        /// among seen rows (empty for miss-only / no label gate).
        /// </param>
        /// <param name="forbiddenClassificationLabels">
        /// Labels that must not appear in Classification CSV rows (hits and misses); null/empty means none.
        /// </param>
        public BoundBranchDescriptor(
            string graphId,
            ParticipantId participant,
            int windowMs,
            IProducer<PocCoincidenceC>? coincidenceOut,
            bool expectCoincidenceRows,
            IProducer<ClassificationEvent>? classificationOut = null,
            bool expectClassificationRows = false,
            IReadOnlyList<ClassificationLabel>? expectedClassificationLabels = null,
            ClassificationLabel[]? forbiddenClassificationLabels = null)
        {
            if (string.IsNullOrWhiteSpace(graphId))
            {
                throw new ArgumentException("graphId must be non-empty.", nameof(graphId));
            }

            if (coincidenceOut == null && classificationOut == null)
            {
                throw new ArgumentException(
                    "BoundBranchDescriptor requires at least one export product: "
                    + "provide CoincidenceOut (Poc) and/or ClassificationOut (Logigramme1).");
            }

            this.GraphId = graphId;
            this.Participant = participant;
            this.WindowMs = windowMs;
            this.CoincidenceOut = coincidenceOut;
            this.ClassificationOut = classificationOut;
            this.ExpectCoincidenceRows = coincidenceOut != null && expectCoincidenceRows;
            this.ExpectClassificationRows = classificationOut != null && expectClassificationRows;
            this.ExpectedClassificationLabels = expectedClassificationLabels ?? EmptyClassificationLabels;
            this.ForbiddenClassificationLabels = forbiddenClassificationLabels ?? EmptyClassificationLabels;
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
        /// Gets the Core coincidence C producer for store + CSV export, or null when this branch has none.
        /// </summary>
        public IProducer<PocCoincidenceC>? CoincidenceOut { get; }

        /// <summary>
        /// Gets the classification product producer, or null when this branch has none (e.g. Poc).
        /// </summary>
        public IProducer<ClassificationEvent>? ClassificationOut { get; }

        /// <summary>
        /// Gets a value indicating whether this branch's coincidence CSV must contain ≥1 data row on success.
        /// Always false when <see cref="CoincidenceOut"/> is null.
        /// </summary>
        public bool ExpectCoincidenceRows { get; }

        /// <summary>
        /// Gets a value indicating whether this branch's Classification CSV must contain ≥1 data row on success
        /// (documented known-trace hits). Always false when <see cref="ClassificationOut"/> is null.
        /// </summary>
        public bool ExpectClassificationRows { get; }

        /// <summary>
        /// Gets expected Classification labels for known-trace hits (every label must appear); empty when none.
        /// </summary>
        public IReadOnlyList<ClassificationLabel> ExpectedClassificationLabels { get; }

        /// <summary>
        /// Gets labels that must not appear in Classification CSV rows (empty when none forbidden).
        /// </summary>
        public IReadOnlyList<ClassificationLabel> ForbiddenClassificationLabels { get; }
    }
}
