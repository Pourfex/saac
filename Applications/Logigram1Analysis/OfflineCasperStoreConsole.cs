// <copyright file="OfflineCasperStoreConsole.cs" company="SAAC">
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
    using System.Security;
    using Microsoft.Psi;
    using Microsoft.Psi.Audio;
    using Microsoft.Psi.Data;
    using Microsoft.Psi.Imaging;
    using SAAC.PipelineServices;
    using SAAC.PsiFormats;

    /// <summary>
    /// Opens a Casper <c>.pds</c> Dataset offline via <see cref="ReplayPipeline"/> and smoke-checks
    /// PreTest.081-shaped required streams without running a full replay.
    /// When config enables Logic units, also reports AD-7 required topics present or all gaps.
    /// After substrate + Logic-topic checks, discovers Battery Spawn→Finish segments (AD-6) or fails explicitly.
    /// When <c>Logigram1.FirstUnit</c> is enabled, runs the interim stub and persists analysis State/Outcome.
    /// </summary>
    public static class OfflineCasperStoreConsole
    {
        /// <summary>
        /// Usage line printed when neither <c>--config</c> nor <c>--store</c> is supplied (or args are invalid).
        /// </summary>
        public const string Usage = "Usage: Logigram1Analysis --config <file.json> [--store <path.pds>] | --store <path.pds>";

        /// <summary>
        /// Required partition/stream pairs matching PreTest.081 in PsiStudio.
        /// <c>RequireData</c> is false for Area streams (presence only — Casper catalogues often list empty Area1/Area2).
        /// </summary>
        public static readonly (string Partition, string Stream, bool RequireData)[] RequiredStreams =
        {
            ("VideoRemoteApp", "FullScreen", true),
            ("WhisperStreaming", "Audio_User_1", true),
            ("WhisperStreaming", "Audio_User_2", true),
            ("Server", "Area1", false),
            ("Server", "Area2", false),
        };

        /// <summary>
        /// Runs the console: resolve store from <c>--store</c> or <c>--config</c>, log session identity,
        /// open the Dataset, load connectors, smoke-check substrate streams, then Logic-topic presence/gaps.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="stdout">Optional stdout; defaults to <see cref="Console.Out"/>.</param>
        /// <param name="stderr">Optional stderr; defaults to <see cref="Console.Error"/>.</param>
        /// <param name="loadConnectors">Optional override for <see cref="ReplayPipeline.LoadDatasetAndConnectors"/> (tests).</param>
        /// <returns>0 on success; non-zero on failure.</returns>
        public static int Run(
            string[] args,
            TextWriter stdout = null,
            TextWriter stderr = null,
            Func<ReplayPipeline, bool> loadConnectors = null)
        {
            stdout = stdout ?? Console.Out;
            stderr = stderr ?? Console.Error;

            // Force-load catalogue assemblies so DatasetLoader Type.GetType can resolve Casper stream types.
            EnsureCatalogueAssembliesLoaded();

            string storePath;
            IReadOnlyList<string> enabledUnits;
            string outputPathOverride;
            try
            {
                ResolveRunInputs(args, out storePath, out enabledUnits, out outputPathOverride);
            }
            catch (ArgumentException exception)
            {
                stderr.WriteLine(exception.Message);
                stderr.WriteLine(Usage);
                return 1;
            }

            // Attribution after path resolve (success or fail): full path + .pds filename.
            string identityPath = TryGetFullPath(storePath) ?? storePath;
            WriteSessionIdentity(stdout, identityPath);

            string fullPath;
            try
            {
                fullPath = ValidateStorePath(storePath);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is PathTooLongException
                || exception is IOException
                || exception is NotSupportedException
                || exception is UnauthorizedAccessException
                || exception is SecurityException)
            {
                stderr.WriteLine(exception.Message);
                return 1;
            }

            string datasetPath = Path.GetDirectoryName(fullPath);
            string datasetName = Path.GetFileName(fullPath);

            ReplayPipeline pipeline = null;
            try
            {
                var configuration = new ReplayPipelineConfiguration
                {
                    DatasetPath = datasetPath,
                    DatasetName = datasetName,
                    DatasetBackup = false,
                    AutomaticPipelineRun = false,
                };

                pipeline = new ReplayPipeline(configuration);
                bool connectorsLoaded = loadConnectors != null
                    ? loadConnectors(pipeline)
                    : pipeline.LoadDatasetAndConnectors();
                if (!connectorsLoaded)
                {
                    stderr.WriteLine($"Failed to load dataset connectors from \"{fullPath}\".");
                    return 1;
                }

                // EPIC1_SMOKE: substrate first (fail-fast), then Logic-topic check when units enabled.
                string smokeError = VerifyRequiredStreams(pipeline.Dataset);
                if (smokeError != null)
                {
                    stderr.WriteLine(smokeError);
                    return 1;
                }

                IReadOnlyList<string> requiredTopics = Logigram1UnitTopicRegistry.TryResolveTopicUnion(
                    enabledUnits,
                    out string unknownUnitId);
                if (unknownUnitId != null)
                {
                    stderr.WriteLine(
                        $"Unknown enabled Logic unit id \"{unknownUnitId}\": no topic registry entry.");
                    return 1;
                }

                if (requiredTopics.Count > 0)
                {
                    IReadOnlyList<string> missingTopics = FindMissingTopicsByExactName(
                        pipeline.Dataset,
                        requiredTopics);
                    if (missingTopics.Count > 0)
                    {
                        foreach (string topic in missingTopics)
                        {
                            stderr.WriteLine($"Required Logic topic missing: {topic}");
                        }

                        return 1;
                    }
                }

                stdout.WriteLine($"Opened dataset \"{fullPath}\" offline; connectors loaded; required streams OK (video/audio must have data; Area presence only).");
                foreach ((string partition, string stream, bool requireData) in RequiredStreams)
                {
                    IStreamMetadata metadata = FindStreamMetadata(pipeline.Dataset, partition, stream);
                    string signal = requireData || HasRecordedData(metadata)
                        ? FormatDataSignal(metadata)
                        : "present (empty catalogue)";
                    stdout.WriteLine($"  {partition}/{stream}: {signal}");
                }

                if (requiredTopics.Count > 0)
                {
                    stdout.WriteLine("Required Logic topics OK:");
                    foreach (string topic in requiredTopics)
                    {
                        stdout.WriteLine($"  {topic}: present");
                    }
                }

                // AD-6: after substrate + Logic-topic checks, discover Battery scopes or fail explicitly.
                if (!BatteryBoundaryDiscovery.TryDiscoverFromDataset(
                        pipeline.Dataset,
                        FindStreamByExactName,
                        out IReadOnlyList<BatterySegment> batterySegments,
                        out string batteryError))
                {
                    stderr.WriteLine(batteryError ?? "Battery boundary discovery failed.");
                    return 1;
                }

                stdout.WriteLine($"Battery segments ({batterySegments.Count}) [Spawn→Finish LIFO]:");
                foreach (BatterySegment segment in batterySegments)
                {
                    stdout.WriteLine(
                        $"  BatteryId={segment.BatteryId} start={segment.StartOriginatingTime:o} end={segment.EndOriginatingTime:o}");
                }

                // Empty EnabledUnits → substrate + Battery discovery only (no unit / no output Dataset).
                bool firstUnitEnabled = enabledUnits.Any(unitId =>
                    string.Equals(
                        unitId?.Trim(),
                        Logigram1UnitTopicRegistry.FirstUnitId,
                        StringComparison.Ordinal));
                if (!firstUnitEnabled)
                {
                    return 0;
                }

                string resolvedOutput;
                try
                {
                    resolvedOutput = AnalysisOutputPath.Resolve(fullPath, outputPathOverride);
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                    || exception is PathTooLongException
                    || exception is NotSupportedException
                    || exception is SecurityException)
                {
                    stderr.WriteLine($"Failed to resolve analysis output path: {exception.Message}");
                    return 1;
                }

                if (!AnalysisOutputPath.TryValidate(fullPath, resolvedOutput, out string outputError))
                {
                    stderr.WriteLine(outputError ?? "Analysis output path rejected.");
                    return 1;
                }

                try
                {
                    FirstUnitPipelineRunner.RunAndPersist(
                        pipeline.Dataset,
                        batterySegments,
                        resolvedOutput);
                }
                catch (Exception exception)
                {
                    stderr.WriteLine(
                        $"FirstUnit analysis run / persist failed (input store untouched): {exception.Message}");
                    return 1;
                }

                stdout.WriteLine($"Analysis output Dataset: {resolvedOutput}");
                return 0;
            }
            catch (Exception exception)
            {
                stderr.WriteLine($"Failed to open dataset \"{fullPath}\": {exception.Message}");
                return 1;
            }
            finally
            {
                pipeline?.Dispose();
            }
        }

        /// <summary>
        /// Resolves the Casper <c>.pds</c> path: <c>--store</c> wins when present; otherwise
        /// <c>StorePath</c> from required <c>--config</c>. Zero args / neither flag → error.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>The unresolved store path string (may not exist yet).</returns>
        public static string ResolveStorePath(string[] args)
        {
            ResolveRunInputs(args, out string storePath, out _, out _);
            return storePath;
        }

        /// <summary>
        /// Resolves store path, enabled Logic unit ids, and optional output path from CLI args.
        /// <c>--store</c> wins for the path; <c>EnabledUnits</c> / <c>OutputPath</c> come from <c>--config</c>
        /// when present (even if <c>--store</c> overrides <c>StorePath</c>).
        /// No config → empty enabled list (substrate only) and null output override.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="storePath">Unresolved store path (may not exist yet).</param>
        /// <param name="enabledUnits">Unit ids for Logic-topic checks (never null).</param>
        /// <param name="outputPathOverride">Config <c>OutputPath</c> when set; otherwise null.</param>
        public static void ResolveRunInputs(
            string[] args,
            out string storePath,
            out IReadOnlyList<string> enabledUnits,
            out string outputPathOverride)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Missing required --config or --store argument.");
            }

            string storeFlag = TryGetNamedArgument(args, "--store");
            string configFlag = TryGetNamedArgument(args, "--config");

            Logigram1Configuration configuration = null;
            if (configFlag != null)
            {
                if (string.IsNullOrWhiteSpace(configFlag))
                {
                    throw new ArgumentException("Missing value for --config <file.json>.");
                }

                configuration = Logigram1Configuration.Load(configFlag);
            }

            enabledUnits = configuration?.EnabledUnits != null
                ? (IReadOnlyList<string>)configuration.EnabledUnits
                : Array.Empty<string>();
            outputPathOverride = configuration?.OutputPath;

            if (storeFlag != null)
            {
                if (string.IsNullOrWhiteSpace(storeFlag))
                {
                    throw new ArgumentException("Missing value for --store <path.pds>.");
                }

                storePath = storeFlag;
                return;
            }

            if (configuration != null)
            {
                storePath = configuration.StorePath;
                return;
            }

            throw new ArgumentException("Missing required --config or --store argument.");
        }

        /// <summary>
        /// Parses the named <c>--store</c> flag (Story 1.1 compatibility). Prefer <see cref="ResolveStorePath"/> for full resolution.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>The store path value.</returns>
        public static string ParseStoreArgument(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Missing required --store argument.");
            }

            string value = TryGetNamedArgument(args, "--store");
            if (value == null)
            {
                throw new ArgumentException("Missing required --store argument.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Missing value for --store <path.pds>.");
            }

            return value;
        }

        /// <summary>
        /// Writes session identity for attribution: full store path and dataset id (<c>.pds</c> filename).
        /// </summary>
        /// <param name="writer">Output writer (typically stdout).</param>
        /// <param name="storePath">Resolved store path (preferably absolute).</param>
        public static void WriteSessionIdentity(TextWriter writer, string storePath)
        {
            if (writer == null)
            {
                return;
            }

            string path = storePath ?? string.Empty;
            string id = TryGetFileName(path) ?? path;
            writer.WriteLine($"Session identity: path={path} id={id}");
        }

        /// <summary>
        /// Guards that <paramref name="storePath"/> is an existing <c>.pds</c> file (never create-empty).
        /// </summary>
        /// <param name="storePath">Path supplied via <c>--store</c>.</param>
        /// <returns>The absolute path to the dataset file.</returns>
        public static string ValidateStorePath(string storePath)
        {
            if (string.IsNullOrWhiteSpace(storePath))
            {
                throw new ArgumentException("Missing value for --store <path.pds>.");
            }

            string fullPath = Path.GetFullPath(storePath);

            if (Directory.Exists(fullPath) && !File.Exists(fullPath))
            {
                throw new ArgumentException(
                    $"A .pds Dataset file is required, not a session folder: \"{fullPath}\".");
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".pds", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"A .pds Dataset file is required: \"{fullPath}\".");
            }

            if (!File.Exists(fullPath))
            {
                throw new ArgumentException($"Dataset file not found: \"{fullPath}\".");
            }

            return fullPath;
        }

        /// <summary>
        /// Confirms each required partition/stream exists; streams with <c>RequireData</c> must also have recorded content.
        /// </summary>
        /// <param name="dataset">Loaded Psi Dataset.</param>
        /// <returns>Null on success; otherwise an error message naming the missing or empty stream.</returns>
        public static string VerifyRequiredStreams(Dataset dataset)
        {
            if (dataset == null)
            {
                return "Dataset is null; cannot verify required streams.";
            }

            foreach ((string partition, string stream, bool requireData) in RequiredStreams)
            {
                IStreamMetadata metadata = FindStreamMetadata(dataset, partition, stream);
                if (metadata == null)
                {
                    return $"Required stream missing: {partition}/{stream}.";
                }

                if (requireData && !HasRecordedData(metadata))
                {
                    return $"Required stream is empty (no recorded data): {partition}/{stream}.";
                }
            }

            return null;
        }

        /// <summary>
        /// Whether stream metadata indicates non-empty recorded content.
        /// Prefer <see cref="IStreamMetadata.MessageCount"/> &gt; 0; else average size &gt; 0;
        /// else originating-time span with <c>Last &gt; First</c> (not &gt;=).
        /// </summary>
        /// <param name="metadata">Stream metadata from the Dataset catalogue.</param>
        /// <returns>True when an honest non-empty signal is present.</returns>
        public static bool HasRecordedData(IStreamMetadata metadata)
        {
            if (metadata == null)
            {
                return false;
            }

            if (metadata.MessageCount > 0)
            {
                return true;
            }

            if (metadata.AverageMessageSize > 0)
            {
                return true;
            }

            if (metadata.FirstMessageOriginatingTime != DateTime.MinValue
                && metadata.LastMessageOriginatingTime != DateTime.MinValue
                && metadata.LastMessageOriginatingTime != DateTime.MaxValue
                && metadata.LastMessageOriginatingTime > metadata.FirstMessageOriginatingTime)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Formats the non-empty data signal used by smoke checks (must not contradict <see cref="HasRecordedData"/>).
        /// </summary>
        /// <param name="metadata">Stream metadata that already passed <see cref="HasRecordedData"/>.</param>
        /// <returns>A short description of the signal that indicates recorded data.</returns>
        public static string FormatDataSignal(IStreamMetadata metadata)
        {
            if (metadata == null)
            {
                return "no metadata";
            }

            if (metadata.MessageCount > 0)
            {
                return $"{metadata.MessageCount} message(s)";
            }

            if (metadata.AverageMessageSize > 0)
            {
                return $"avg size {metadata.AverageMessageSize} byte(s) (message count {metadata.MessageCount})";
            }

            if (metadata.FirstMessageOriginatingTime != DateTime.MinValue
                && metadata.LastMessageOriginatingTime != DateTime.MinValue
                && metadata.LastMessageOriginatingTime != DateTime.MaxValue
                && metadata.LastMessageOriginatingTime > metadata.FirstMessageOriginatingTime)
            {
                return $"originating time span {metadata.FirstMessageOriginatingTime:o} -> {metadata.LastMessageOriginatingTime:o}";
            }

            return "unknown data signal";
        }

        /// <summary>
        /// STREAM_LOOKUP A: a required Logic topic is present when any Dataset partition has a stream
        /// whose <see cref="IStreamMetadata.Name"/> equals the AD-7 topic string (ordinal, no aliases).
        /// </summary>
        /// <param name="dataset">Loaded Psi Dataset.</param>
        /// <param name="topicName">Exact AD-7 topic / stream name.</param>
        /// <returns>Matching metadata, or null if absent.</returns>
        public static IStreamMetadata FindStreamByExactName(Dataset dataset, string topicName)
        {
            if (dataset == null || string.IsNullOrEmpty(topicName))
            {
                return null;
            }

            foreach (Session session in dataset.Sessions)
            {
                foreach (IPartition partition in session.Partitions)
                {
                    if (partition.AvailableStreams == null)
                    {
                        continue;
                    }

                    IStreamMetadata match = partition.AvailableStreams
                        .FirstOrDefault(stream => string.Equals(stream.Name, topicName, StringComparison.Ordinal));
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns every required topic whose exact stream name is absent from the Dataset (GAP_POLICY A).
        /// </summary>
        /// <param name="dataset">Loaded Psi Dataset.</param>
        /// <param name="requiredTopics">AD-7 topic names to look up.</param>
        /// <returns>Missing topics in the same order as <paramref name="requiredTopics"/>; empty when all present.</returns>
        public static IReadOnlyList<string> FindMissingTopicsByExactName(
            Dataset dataset,
            IEnumerable<string> requiredTopics)
        {
            var missing = new List<string>();
            if (requiredTopics == null)
            {
                return missing;
            }

            foreach (string topic in requiredTopics)
            {
                if (string.IsNullOrEmpty(topic))
                {
                    continue;
                }

                if (FindStreamByExactName(dataset, topic) == null)
                {
                    missing.Add(topic);
                }
            }

            return missing;
        }

        /// <summary>
        /// Finds stream metadata by partition name or, when the partition display name differs, by stream StoreName.
        /// </summary>
        /// <param name="dataset">Loaded Psi Dataset.</param>
        /// <param name="partitionName">Expected partition (or store) name.</param>
        /// <param name="streamName">Stream name.</param>
        /// <returns>Matching metadata, or null if absent.</returns>
        public static IStreamMetadata FindStreamMetadata(Dataset dataset, string partitionName, string streamName)
        {
            if (dataset == null)
            {
                return null;
            }

            foreach (Session session in dataset.Sessions)
            {
                foreach (IPartition partition in session.Partitions)
                {
                    if (!PartitionMatches(partition, partitionName))
                    {
                        continue;
                    }

                    if (partition.AvailableStreams == null)
                    {
                        continue;
                    }

                    IStreamMetadata match = partition.AvailableStreams
                        .FirstOrDefault(stream => string.Equals(stream.Name, streamName, StringComparison.Ordinal));
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Matches a partition by display name, or by any stream's <see cref="IStreamMetadata.StoreName"/>.
        /// </summary>
        /// <param name="partition">Partition from the Dataset.</param>
        /// <param name="partitionName">Expected PreTest.081 partition name.</param>
        /// <returns>True when the partition or its store name matches.</returns>
        public static bool PartitionMatches(IPartition partition, string partitionName)
        {
            if (partition == null)
            {
                return false;
            }

            if (string.Equals(partition.Name, partitionName, StringComparison.Ordinal))
            {
                return true;
            }

            // Some catalogues expose the store name on streams rather than the partition display name.
            return partition.AvailableStreams != null
                && partition.AvailableStreams.Any(stream =>
                    string.Equals(stream.StoreName, partitionName, StringComparison.Ordinal));
        }

        /// <summary>
        /// Touches types from Audio / Imaging / PsiFormats so their assemblies are loaded before DatasetLoader resolves catalogue names.
        /// </summary>
        private static void EnsureCatalogueAssembliesLoaded()
        {
            _ = typeof(AudioBuffer);
            _ = typeof(Image);
            _ = typeof(PsiBatterie);
            _ = typeof(PsiBatteryEvent);
            _ = typeof(PsiBatteryModuleEvent);
            _ = typeof(PsiGazeObjectEvent);
            _ = typeof(PsiBatteryFinish);
            _ = typeof(PsiTV);
        }

        /// <summary>
        /// Returns the value after <paramref name="name"/>, empty string if the flag is present without a value,
        /// or null if the flag is absent.
        /// </summary>
        private static string TryGetNamedArgument(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length
                    || args[i + 1] == null
                    || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                return args[i + 1];
            }

            return null;
        }

        private static string TryGetFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is PathTooLongException
                || exception is NotSupportedException)
            {
                return null;
            }
        }

        private static string TryGetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileName(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
