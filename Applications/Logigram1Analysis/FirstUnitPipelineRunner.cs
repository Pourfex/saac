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
    /// FullSpeed importer pipeline: FirstUnit interim stub + designated Outcome writer →
    /// one analysis PsiStore partition + Dataset <c>.pds</c> (PERSIST / REPLAY locked decisions).
    /// </summary>
    public static class FirstUnitPipelineRunner
    {
        /// <summary>Partition / store name written into the analysis Dataset.</summary>
        public const string AnalysisStoreName = "Logigram1Analysis";

        /// <summary>
        /// Runs FirstUnit over the seven AD-7 topics, pipes evidence to the writer, persists
        /// <see cref="LogigramState.StreamName"/> and <see cref="LogigramOutcome.StreamName"/>,
        /// and saves the analysis Dataset at <paramref name="resolvedOutputPds"/>.
        /// </summary>
        /// <param name="dataset">Loaded input Dataset (topics already verified present).</param>
        /// <param name="batterySegments">Discovered Spawn→Finish segments for BatteryId stamping.</param>
        /// <param name="resolvedOutputPds">Validated absolute output <c>.pds</c> path.</param>
        /// <param name="inputStorePath">Absolute input Casper <c>.pds</c> (SESSION_ATTR session name).</param>
        /// <param name="findStreamByExactName">Stream lookup (defaults to console helper).</param>
        /// <exception cref="InvalidOperationException">Missing topic stream or persist failure.</exception>
        public static void RunAndPersist(
            Dataset dataset,
            IReadOnlyList<BatterySegment> batterySegments,
            string resolvedOutputPds,
            string inputStorePath,
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

            using (Pipeline pipeline = Pipeline.Create(nameof(FirstUnitPipelineRunner)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);

                var importers = new Dictionary<string, PsiImporter>(StringComparer.Ordinal);
                foreach (string topic in Logigram1UnitTopicRegistry.FirstUnitTopics)
                {
                    IStreamMetadata metadata = lookup(dataset, topic);
                    if (metadata == null)
                    {
                        throw new InvalidOperationException(
                            $"Required FirstUnit topic stream missing at run time: {topic}.");
                    }

                    PsiImporter importer = GetOrOpenImporter(pipeline, importers, metadata);
                    AttachTopic(importer, unit, topic, metadata.Name);
                }

                PsiExporter exporter = PsiStore.Create(pipeline, AnalysisStoreName, storeRoot);
                unit.StateOut.Write(LogigramState.StreamName, exporter);
                writer.Out.Write(LogigramOutcome.StreamName, exporter);

                // REPLAY = FullSpeed over FirstUnit + Battery-scoped stub (importer-driven).
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
                        $"FirstUnit FullSpeed pipeline failed: {root.GetType().Name}: {root.Message}",
                        root);
                }

                if (pipelineError != null)
                {
                    throw new InvalidOperationException(
                        $"FirstUnit FullSpeed pipeline failed: {pipelineError.GetType().Name}: {pipelineError.Message}",
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
            Logigram1FirstUnit unit,
            string topicName,
            string streamName)
        {
            // Catalogue types from PSI_EVENTS_README / Casper (payload ignored by interim stub).
            switch (topicName)
            {
                case "Module status":
                    importer.OpenStream<ValueTuple<int, string>>(streamName)
                        .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>(topicName));
                    break;

                case "Porte1 ouverture":
                case "Porte2 ouverture":
                    importer.OpenStream<ValueTuple<bool, Vector3>>(streamName)
                        .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>(topicName));
                    break;

                case "M1-SelectModule":
                case "M2-SelectModule":
                    importer.OpenStream<string>(streamName)
                        .PipeTo(unit.AddTopicInput<string>(topicName));
                    break;

                case "M1-Validation":
                case "M2-Validation":
                    importer.OpenStream<bool>(streamName)
                        .PipeTo(unit.AddTopicInput<bool>(topicName));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"No catalogue type mapping for FirstUnit topic \"{topicName}\".");
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
