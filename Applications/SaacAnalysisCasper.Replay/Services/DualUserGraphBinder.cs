// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Replay.Services
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using SAAC;
    using SAAC.PipelineServices;
    using SaacAnalysisCasper.Core.Config;
    using SaacAnalysisCasper.Core.Graphs;
    using SaacAnalysisCasper.Core.Mapping;
    using SaacAnalysisCasper.Core.Poc;

    /// <summary>
    /// Binds dual-user Core compositions to inject sources (and optional catalog connectors) after Replay load (AD-3 / AD-12).
    /// </summary>
    /// <remarks>
    /// Sweep orchestration (preferred): for each W in <see cref="AnalysisRunConfig.EnumerateWindowMs"/>,
    /// instantiate M1+M2 POC compositions closed over that W; one FullSpeed run; N CSV pairs.
    /// Inject is host-owned and fanned out per participant to all W instances — A∧B⇒C stays in Core.
    /// </remarks>
    public sealed class DualUserGraphBinder
    {
        private static readonly ParticipantId[] Participants = new[] { ParticipantId.M1, ParticipantId.M2 };

        private readonly LogStatus log;
        private readonly PortTopicMap portTopicMap;

        /// <summary>
        /// Initializes a new instance of the <see cref="DualUserGraphBinder"/> class.
        /// </summary>
        /// <param name="log">Host logging delegate.</param>
        /// <param name="portTopicMap">Core port-to-topic map (defaults to <see cref="PortTopicMap.CreateDefault"/>).</param>
        public DualUserGraphBinder(LogStatus log, PortTopicMap portTopicMap = null)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.portTopicMap = portTopicMap ?? PortTopicMap.CreateDefault();
        }

        /// <summary>
        /// Instantiates each configured graph for every resolved W × (M1/M2), wires synthetic inject, and binds optional catalog ports.
        /// Missing required catalog topics fail closed — no silent empty success.
        /// </summary>
        /// <param name="replay">Loaded Replay pipeline with connectors populated.</param>
        /// <param name="runConfig">Core run-config selecting graph ids and W list.</param>
        /// <param name="sessionName">Opened session name (for inject OT placement inside session interval).</param>
        /// <returns>Bound branch descriptors with exportable C emitters (M1/M2 kept separate).</returns>
        public IReadOnlyList<BoundBranchDescriptor> Bind(
            ReplayPipeline replay,
            AnalysisRunConfig runConfig,
            string sessionName)
        {
            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            if (runConfig == null)
            {
                throw new ArgumentNullException(nameof(runConfig));
            }

            if (string.IsNullOrWhiteSpace(sessionName))
            {
                throw new ArgumentException("sessionName must be non-empty.", nameof(sessionName));
            }

            if (replay.Pipeline == null)
            {
                throw new InvalidOperationException("ReplayPipeline.Pipeline is null; cannot bind dual-user graphs.");
            }

            if (replay.Connectors == null || replay.Connectors.Count == 0)
            {
                throw new InvalidOperationException(
                    "Replay connectors are empty after LoadDatasetAndConnectors; cannot open session shell for inject.");
            }

            if (runConfig.Graphs == null || runConfig.Graphs.Count == 0)
            {
                throw new InvalidOperationException("Run-config graphs must be non-empty before dual-user bind.");
            }

            IReadOnlyList<int> windowList = runConfig.EnumerateWindowMs();
            this.log(
                "Sweep orchestration: one FullSpeed run; for each W in ["
                + string.Join(", ", windowList)
                + "] instantiate M1+M2 POC closed over that W (N CSV pairs).");

            DateTime injectBase = ResolveInjectBaseOriginatingTime(replay, sessionName);
            this.log(
                "Poc inject schedule: A @ " + injectBase.ToString("o")
                + ", B_near +" + PocInjectSources.NearGapMs + "ms, B_far +"
                + PocInjectSources.FarGapMs + "ms (per participant, independent).");

            Dictionary<string, ConnectorInfo> topicIndex = BuildTopicIndex(replay.Connectors);
            this.log("Topic index built: " + topicIndex.Count + " stream(s) across "
                + replay.Connectors.Count + " store key(s).");

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

                    // One inject source per participant; fan-out to every W composition (never merge M1/M2).
                    IProducer<PocTaggedEvent> inject = PocInjectSources.Create(
                        replay.Pipeline,
                        participant,
                        injectBase);

                    for (int w = 0; w < windowList.Count; w++)
                    {
                        int windowMs = windowList[w];
                        IBindableComposition composition = CompositionFactory.Create(
                            graphId,
                            participant,
                            replay.Pipeline,
                            windowMs);

                        this.BindParticipant(composition, participant, topicIndex, replay.Pipeline);

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
            LogStatus log = this.log;
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

        private static DateTime ResolveInjectBaseOriginatingTime(ReplayPipeline replay, string sessionName)
        {
            if (replay.Dataset == null)
            {
                throw new InvalidOperationException("ReplayPipeline.Dataset is null; cannot place inject OriginatingTimes.");
            }

            Session? matched = null;
            foreach (Session session in replay.Dataset.Sessions)
            {
                if (session != null && string.Equals(session.Name, sessionName, StringComparison.Ordinal))
                {
                    matched = session;
                    break;
                }
            }

            if (matched == null)
            {
                throw new InvalidOperationException(
                    "Session '" + sessionName + "' not found when resolving inject base OriginatingTime.");
            }

            TimeInterval interval = matched.MessageOriginatingTimeInterval;
            if (interval == null || !interval.LeftEndpoint.Bounded)
            {
                throw new InvalidOperationException(
                    "Session '" + sessionName + "' has no bounded MessageOriginatingTimeInterval for inject placement.");
            }

            // Nest inject inside the session interval so FullSpeed ReplayDescriptor delivers it.
            DateTime baseTime = interval.Left.AddMilliseconds(500);
            DateTime lastInject = baseTime.AddMilliseconds(PocInjectSources.FarGapMs);
            if (interval.RightEndpoint.Bounded && lastInject > interval.Right)
            {
                // Fall back toward the left edge if the session is unexpectedly short.
                baseTime = interval.Left;
                lastInject = baseTime.AddMilliseconds(PocInjectSources.FarGapMs);
            }

            if (baseTime < interval.Left
                || (interval.RightEndpoint.Bounded && lastInject > interval.Right))
            {
                throw new InvalidOperationException(
                    "Session '" + sessionName + "' MessageOriginatingTimeInterval is too short to host the POC inject schedule "
                    + "(needs ≥ " + PocInjectSources.FarGapMs + " ms after inject base). "
                    + "interval=[" + interval.Left.ToString("o") + ", "
                    + (interval.RightEndpoint.Bounded ? interval.Right.ToString("o") : "∞") + "].");
            }

            return baseTime;
        }

        private static Dictionary<string, ConnectorInfo> BuildTopicIndex(
            Dictionary<string, Dictionary<string, ConnectorInfo>> connectorsByStore)
        {
            // Outer key is store name (DatasetLoader), not session name.
            Dictionary<string, ConnectorInfo> topicIndex =
                new Dictionary<string, ConnectorInfo>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Dictionary<string, ConnectorInfo>> storeEntry in connectorsByStore)
            {
                if (storeEntry.Value == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, ConnectorInfo> streamEntry in storeEntry.Value)
                {
                    if (streamEntry.Value == null)
                    {
                        throw new InvalidOperationException(
                            "Null ConnectorInfo for topic '" + streamEntry.Key
                            + "' under store key '" + storeEntry.Key + "'.");
                    }

                    if (topicIndex.ContainsKey(streamEntry.Key))
                    {
                        throw new InvalidOperationException(
                            "Duplicate topic '" + streamEntry.Key + "' across stores; refusing silent overwrite.");
                    }

                    topicIndex.Add(streamEntry.Key, streamEntry.Value);
                }
            }

            return topicIndex;
        }

        private void BindParticipant(
            IBindableComposition composition,
            ParticipantId participant,
            Dictionary<string, ConnectorInfo> topicIndex,
            Pipeline analysisPipeline)
        {
            IReadOnlyList<string> roles = composition.RequiredPortRoles;
            if (roles == null)
            {
                throw new InvalidOperationException(
                    "Composition RequiredPortRoles must be non-null (" + composition.GraphId + "/" + participant + ").");
            }

            // Empty roles = inject-only (Story 1.7). Catalog SelectModule is optional/secondary.
            for (int i = 0; i < roles.Count; i++)
            {
                string roleId = roles[i];
                string topic = this.portTopicMap.GetTopic(roleId, participant);

                if (!topicIndex.TryGetValue(topic, out ConnectorInfo info))
                {
                    throw new InvalidOperationException(
                        "Missing required topic '" + topic + "' for role '" + roleId
                        + "' participant '" + participant + "'.");
                }

                object bridge = CreateTypedBridge(info, analysisPipeline);
                composition.Connect(roleId, bridge);
                this.log(
                    "Bound " + participant + " role '" + roleId + "' -> topic '" + topic
                    + "' (" + info.DataType.FullName + ").");
            }
        }

        private static object CreateTypedBridge(ConnectorInfo info, Pipeline analysisPipeline)
        {
            if (info.DataType == null)
            {
                throw new InvalidOperationException(
                    "Connector '" + info.SourceName + "' has null DataType; cannot CreateBridge.");
            }

            MethodInfo createBridge = typeof(ConnectorInfo).GetMethod("CreateBridge");
            if (createBridge == null)
            {
                throw new InvalidOperationException("ConnectorInfo.CreateBridge method not found.");
            }

            MethodInfo typed = createBridge.MakeGenericMethod(info.DataType);
            try
            {
                object bridge = typed.Invoke(info, new object[] { analysisPipeline });
                if (bridge == null)
                {
                    throw new InvalidOperationException(
                        "CreateBridge returned null for topic '" + info.SourceName + "'.");
                }

                return bridge;
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException(
                    "CreateBridge failed for topic '" + info.SourceName + "'.",
                    ex.InnerException ?? ex);
            }
        }
    }
}
