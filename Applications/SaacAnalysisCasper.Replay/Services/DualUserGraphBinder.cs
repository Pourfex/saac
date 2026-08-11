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
    using SaacAnalysisCasper.Core.Classification;
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
                + "] instantiate M1+M2 compositions closed over that W.");

            IReadOnlyList<string> graphs = runConfig.Graphs;
            bool needsPocInject = ContainsGraphId(graphs, "Poc");
            string knownTraceScenario = runConfig.KnownTraceScenario;
            bool needsLogigramme1KnownTrace = ContainsGraphId(graphs, "Logigramme1")
                && !string.IsNullOrWhiteSpace(knownTraceScenario);

            if (needsPocInject && needsLogigramme1KnownTrace)
            {
                throw new InvalidOperationException(
                    "Cannot mix Poc inject with Logigramme1 known-trace in one run. "
                    + "Use graphs:[\"Poc\"] alone, or graphs:[\"Logigramme1\"] with knownTraceScenario.");
            }

            if (needsLogigramme1KnownTrace && !Logigramme1InjectSources.IsKnownScenario(knownTraceScenario))
            {
                throw new InvalidOperationException(
                    "Unknown knownTraceScenario '" + knownTraceScenario
                    + "'. See Logigramme1KnownTraceScenarios.md for documented ids.");
            }

            int injectSpanMs = 0;
            if (needsPocInject)
            {
                injectSpanMs = Math.Max(injectSpanMs, PocInjectSources.FarGapMs);
            }

            if (needsLogigramme1KnownTrace)
            {
                injectSpanMs = Math.Max(injectSpanMs, Logigramme1InjectSources.ScheduleSpanMs);
            }

            DateTime injectBase = default;
            if (injectSpanMs > 0)
            {
                injectBase = ResolveInjectBaseOriginatingTime(replay, sessionName, injectSpanMs);
                if (needsPocInject)
                {
                    this.log(
                        "Poc inject schedule: A @ " + injectBase.ToString("o")
                        + ", B_near +" + PocInjectSources.NearGapMs + "ms, B_far +"
                        + PocInjectSources.FarGapMs + "ms (per participant, independent).");
                }

                if (needsLogigramme1KnownTrace)
                {
                    this.log(
                        "Logigramme1 known-trace scenario '" + knownTraceScenario
                        + "' inject base @ " + injectBase.ToString("o")
                        + " (span ≤ " + Logigramme1InjectSources.ScheduleSpanMs
                        + " ms; per participant, independent).");
                }
            }
            else
            {
                this.log("Synthetic inject skipped (no Poc; Logigramme1 catalog mode or not selected).");
            }

            Dictionary<string, ConnectorInfo> topicIndex = BuildTopicIndex(replay.Connectors);
            this.log("Topic index built: " + topicIndex.Count + " stream(s) across "
                + replay.Connectors.Count + " store key(s).");

            List<BoundBranchDescriptor> boundBranches = new List<BoundBranchDescriptor>();
            HashSet<string> seenGraphIds = new HashSet<string>(StringComparer.Ordinal);
            for (int g = 0; g < graphs.Count; g++)
            {
                string graphId = graphs[g];
                if (!seenGraphIds.Add(graphId))
                {
                    throw new InvalidOperationException(
                        "Duplicate graph id '" + graphId + "' in run-config; each graph may appear only once.");
                }

                this.log("Binding graph '" + graphId + "' for M1 and M2 across " + windowList.Count + " W value(s)...");

                if (string.Equals(graphId, "Poc", StringComparison.Ordinal))
                {
                    this.BindPocGraph(replay, injectBase, topicIndex, windowList, boundBranches);
                }
                else if (string.Equals(graphId, "Logigramme1", StringComparison.Ordinal))
                {
                    this.BindLogigramme1Graph(
                        replay,
                        topicIndex,
                        windowList,
                        boundBranches,
                        knownTraceScenario,
                        injectBase);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Unknown or unsupported graph id '" + graphId
                        + "' for dual-user bind. Supported: Poc, Logigramme1.");
                }
            }

            return boundBranches;
        }

        private static bool ContainsGraphId(IReadOnlyList<string> graphs, string graphId)
        {
            for (int i = 0; i < graphs.Count; i++)
            {
                if (string.Equals(graphs[i], graphId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void BindPocGraph(
            ReplayPipeline replay,
            DateTime injectBase,
            Dictionary<string, ConnectorInfo> topicIndex,
            IReadOnlyList<int> windowList,
            List<BoundBranchDescriptor> boundBranches)
        {
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
                        "Poc",
                        participant,
                        replay.Pipeline,
                        windowMs);

                    this.BindParticipant(composition, participant, topicIndex, replay.Pipeline);

                    IPocExportSurface pocSurface = composition as IPocExportSurface;
                    if (pocSurface == null)
                    {
                        throw new InvalidOperationException(
                            "Graph 'Poc' did not expose IPocExportSurface for participant "
                            + participant + " W=" + windowMs + ".");
                    }

                    inject.PipeTo(pocSurface.InjectIn, DeliveryPolicy.Unlimited);
                    this.AttachProofSink(pocSurface, composition.GraphId, participant, windowMs);

                    boundBranches.Add(new BoundBranchDescriptor(
                        composition.GraphId,
                        participant,
                        windowMs,
                        pocSurface.CoincidenceOut,
                        PocInjectSources.ExpectsCoincidenceRows(windowMs),
                        classificationOut: null));
                }
            }
        }

        private void BindLogigramme1Graph(
            ReplayPipeline replay,
            Dictionary<string, ConnectorInfo> topicIndex,
            IReadOnlyList<int> windowList,
            List<BoundBranchDescriptor> boundBranches,
            string knownTraceScenario,
            DateTime injectBase)
        {
            bool knownTraceMode = !string.IsNullOrWhiteSpace(knownTraceScenario);

            for (int p = 0; p < Participants.Length; p++)
            {
                ParticipantId participant = Participants[p];

                // One inject bundle per participant; fan-out to every W composition (never merge M1/M2).
                Logigramme1InjectSources.InjectBundle injectBundle = null;
                if (knownTraceMode)
                {
                    injectBundle = Logigramme1InjectSources.Create(
                        replay.Pipeline,
                        participant,
                        knownTraceScenario,
                        injectBase);
                }

                for (int w = 0; w < windowList.Count; w++)
                {
                    int windowMs = windowList[w];
                    IBindableComposition composition = CompositionFactory.Create(
                        "Logigramme1",
                        participant,
                        replay.Pipeline,
                        windowMs);

                    if (knownTraceMode)
                    {
                        injectBundle.ConnectTo(composition);
                        this.log(
                            "Known-trace Connect " + participant + " scenario '" + knownTraceScenario
                            + "' W=" + windowMs + ".");
                    }
                    else
                    {
                        this.BindParticipant(composition, participant, topicIndex, replay.Pipeline);
                    }

                    IClassificationExportSurface classificationSurface = composition as IClassificationExportSurface;
                    if (classificationSurface == null)
                    {
                        throw new InvalidOperationException(
                            "Graph 'Logigramme1' did not expose IClassificationExportSurface for participant "
                            + participant + " W=" + windowMs + ".");
                    }

                    this.AttachClassificationProofSink(
                        classificationSurface,
                        composition.GraphId,
                        participant,
                        windowMs);

                    bool expectClassificationRows = false;
                    IReadOnlyList<ClassificationLabel> expectedClassificationLabels = Array.Empty<ClassificationLabel>();
                    ClassificationLabel[]? forbiddenClassificationLabels = null;
                    if (knownTraceMode)
                    {
                        Logigramme1InjectSources.ScenarioMeta scenarioMeta;
                        if (!Logigramme1InjectSources.TryGetMeta(knownTraceScenario, out scenarioMeta))
                        {
                            throw new InvalidOperationException(
                                "Unknown knownTraceScenario '" + knownTraceScenario
                                + "'; cannot resolve Classification export gates.");
                        }

                        expectClassificationRows = scenarioMeta.ExpectClassificationRows;
                        expectedClassificationLabels = scenarioMeta.ExpectedLabels;
                        forbiddenClassificationLabels = scenarioMeta.ForbiddenLabels;
                    }

                    boundBranches.Add(new BoundBranchDescriptor(
                        composition.GraphId,
                        participant,
                        windowMs,
                        coincidenceOut: null,
                        expectCoincidenceRows: false,
                        classificationOut: classificationSurface.ClassificationOut,
                        expectClassificationRows: expectClassificationRows,
                        expectedClassificationLabels: expectedClassificationLabels,
                        forbiddenClassificationLabels: forbiddenClassificationLabels));
                }
            }
        }

        private void AttachClassificationProofSink(
            IClassificationExportSurface surface,
            string graphId,
            ParticipantId participant,
            int windowMs)
        {
            LogStatus log = this.log;
            surface.ClassificationOut.Do(
                (ClassificationEvent classification, Envelope envelope) =>
                {
                    if (classification == null)
                    {
                        return;
                    }

                    log(
                        "Classification emitted: graph=" + graphId
                        + " participant=" + participant
                        + " W=" + windowMs
                        + " label=" + classification.Label
                        + " @ " + envelope.OriginatingTime.ToString("o"));
                },
                DeliveryPolicy.Unlimited);
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

        private static DateTime ResolveInjectBaseOriginatingTime(
            ReplayPipeline replay,
            string sessionName,
            int scheduleSpanMs)
        {
            if (replay.Dataset == null)
            {
                throw new InvalidOperationException("ReplayPipeline.Dataset is null; cannot place inject OriginatingTimes.");
            }

            if (scheduleSpanMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scheduleSpanMs), "scheduleSpanMs must be ≥ 0.");
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

            if (interval.IsFinite && interval.Left.CompareTo(interval.Right) > 0)
            {
                throw new InvalidOperationException(
                    "Session '" + sessionName + "' MessageOriginatingTimeInterval is inverted (Left > Right).");
            }

            // Nest inject inside the session interval so FullSpeed ReplayDescriptor delivers it.
            // Prefer Left+500ms; fall back toward the inclusive interior if the session is short.
            DateTime baseTime = interval.Left.AddMilliseconds(500);
            DateTime midInject = baseTime.AddMilliseconds(Math.Min(scheduleSpanMs, PocInjectSources.NearGapMs));
            DateTime lastInject = baseTime.AddMilliseconds(scheduleSpanMs);
            if (!ScheduleFitsSessionInterval(interval, baseTime, midInject, lastInject))
            {
                if (interval.LeftEndpoint.Inclusive)
                {
                    baseTime = interval.Left;
                }
                else
                {
                    // Exclusive Left: first deliverable tick after the edge.
                    baseTime = interval.Left.AddTicks(1);
                }

                midInject = baseTime.AddMilliseconds(Math.Min(scheduleSpanMs, PocInjectSources.NearGapMs));
                lastInject = baseTime.AddMilliseconds(scheduleSpanMs);
            }

            if (!ScheduleFitsSessionInterval(interval, baseTime, midInject, lastInject))
            {
                throw new InvalidOperationException(
                    "Session '" + sessionName + "' MessageOriginatingTimeInterval is too short to host the inject schedule "
                    + "(needs ≥ " + scheduleSpanMs + " ms after inject base, respecting endpoint inclusivity). "
                    + "interval=[" + interval.Left.ToString("o") + ", "
                    + (interval.RightEndpoint.Bounded ? interval.Right.ToString("o") : "∞") + "]"
                    + " LeftInclusive=" + interval.LeftEndpoint.Inclusive
                    + " RightInclusive=" + interval.RightEndpoint.Inclusive + ".");
            }

            return baseTime;
        }

        private static bool ScheduleFitsSessionInterval(
            TimeInterval interval,
            DateTime baseTime,
            DateTime midInject,
            DateTime lastInject)
        {
            if (!interval.PointIsWithin(baseTime) || !interval.PointIsWithin(midInject))
            {
                return false;
            }

            // Unbounded Right: last inject always fits once base and mid are inside.
            if (!interval.RightEndpoint.Bounded)
            {
                return true;
            }

            return interval.PointIsWithin(lastInject);
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
                    throw new InvalidOperationException(
                        "Null connector map under store key '" + storeEntry.Key
                        + "'; refusing to drop required topics.");
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
                if (string.IsNullOrWhiteSpace(roleId))
                {
                    throw new InvalidOperationException(
                        "Invalid required port role id (null/whitespace) for composition "
                        + composition.GraphId + "/" + participant + ".");
                }

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

            MethodInfo createBridge;
            MethodInfo typed;
            try
            {
                createBridge = typeof(ConnectorInfo).GetMethod("CreateBridge");
                if (createBridge == null)
                {
                    throw new InvalidOperationException("ConnectorInfo.CreateBridge method not found.");
                }

                typed = createBridge.MakeGenericMethod(info.DataType);
            }
            catch (AmbiguousMatchException ex)
            {
                throw new InvalidOperationException(
                    "CreateBridge overload resolution failed for topic '" + info.SourceName + "'.",
                    ex);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    "CreateBridge cannot be constructed for topic '" + info.SourceName
                    + "' with DataType '" + info.DataType.FullName + "'.",
                    ex);
            }

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
