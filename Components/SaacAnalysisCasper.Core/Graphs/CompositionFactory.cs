// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Graphs
{
    using System;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Indices;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Creates Core composition instances by graph id for dual-user host binding.
    /// </summary>
    public static class CompositionFactory
    {
        private static readonly object SessionResetGate = new object();

        private static Pipeline? sessionResetForPipeline;

        /// <summary>
        /// Creates a new composition instance for the given graph and participant, closed over <paramref name="windowMs"/>.
        /// Call once per participant × W (AD-3: independent instances; never merge M1/M2).
        /// </summary>
        /// <param name="graphId">Graph id from run-config (e.g. <c>Poc</c>, <c>Logigramme1</c>).</param>
        /// <param name="participant">Participant branch (M1 or M2).</param>
        /// <param name="pipeline">Analysis pipeline that owns receivers/emitters.</param>
        /// <param name="windowMs">Window length W from AD-8 for this instance.</param>
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

            if (string.Equals(graphId, "Logigramme1", StringComparison.Ordinal))
            {
                // Session-global ModuleStatus seen-set: ResetSession once per analysis pipeline, not M1-only Apply.
                EnsureLogigramme1SessionReset(pipeline, participant);
                return new Logigramme1BindableComposition(pipeline, participant, windowMs);
            }

            throw new InvalidOperationException(
                "Unknown or unsupported graph id '" + graphId
                + "' for dual-user binding. Supported: Poc, Logigramme1.");
        }

        private static void EnsureLogigramme1SessionReset(Pipeline pipeline, ParticipantId participant)
        {
            lock (SessionResetGate)
            {
                // Reset once per analysis pipeline when M1 is created (or first Create if M2-only).
                // Reusing the same Pipeline for a new run that creates M1 again also resets.
                bool samePipeline = ReferenceEquals(sessionResetForPipeline, pipeline);
                if (samePipeline && participant != ParticipantId.M1)
                {
                    return;
                }

                if (samePipeline && participant == ParticipantId.M1)
                {
                    ModuleGenerationSuccessFilter.ResetSession();
                    return;
                }

                ModuleGenerationSuccessFilter.ResetSession();
                sessionResetForPipeline = pipeline;
            }
        }
    }
}
