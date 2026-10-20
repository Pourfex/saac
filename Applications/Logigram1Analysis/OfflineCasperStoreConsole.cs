// <copyright file="OfflineCasperStoreConsole.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Security;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using SAAC.PipelineServices;

    /// <summary>
    /// Opens a Casper <c>.pds</c> Dataset offline via <see cref="ReplayPipeline"/> and smoke-checks
    /// PreTest.081-shaped required streams without running a full replay.
    /// </summary>
    public static class OfflineCasperStoreConsole
    {
        /// <summary>
        /// Usage line printed when <c>--store</c> is missing or empty.
        /// </summary>
        public const string Usage = "Usage: Logigram1Analysis --store <path.pds>";

        /// <summary>
        /// Required partition/stream pairs matching PreTest.081 in PsiStudio.
        /// </summary>
        public static readonly (string Partition, string Stream)[] RequiredStreams =
        {
            ("VideoRemoteApp", "FullScreen"),
            ("WhisperStreaming", "Audio_User_1"),
            ("WhisperStreaming", "Audio_User_2"),
            ("Server", "Area1"),
            ("Server", "Area2"),
        };

        /// <summary>
        /// Runs the console: parse <c>--store</c>, open the Dataset, load connectors, smoke-check streams.
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

            string storePath;
            try
            {
                storePath = ParseStoreArgument(args);
            }
            catch (ArgumentException exception)
            {
                stderr.WriteLine(exception.Message);
                stderr.WriteLine(Usage);
                return 1;
            }

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

                string smokeError = VerifyRequiredStreams(pipeline.Dataset);
                if (smokeError != null)
                {
                    stderr.WriteLine(smokeError);
                    return 1;
                }

                stdout.WriteLine($"Opened dataset \"{fullPath}\" offline; connectors loaded; required streams present with data.");
                foreach ((string partition, string stream) in RequiredStreams)
                {
                    IStreamMetadata metadata = FindStreamMetadata(pipeline.Dataset, partition, stream);
                    stdout.WriteLine($"  {partition}/{stream}: {FormatDataSignal(metadata)}");
                }

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
        /// Parses the named <c>--store</c> flag.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>The store path value.</returns>
        public static string ParseStoreArgument(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException("Missing required --store argument.");
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--store", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException("Missing value for --store <path.pds>.");
                    }

                    return args[i + 1];
                }
            }

            throw new ArgumentException("Missing required --store argument.");
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
        /// Confirms each required partition/stream exists with non-empty recorded content.
        /// </summary>
        /// <param name="dataset">Loaded Psi Dataset.</param>
        /// <returns>Null on success; otherwise an error message naming the missing or empty stream.</returns>
        public static string VerifyRequiredStreams(Dataset dataset)
        {
            if (dataset == null)
            {
                return "Dataset is null; cannot verify required streams.";
            }

            foreach ((string partition, string stream) in RequiredStreams)
            {
                IStreamMetadata metadata = FindStreamMetadata(dataset, partition, stream);
                if (metadata == null)
                {
                    return $"Required stream missing: {partition}/{stream}.";
                }

                if (!HasRecordedData(metadata))
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
    }
}
