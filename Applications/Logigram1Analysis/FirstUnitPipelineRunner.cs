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
    /// FullSpeed importer pipeline: <c>Logigram1.GammaPath</c> cycle FSM + designated Outcome
    /// writer → one analysis PsiStore partition + Dataset <c>.pds</c> (PERSIST / REPLAY locked decisions).
    /// Clear-before-write; SESSION_ATTR. Runner name kept for call-site stability.
    /// </summary>
    public static class FirstUnitPipelineRunner
    {
        /// <summary>Partition / store name written into the analysis Dataset.</summary>
        public const string AnalysisStoreName = "Logigram1Analysis";

        /// <summary>
        /// Runs the Gamma-path unit over its AD-7 topic union, pipes evidence and Gamma decisions to the writer,
        /// persists <see cref="LogigramState.StreamName"/> and <see cref="LogigramOutcome.StreamName"/>,
        /// and saves the analysis Dataset at <paramref name="resolvedOutputPds"/>.
        /// </summary>
        /// <param name="dataset">Loaded input Dataset (topics already verified present).</param>
        /// <param name="batterySegments">Discovered Spawn→Finish segments for BatteryId stamping.</param>
        /// <param name="resolvedOutputPds">Validated absolute output <c>.pds</c> path.</param>
        /// <param name="inputStorePath">Absolute input Casper <c>.pds</c> (SESSION_ATTR session name).</param>
        /// <param name="enabledUnits">Enabled unit ids; must include <c>Logigram1.GammaPath</c> (legacy ids ignored upstream).</param>
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

            if (!IsUnitEnabled(enabledUnits, Logigram1UnitTopicRegistry.GammaPathUnitId))
            {
                throw new InvalidOperationException(
                    "RunAndPersist requires Logigram1.GammaPath enabled.");
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

            IReadOnlyList<string> requiredTopics = Logigram1UnitTopicRegistry.TryResolveTopicUnion(
                enabledUnits,
                out string unknownUnitId);
            if (unknownUnitId != null || requiredTopics == null)
            {
                throw new InvalidOperationException(
                    $"Unknown enabled Logic unit id \"{unknownUnitId ?? "(null)"}\": no topic registry entry.");
            }

            IReadOnlyList<string> optionalTopics = Logigram1UnitTopicRegistry.ResolveOptionalTopics(enabledUnits);

            // Pass 1: read every topic of the union, then merge into one globally OT-ordered sequence
            // (importers do not interleave streams by originating time — see GammaPathTopicOrdering).
            IReadOnlyList<(GammaPathTopicEvent Event, DateTime OriginatingTime)> orderedEvents =
                GammaPathTopicOrdering.Order(LoadTopicEvents(dataset, lookup, requiredTopics, optionalTopics));

            // Pass 2: causal FullSpeed replay of the ordered sequence through the cycle FSM + Outcome writer.
            using (Pipeline pipeline = Pipeline.Create(nameof(FirstUnitPipelineRunner)))
            {
                var writer = new LogigramOutcomeWriter(pipeline);
                var gammaPath = new Logigram1GammaPathUnit(pipeline, scopes);
                gammaPath.EvidenceOut.PipeTo(writer.EvidenceIn);
                gammaPath.RegulationDecisionWithBatteryOut.PipeTo(writer.RegulationDecisionWithBatteryIn);

                Generators.Sequence(pipeline, orderedEvents)
                    .PipeTo(gammaPath.AddOrderedTopicInput());

                // State / Outcome keep the unit's own module- and leave-aligned originating times:
                // no second remapper (it relocated node-3 States onto the session's last State time).
                PsiExporter exporter = PsiStore.Create(pipeline, AnalysisStoreName, storeRoot);
                gammaPath.StateOut.Write(LogigramState.StreamName, exporter);
                writer.Out.Write(LogigramOutcome.StreamName, exporter);

                // REPLAY = FullSpeed over GammaPath + Battery-scoped derivation.
                RunFullSpeed(pipeline);
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

        /// <summary>
        /// Pass 1: reads every available topic of the union (required first; optional skipped when absent) into
        /// memory with its store originating time, in arbitrary cross-stream arrival order.
        /// </summary>
        private static List<(GammaPathTopicEvent Event, DateTime OriginatingTime)> LoadTopicEvents(
            Dataset dataset,
            Func<Dataset, string, IStreamMetadata> lookup,
            IReadOnlyList<string> requiredTopics,
            IReadOnlyList<string> optionalTopics)
        {
            var arrivals = new List<(GammaPathTopicEvent Event, DateTime OriginatingTime)>();
            var gate = new object();
            Action<GammaPathTopicEvent, DateTime> sink = (topicEvent, time) =>
            {
                lock (gate)
                {
                    arrivals.Add((topicEvent, time));
                }
            };

            using (Pipeline loader = Pipeline.Create(nameof(FirstUnitPipelineRunner) + "-Load"))
            {
                var importers = new Dictionary<string, PsiImporter>(StringComparer.Ordinal);
                var attachedTopics = new HashSet<string>(StringComparer.Ordinal);

                foreach (string topic in requiredTopics)
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

                    PsiImporter importer = GetOrOpenImporter(loader, importers, metadata);
                    AttachTopic(importer, topic, metadata.Name, sink);
                }

                foreach (string topic in optionalTopics)
                {
                    if (!attachedTopics.Add(topic))
                    {
                        continue;
                    }

                    IStreamMetadata metadata = lookup(dataset, topic);
                    if (metadata == null)
                    {
                        continue;
                    }

                    PsiImporter importer = GetOrOpenImporter(loader, importers, metadata);
                    AttachTopic(importer, topic, metadata.Name, sink);
                }

                RunFullSpeed(loader);
            }

            return arrivals;
        }

        private static void RunFullSpeed(Pipeline pipeline)
        {
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

        private static void AttachTopic(
            PsiImporter importer,
            string topicName,
            string streamName,
            Action<GammaPathTopicEvent, DateTime> sink)
        {
            switch (topicName)
            {
                case "Module status":
                    Collect<ValueTuple<int, string>>(importer, topicName, streamName, sink);
                    break;

                case "Porte1 ouverture":
                case "Porte2 ouverture":
                    Collect<ValueTuple<bool, Vector3>>(importer, topicName, streamName, sink);
                    break;

                case "M1-SelectModule":
                case "M2-SelectModule":
                    Collect<string>(importer, topicName, streamName, sink);
                    break;

                case "M1-Validation":
                case "M2-Validation":
                    Collect<bool>(importer, topicName, streamName, sink);
                    break;

                case "Area1":
                case "Area2":
                    Collect<ValueTuple<int, bool, string>>(importer, topicName, streamName, sink);
                    break;

                case "ObjectBound":
                    Collect<ValueTuple<string, Vector3, Vector3>>(importer, topicName, streamName, sink);
                    break;

                case "1-LeftWrist":
                case "1-RightWrist":
                case "2-LeftWrist":
                case "2-RightWrist":
                    // Own the tuple: do not keep a reference into importer-owned message state.
                    Collect<Tuple<Vector3, Vector3>>(
                        importer,
                        topicName,
                        streamName,
                        sink,
                        wrist => Tuple.Create(wrist.Item1, wrist.Item2));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"No catalogue type mapping for Logic topic \"{topicName}\".");
            }
        }

        private static void Collect<T>(
            PsiImporter importer,
            string topicName,
            string streamName,
            Action<GammaPathTopicEvent, DateTime> sink,
            Func<T, object> copy = null)
        {
            importer.OpenStream<T>(streamName)
                .Do((payload, envelope) => sink(
                    new GammaPathTopicEvent(topicName, copy != null ? copy(payload) : payload),
                    envelope.OriginatingTime));
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
