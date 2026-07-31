// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Graphs
{
    using System;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Creates Core composition instances by graph id for dual-user host binding.
    /// </summary>
    public static class CompositionFactory
    {
        /// <summary>
        /// Creates a new composition instance for the given graph and participant, closed over <paramref name="windowMs"/>.
        /// Call once per participant × W (AD-3: independent instances; never merge M1/M2).
        /// </summary>
        /// <param name="graphId">Graph id from run-config (e.g. <c>Poc</c>).</param>
        /// <param name="participant">Participant branch (M1 or M2).</param>
        /// <param name="pipeline">Analysis pipeline that owns receivers/emitters.</param>
        /// <param name="windowMs">Window length W from AD-8 for this POC instance.</param>
        /// <returns>A fresh bindable composition instance.</returns>
        public static IBindableComposition Create(
            string graphId,
            ParticipantId participant,
            Pipeline pipeline,
            int windowMs)
        {
            if (string.IsNullOrWhiteSpace(graphId))
            {
                throw new ArgumentException("Graph id must be non-empty.", nameof(graphId));
            }

            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (string.Equals(graphId, "Poc", StringComparison.Ordinal))
            {
                return new PocBindableComposition(pipeline, participant, windowMs);
            }

            throw new InvalidOperationException(
                "Unknown or unsupported graph id '" + graphId + "' for dual-user binding. Supported in Story 1.7: Poc.");
        }
    }
}
