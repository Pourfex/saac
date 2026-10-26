// <copyright file="FirstUnitPipelineRunner.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Security;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using SAAC.Logigram1;
    using SAAC.Logigram1.Logic;

    /// <summary>
    /// FullSpeed importer pipeline: enabled Logic units (FirstUnit and/or Node3) + designated Outcome
    /// writer → one analysis PsiStore partition + Dataset <c>.pds</c> (PERSIST / REPLAY locked decisions).
    /// Multi-unit: shared State/Outcome store; topic union; clear-before-write; SESSION_ATTR.
    /// </summary>
    public static class FirstUnitPipelineRunner
    {
        /// <summary>Partition / store name written into the analysis Dataset.</summary>
        public const string AnalysisStoreName = "Logigram1Analysis";

        /// <summary>
        /// Runs enabled Logic units over their AD-7 topics, pipes evidence and Gamma decisions to the writer,
        /// persists <see cref="LogigramState.StreamName"/> and <see cref="LogigramOutcome.StreamName"/>,
        /// and saves the analysis Dataset at <paramref name="resolvedOutputPds"/>.
        /// </summary>
        /// <param name="dataset">Loaded input Dataset (topics already verified present).</param>
        /// <param name="batterySegments">Discovered Spawn→Finish segments for BatteryId stamping.</param>
        /// <param name="resolvedOutputPds">Validated absolute output <c>.pds</c> path.</param>
        /// <param name="inputStorePath">Absolute input Casper <c>.pds</c> (SESSION_ATTR session name).</param>
        /// <param name="enabledUnits">Enabled unit ids (FirstUnit and/or Node3); at least one required.</param>
        /// <param name="findStreamByExactName">Stream lookup (defaults to console helper).</param>
        /// <exception cref="InvalidOperationException">Missing topic stream or persist failure.</exception>
        public static void RunAndPersist(
            Dataset dataset,
            IReadOnlyList<BatterySegment> batterySegments,
            string resolvedOutputPds,
            string inputStorePath,
            IReadOnlyList<string> enabledUnits,
            Func<Dataset, string, IStreamMetadata> findStreamByExactName = null)
        {
            if (dataset == null)
            {
                throw new ArgumentNullException(nameof(dataset));
            }

            if (string.IsNullOrWhiteSpace(resolvedOutputPds))
            {
                throw new ArgumentException("Resolved output Dataset path is required.", nameof(resolvedOutputPds));
            }

            bool firstUnitEnabled = IsUnitEnabled(enabledUnits, Logigram1UnitTopicRegistry.FirstUnitId);
            bool node3Enabled = IsUnitEnabled(enabledUnits, Logigram1UnitTopicRegistry.Node3UnitId);
            if (!firstUnitEnabled && !node3Enabled)
            {
                throw new InvalidOperationException(
                    "RunAndPersist requires at least one of Logigram1.FirstUnit or Logigram1.Node3 enabled.");
            }

            Func<Dataset, string, IStreamMetadata> lookup = findStreamByExactName
                ?? OfflineCasperStoreConsole.FindStreamByExactName;

            IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> scopes =
                (batterySegments ?? Array.Empty<BatterySegment>())
                .Select(segment => (segment.BatteryId, segment.StartOriginatingTime, segment.EndOriginatingTime))
                .ToList();

            string storeRoot = AnalysisOutputPath.ResolveStoreRoot(resolvedOutputPds);

            // Re-run must not leave stale partition files from a previous analysis write.
            if (Directory.Exists(storeRoot))
            {
                Directory.Delete(storeRoot, recursive: true);
            }

            Directory.CreateDirectory(storeRoot);

            IReadOnlyList<string> topicsToAttach = Logigram1UnitTopicRegistry.TryResolveTopicUnion(
                enabledUnits,
                out string unknownUnitId);
            if (unknownUnitId != null || topicsToAttach == null)
            {
                throw new InvalidOperationException(
                    $"Unknown enabled Logic unit id \"{unknownUnitId ?? "(null)"}\": no topic registry entry.");
            }

            using (Pipeline pipeline = Pipeline.Create(nameof(FirstUnitPipelineRunner)))
            {
                var writer = new LogigramOutcomeWriter(pipeline);
                Logigram1FirstUnit firstUnit = null;
                Logigram1Node3Unit node3Unit = null;
                var stateProducers = new List<IProducer<LogigramState>>();
                var regulationProducers = new List<IProducer<(LogigramRegulation Regulation, int BatteryId)>>();
                bool evidenceWired = false;

                if (firstUnitEnabled)
                {
                    firstUnit = new Logigram1FirstUnit(pipeline, scopes);
                    firstUnit.EvidenceOut.PipeTo(writer.EvidenceIn);
                    evidenceWired = true;
                    stateProducers.Add(firstUnit.StateOut);
                    regulationProducers.Add(firstUnit.RegulationDecisionWithBatteryOut);
                }

                if (node3Enabled)
                {
                    node3Unit = new Logigram1Node3Unit(pipeline, scopes);
                    if (evidenceWired)
                    {
                        node3Unit.EvidenceOut.PipeTo(writer.AddEvidenceInput());
                    }
                    else
                    {
                        node3Unit.EvidenceOut.PipeTo(writer.EvidenceIn);
                    }

                    stateProducers.Add(node3Unit.StateOut);
                    regulationProducers.Add(node3Unit.RegulationDecisionWithBatteryOut);
                }

                WireMergedRegulation(regulationProducers, writer);
                IProducer<LogigramState> mergedState = WireMergedState(stateProducers);

                var importers = new Dictionary<string, PsiImporter>(StringComparer.Ordinal);
                var attachedTopics = new HashSet<string>(StringComparer.Ordinal);

                foreach (string topic in topicsToAttach)
                {
                    if (!attachedTopics.Add(topic))
                    {
                        continue;
                    }

                    IStreamMetadata metadata = lookup(dataset, topic);
                    if (metadata == null)
                    {
                        throw new InvalidOperationException(
                            $"Required Logic topic stream missing at run time: {topic}.");
                    }

                    PsiImporter importer = GetOrOpenImporter(pipeline, importers, metadata);
                    AttachTopic(importer, firstUnit, node3Unit, topic, metadata.Name);
                }

                PsiExporter exporter = PsiStore.Create(pipeline, AnalysisStoreName, storeRoot);
                DateTime lastStateWrite = DateTime.MinValue;
                mergedState
                    .Process<LogigramState, LogigramState>(
                        (state, envelope, emitter) =>
                        {
                            DateTime time = envelope.OriginatingTime;
                            if (time <= lastStateWrite)
                            {
                                time = lastStateWrite.AddTicks(1);
                            }

                            lastStateWrite = time;
                            emitter.Post(state, time);
                        },
                        DeliveryPolicy.Unlimited)
                    .Write(LogigramState.StreamName, exporter);
                writer.Out.Write(LogigramOutcome.StreamName, exporter);

                // REPLAY = FullSpeed over enabled units + Battery-scoped derivation (importer-driven).
                Exception pipelineError = null;
                pipeline.PipelineExceptionNotHandled += (_, args) =>
                {
                    pipelineError = args.Exception;
                };
                try
                {
                    pipeline.Run(ReplayDescriptor.ReplayAll);
                }
                catch (AggregateException aggregate) when (pipelineError != null || aggregate.InnerExceptions.Count > 0)
                {
                    Exception root = pipelineError ?? aggregate.Flatten().InnerException ?? aggregate;
                    throw new InvalidOperationException(
                        $"Logic FullSpeed pipeline failed: {root.GetType().Name}: {root.Message}",
                        root);
                }

                if (pipelineError != null)
                {
                    throw new InvalidOperationException(
                        $"Logic FullSpeed pipeline failed: {pipelineError.GetType().Name}: {pipelineError.Message}",
                        pipelineError);
                }
            }

            SaveAnalysisDataset(resolvedOutputPds, storeRoot, inputStorePath);
        }

        /// <summary>
        /// SESSION_ATTR A: builds the analysis Dataset session name from the input store path and id
        /// (<c>.pds</c> filename), matching the stdout identity fields.
        /// </summary>
        /// <param name="inputStorePath">Input Casper <c>.pds</c> path (preferably absolute).</param>
        /// <returns>Session name of the form <c>path=… id=…</c>.</returns>
        public static string FormatAnalysisSessionName(string inputStorePath)
        {
            string path = inputStorePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    path = Path.GetFullPath(path);
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                    || exception is PathTooLongException
                    || exception is NotSupportedException
                    || exception is SecurityException)
                {
                    // Keep the raw path when normalization fails.
                }
            }

            string id;
            try
            {
                id = Path.GetFileName(path);
            }
            catch (ArgumentException)
            {
                id = path;
            }

            if (string.IsNullOrEmpty(id))
            {
                id = path;
            }

            return $"path={path} id={id}";
        }

        /// <summary>
        /// Whether <paramref name="enabledUnits"/> contains the given unit id (ordinal, trimmed).
        /// </summary>
        /// <param name="enabledUnits">Enabled unit ids from config.</param>
        /// <param name="unitId">Canonical unit id to look for.</param>
        /// <returns>True when the unit id is listed.</returns>
        public static bool IsUnitEnabled(IEnumerable<string> enabledUnits, string unitId)
        {
            if (enabledUnits == null || string.IsNullOrEmpty(unitId))
            {
                return false;
            }

            return enabledUnits.Any(raw =>
                string.Equals(raw?.Trim(), unitId, StringComparison.Ordinal));
        }

        private static void WireMergedRegulation(
            List<IProducer<(LogigramRegulation Regulation, int BatteryId)>> producers,
            LogigramOutcomeWriter writer)
        {
            if (producers == null || producers.Count == 0)
            {
                throw new InvalidOperationException("At least one regulation producer is required.");
            }

            if (producers.Count == 1)
            {
                producers[0].PipeTo(writer.RegulationDecisionWithBatteryIn);
                return;
            }

            // Same OT bump as merged State: equal FirstUnit+Node3 times must not break writer.Post.
            DateTime lastRegulationWrite = DateTime.MinValue;
            producers[0]
                .Merge(producers[1], DeliveryPolicy.Unlimited)
                .Select(message => message.Data, DeliveryPolicy.Unlimited)
                .Process<(LogigramRegulation Regulation, int BatteryId), (LogigramRegulation Regulation, int BatteryId)>(
                    (decision, envelope, emitter) =>
                    {
                        DateTime time = envelope.OriginatingTime;
                        if (time <= lastRegulationWrite)
                        {
                            time = lastRegulationWrite.AddTicks(1);
                        }

                        lastRegulationWrite = time;
                        emitter.Post(decision, time);
                    },
                    DeliveryPolicy.Unlimited)
                .PipeTo(writer.RegulationDecisionWithBatteryIn);
        }

        private static IProducer<LogigramState> WireMergedState(List<IProducer<LogigramState>> producers)
        {
            if (producers == null || producers.Count == 0)
            {
                throw new InvalidOperationException("At least one State producer is required.");
            }

            if (producers.Count == 1)
            {
                return producers[0];
            }

            return producers[0]
                .Merge(producers[1], DeliveryPolicy.Unlimited)
                .Select(message => message.Data, DeliveryPolicy.Unlimited);
        }

        private static PsiImporter GetOrOpenImporter(
            Pipeline pipeline,
            Dictionary<string, PsiImporter> importers,
            IStreamMetadata metadata)
        {
            string key = metadata.StorePath + "\0" + metadata.StoreName;
            if (!importers.TryGetValue(key, out PsiImporter importer))
            {
                importer = PsiStore.Open(pipeline, metadata.StoreName, metadata.StorePath);
                importers[key] = importer;
            }

            return importer;
        }

        private static void AttachTopic(
            PsiImporter importer,
            Logigram1FirstUnit firstUnit,
            Logigram1Node3Unit node3Unit,
            string topicName,
            string streamName)
        {
            // Shared topics (Porte) may feed both units; others are unit-specific.
            bool isFirstUnitTopic = firstUnit != null
                && Logigram1UnitTopicRegistry.FirstUnitTopics.Contains(topicName);
            bool isNode3Topic = node3Unit != null
                && Logigram1UnitTopicRegistry.Node3Topics.Contains(topicName);

            if (!isFirstUnitTopic && !isNode3Topic)
            {
                throw new InvalidOperationException(
                    $"No catalogue type mapping for Logic topic \"{topicName}\".");
            }

            switch (topicName)
            {
                case "Module status":
                    importer.OpenStream<ValueTuple<int, string>>(streamName)
                        .PipeTo(firstUnit.AddTopicInput<ValueTuple<int, string>>(topicName));
                    break;

                case "Porte1 ouverture":
                case "Porte2 ouverture":
                    {
                        // Tee via Select so FirstUnit + Node3 each get a dedicated branch (shared Porte).
                        var doorStream = importer.OpenStream<ValueTuple<bool, Vector3>>(streamName);
                        if (isFirstUnitTopic)
                        {
                            doorStream
                                .Select(payload => payload, DeliveryPolicy.Unlimited)
                                .PipeTo(firstUnit.AddTopicInput<ValueTuple<bool, Vector3>>(topicName));
                        }

                        if (isNode3Topic)
                        {
                            doorStream
                                .Select(payload => payload, DeliveryPolicy.Unlimited)
                                .PipeTo(node3Unit.AddTopicInput<ValueTuple<bool, Vector3>>(topicName));
                        }

                        break;
                    }

                case "M1-SelectModule":
                case "M2-SelectModule":
                    importer.OpenStream<string>(streamName)
                        .PipeTo(firstUnit.AddTopicInput<string>(topicName));
                    break;

                case "M1-Validation":
                case "M2-Validation":
                    importer.OpenStream<bool>(streamName)
                        .PipeTo(firstUnit.AddTopicInput<bool>(topicName));
                    break;

                case "Area1":
                case "Area2":
                    importer.OpenStream<ValueTuple<int, bool, string>>(streamName)
                        .PipeTo(node3Unit.AddTopicInput<ValueTuple<int, bool, string>>(topicName));
                    break;

                case "Object Bound":
                    importer.OpenStream<ValueTuple<string, Vector3, Vector3>>(streamName)
                        .PipeTo(node3Unit.AddTopicInput<ValueTuple<string, Vector3, Vector3>>(topicName));
                    break;

                case "1-LeftWrist":
                case "1-RightWrist":
                case "2-LeftWrist":
                case "2-RightWrist":
                    importer.OpenStream<Tuple<Vector3, Vector3>>(streamName)
                        .PipeTo(node3Unit.AddTopicInput<Tuple<Vector3, Vector3>>(topicName));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"No catalogue type mapping for Logic topic \"{topicName}\".");
            }
        }

        private static void SaveAnalysisDataset(
            string resolvedOutputPds,
            string storeRoot,
            string inputStorePath)
        {
            string directory = Path.GetDirectoryName(resolvedOutputPds);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Stage then replace so a failed AddPartition/Save leaves the previous .pds intact.
            string stagingPds = resolvedOutputPds + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                var dataset = new Dataset(
                    Path.GetFileNameWithoutExtension(resolvedOutputPds),
                    stagingPds,
                    autoSave: true);

                // SESSION_ATTR A: encode input store path/id (not hard-coded "Session").
                Session session = dataset.AddEmptySession(FormatAnalysisSessionName(inputStorePath));
                session.AddPartitionFromPsiStoreAsync(AnalysisStoreName, storeRoot).GetAwaiter().GetResult();
                dataset.Save();

                if (File.Exists(resolvedOutputPds))
                {
                    File.Delete(resolvedOutputPds);
                }

                File.Move(stagingPds, resolvedOutputPds);
            }
            catch
            {
                if (File.Exists(stagingPds))
                {
                    try
                    {
                        File.Delete(stagingPds);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                throw;
            }
        }
    }
}
