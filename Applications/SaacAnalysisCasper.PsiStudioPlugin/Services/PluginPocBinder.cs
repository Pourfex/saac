// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.PsiStudioPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Config;
    using SaacAnalysisCasper.Core.Graphs;
    using SaacAnalysisCasper.Core.Mapping;
    using SaacAnalysisCasper.Core.Poc;

    /// <summary>
    /// Binds dual-user Core Poc compositions to host inject on a plain <see cref="Pipeline"/> (AD-3 / AD-10).
    /// No Replay/catalog connectors — inject-only Plugin proof.
    /// </summary>
    public sealed class PluginPocBinder
    {
        private static readonly ParticipantId[] Participants = new[] { ParticipantId.M1, ParticipantId.M2 };

        private readonly Action<string> log;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginPocBinder"/> class.
        /// </summary>
        /// <param name="log">Host logging delegate.</param>
        public PluginPocBinder(Action<string> log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// Instantiates each configured graph for every resolved W × (M1/M2) and wires synthetic inject.
        /// </summary>
        /// <param name="pipeline">Analysis pipeline (Plugin-owned; not ReplayPipeline).</param>
        /// <param name="runConfig">Core run-config selecting graph ids and W list.</param>
        /// <param name="injectBase">Deterministic OriginatingTime for A (science clock).</param>
        /// <returns>Bound branch descriptors with exportable C emitters (M1/M2 kept separate).</returns>
        public IReadOnlyList<BoundBranchDescriptor> Bind(
            Pipeline pipeline,
            AnalysisRunConfig runConfig,
            DateTime injectBase)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (runConfig == null)
            {
                throw new ArgumentNullException(nameof(runConfig));
            }

            if (runConfig.Graphs == null || runConfig.Graphs.Count == 0)
            {
                throw new InvalidOperationException("Run-config graphs must be non-empty before Plugin Poc bind.");
            }

            IReadOnlyList<int> windowList = runConfig.EnumerateWindowMs();
            this.log(
                "Plugin sweep: one FullSpeed run; for each W in ["
                + string.Join(", ", windowList)
                + "] instantiate M1+M2 POC closed over that W.");

            this.log(
                "Poc inject schedule: A @ " + injectBase.ToString("o")
                + ", B_near +" + PocInjectSources.NearGapMs + "ms, B_far +"
                + PocInjectSources.FarGapMs + "ms (per participant, independent).");

            List<BoundBranchDescriptor> boundBranches = new List<BoundBranchDescriptor>();
            HashSet<string> seenGraphIds = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<string> graphs = runConfig.Graphs;
            for (int g = 0; g < graphs.Count; g++)
            {
                string graphId = graphs[g];
                if (!seenGraphIds.Add(graphId))
                {
                    throw new InvalidOperationException(
                        "Duplicate graph id '" + graphId + "' in run-config; each graph may appear only once.");
                }

                this.log("Binding graph '" + graphId + "' for M1 and M2 across " + windowList.Count + " W value(s)...");

                for (int p = 0; p < Participants.Length; p++)
                {
                    ParticipantId participant = Participants[p];

                    IProducer<PocTaggedEvent> inject = PocInjectSources.Create(
                        pipeline,
                        participant,
                        injectBase);

                    for (int w = 0; w < windowList.Count; w++)
                    {
                        int windowMs = windowList[w];
                        IBindableComposition composition = CompositionFactory.Create(
                            graphId,
                            participant,
                            pipeline,
                            windowMs);

                        IPocExportSurface pocSurface = composition as IPocExportSurface;
                        if (pocSurface == null)
                        {
                            throw new InvalidOperationException(
                                "Graph '" + graphId + "' did not expose IPocExportSurface for participant "
                                + participant + " W=" + windowMs + ".");
                        }

                        inject.PipeTo(pocSurface.InjectIn, DeliveryPolicy.Unlimited);
                        this.AttachProofSink(pocSurface, composition.GraphId, participant, windowMs);

                        boundBranches.Add(new BoundBranchDescriptor(
                            composition.GraphId,
                            participant,
                            windowMs,
                            pocSurface.CoincidenceOut,
                            PocInjectSources.ExpectsCoincidenceRows(windowMs)));
                    }
                }
            }

            return boundBranches;
        }

        private void AttachProofSink(
            IPocExportSurface pocSurface,
            string graphId,
            ParticipantId participant,
            int windowMs)
        {
            Action<string> log = this.log;
            pocSurface.CoincidenceOut.Do(
                (PocCoincidenceC coincidence, Envelope envelope) =>
                {
                    log(
                        "Poc C emitted: graph=" + graphId
                        + " participant=" + participant
                        + " W=" + windowMs
                        + " @ " + envelope.OriginatingTime.ToString("o"));
                },
                DeliveryPolicy.Unlimited);
        }
    }
}
