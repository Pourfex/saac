// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Replay.Services
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Microsoft.Psi;
    using SAAC;
    using SAAC.PipelineServices;
    using SaacAnalysisCasper.Core.Config;
    using SaacAnalysisCasper.Core.Graphs;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Binds dual-user Core compositions to catalog connectors after Replay load (AD-3 / AD-12).
    /// </summary>
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
        /// Instantiates each configured graph twice (M1/M2) and binds required ports from connectors.
        /// Missing required topics fail closed — no silent empty success.
        /// </summary>
        /// <param name="replay">Loaded Replay pipeline with connectors populated.</param>
        /// <param name="runConfig">Core run-config selecting graph ids.</param>
        public void Bind(ReplayPipeline replay, AnalysisRunConfig runConfig)
        {
            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            if (runConfig == null)
            {
                throw new ArgumentNullException(nameof(runConfig));
            }

            if (replay.Pipeline == null)
            {
                throw new InvalidOperationException("ReplayPipeline.Pipeline is null; cannot bind dual-user graphs.");
            }

            if (replay.Connectors == null || replay.Connectors.Count == 0)
            {
                throw new InvalidOperationException(
                    "Replay connectors are empty after LoadDatasetAndConnectors; cannot bind required ports.");
            }

            if (runConfig.Graphs == null || runConfig.Graphs.Length == 0)
            {
                throw new InvalidOperationException("Run-config graphs must be non-empty before dual-user bind.");
            }

            Dictionary<string, ConnectorInfo> topicIndex = BuildTopicIndex(replay.Connectors);
            this.log("Topic index built: " + topicIndex.Count + " stream(s) across "
                + replay.Connectors.Count + " store key(s).");

            HashSet<string> seenGraphIds = new HashSet<string>(StringComparer.Ordinal);
            string[] graphs = runConfig.Graphs;
            for (int g = 0; g < graphs.Length; g++)
            {
                string graphId = graphs[g];
                if (!seenGraphIds.Add(graphId))
                {
                    throw new InvalidOperationException(
                        "Duplicate graph id '" + graphId + "' in run-config; each graph may appear only once.");
                }

                this.log("Binding graph '" + graphId + "' for M1 and M2...");

                for (int p = 0; p < Participants.Length; p++)
                {
                    ParticipantId participant = Participants[p];
                    IBindableComposition composition = CompositionFactory.Create(
                        graphId,
                        participant,
                        replay.Pipeline);

                    this.BindParticipant(composition, participant, topicIndex, replay.Pipeline);
                    this.AttachProofSink(composition);
                }
            }
        }

        private void AttachProofSink(IBindableComposition composition)
        {
            PocBindableComposition poc = composition as PocBindableComposition;
            if (poc == null)
            {
                return;
            }

            LogStatus log = this.log;
            poc.MessageCountOut.Do(
                (count, envelope) =>
                {
                    if (count == 1 || (count % 50) == 0)
                    {
                        log(poc.Name + " received SelectModule count=" + count
                            + " @ " + envelope.OriginatingTime.ToString("o"));
                    }
                },
                DeliveryPolicy.Unlimited);
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
