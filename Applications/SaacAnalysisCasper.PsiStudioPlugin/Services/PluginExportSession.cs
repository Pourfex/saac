// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.PsiStudioPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using SaacAnalysisCasper.Core.Config;
    using SaacAnalysisCasper.Core.Export;
    using SaacAnalysisCasper.Core.Poc;

    /// <summary>
    /// Host-owned derived store + CSV sinks for a Plugin POC run (AD-5 / AD-9).
    /// Mirrors Replay export contracts — Core helpers format names and write rows.
    /// </summary>
    public sealed class PluginExportSession : IDisposable
    {
        private const string DerivedStoreLeafName = "derived";

        private readonly Action<string> log;
        private readonly List<string> csvPaths;
        private readonly List<string> storeDirectories;
        private readonly List<StreamWriter?> streamWriters;
        private readonly Dictionary<string, int> rowsByCsvPath;
        private readonly Dictionary<string, bool> requireRowsByCsvPath;
        private readonly object writerGate;
        private bool completedSuccessfully;
        private bool writersClosed;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginExportSession"/> class.
        /// </summary>
        /// <param name="log">Host logging delegate.</param>
        public PluginExportSession(Action<string> log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.csvPaths = new List<string>();
            this.storeDirectories = new List<string>();
            this.streamWriters = new List<StreamWriter?>();
            this.rowsByCsvPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            this.requireRowsByCsvPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            this.writerGate = new object();
        }

        /// <summary>
        /// Gets a value indicating whether the run was marked successfully completed.
        /// </summary>
        public bool CompletedSuccessfully => this.completedSuccessfully;

        /// <summary>
        /// Gets CSV paths created for this run.
        /// </summary>
        public IReadOnlyList<string> CsvPaths => this.csvPaths;

        /// <summary>
        /// Gets derived store directories created for this run.
        /// </summary>
        public IReadOnlyList<string> StoreDirectories => this.storeDirectories;

        /// <summary>
        /// Creates output root, derived store(s), and per graph×participant×W CSV writers;
        /// wires store Write + CSV <c>Do</c> sinks before pipeline start.
        /// </summary>
        /// <param name="pipeline">Analysis pipeline that owns emitters.</param>
        /// <param name="branches">Bound Poc branches from <see cref="PluginPocBinder"/>.</param>
        /// <param name="runConfig">Core run-config (outputRoot, W list).</param>
        /// <param name="sessionName">Session name for store attribution (Plugin uses a host label).</param>
        /// <param name="datasetIdentity">Dataset/host identity for attribution logs.</param>
        public void AttachExports(
            Pipeline pipeline,
            IReadOnlyList<BoundBranchDescriptor> branches,
            AnalysisRunConfig runConfig,
            string sessionName,
            string datasetIdentity)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (branches == null)
            {
                throw new ArgumentNullException(nameof(branches));
            }

            if (runConfig == null)
            {
                throw new ArgumentNullException(nameof(runConfig));
            }

            if (string.IsNullOrWhiteSpace(sessionName))
            {
                throw new ArgumentException("sessionName must be non-empty.", nameof(sessionName));
            }

            if (branches.Count == 0)
            {
                throw new InvalidOperationException(
                    "No bound branches available for derived store/CSV export.");
            }

            Directory.CreateDirectory(runConfig.OutputRoot);

            Dictionary<string, PsiExporter> exportersByGraph =
                new Dictionary<string, PsiExporter>(StringComparer.Ordinal);

            for (int i = 0; i < branches.Count; i++)
            {
                BoundBranchDescriptor branch = branches[i];
                if (branch == null)
                {
                    throw new ArgumentException("branches contains a null entry.", nameof(branches));
                }

                if (!exportersByGraph.ContainsKey(branch.GraphId))
                {
                    string storeDirectory = ExportPathFormatter.FormatDerivedStoreDirectory(
                        runConfig.OutputRoot,
                        branch.GraphId,
                        sessionName);
                    string storeFolderName = ExportPathFormatter.FormatDerivedStoreName(
                        branch.GraphId,
                        sessionName);

                    Directory.CreateDirectory(storeDirectory);

                    PsiExporter exporter = PsiStore.Create(pipeline, DerivedStoreLeafName, storeDirectory);
                    exportersByGraph.Add(branch.GraphId, exporter);
                    this.storeDirectories.Add(storeDirectory);

                    this.log(
                        "Derived store created: graph=" + branch.GraphId
                        + " session=" + sessionName
                        + " dataset=" + datasetIdentity
                        + " storeFolder=" + storeFolderName
                        + " storeName=" + DerivedStoreLeafName
                        + " path=" + storeDirectory);
                }

                PsiExporter graphExporter = exportersByGraph[branch.GraphId];
                string streamRole = ExportStreamRoles.CoincidenceForWindow(branch.WindowMs);
                string streamName = ExportPathFormatter.FormatStreamName(
                    branch.GraphId,
                    branch.Participant,
                    streamRole);

                IProducer<int> storePayload = branch.CoincidenceOut.Select(
                    (PocCoincidenceC c) => c.WindowMs,
                    DeliveryPolicy.Unlimited);
                StoreExportHelper.Write(graphExporter, storePayload, streamName);
                this.log(
                    "Store stream wired: " + streamName
                    + " participant=" + branch.Participant
                    + " W=" + branch.WindowMs
                    + " graph=" + branch.GraphId);

                string csvPath = ExportPathFormatter.FormatCsvPath(
                    runConfig.OutputRoot,
                    branch.GraphId,
                    branch.Participant,
                    branch.WindowMs);

                StreamWriter streamWriter = new StreamWriter(csvPath, append: false, CsvExportFormat.Utf8WithoutBom);
                CsvExportWriter csvWriter = new CsvExportWriter(streamWriter);
                csvWriter.WriteCoincidenceHeader();
                this.streamWriters.Add(streamWriter);
                this.csvPaths.Add(csvPath);
                this.rowsByCsvPath[csvPath] = 0;
                this.requireRowsByCsvPath[csvPath] = branch.ExpectCoincidenceRows;

                CsvExportWriter capturedWriter = csvWriter;
                string capturedPath = csvPath;
                int capturedWindowMs = branch.WindowMs;
                object gate = this.writerGate;
                branch.CoincidenceOut.Do(
                    (PocCoincidenceC coincidence, Envelope envelope) =>
                    {
                        lock (gate)
                        {
                            if (this.writersClosed)
                            {
                                return;
                            }

                            int w = coincidence != null ? coincidence.WindowMs : capturedWindowMs;
                            capturedWriter.WriteCoincidenceRow(envelope.OriginatingTime, w);
                            this.rowsByCsvPath[capturedPath] = this.rowsByCsvPath[capturedPath] + 1;
                        }
                    },
                    DeliveryPolicy.Unlimited);

                this.log(
                    "CSV export wired: participant=" + branch.Participant
                    + " W=" + branch.WindowMs
                    + " session=" + sessionName
                    + " dataset=" + datasetIdentity
                    + " graph=" + branch.GraphId
                    + " expectRows=" + branch.ExpectCoincidenceRows
                    + " path=" + csvPath);
            }
        }

        /// <summary>
        /// Ensures CSV science gates for the inject schedule (same bar as Replay 1.7).
        /// </summary>
        public void EnsureExportRowsPresent()
        {
            lock (this.writerGate)
            {
                bool anyExpected = false;

                for (int i = 0; i < this.csvPaths.Count; i++)
                {
                    string path = this.csvPaths[i];
                    int rows;
                    if (!this.rowsByCsvPath.TryGetValue(path, out rows))
                    {
                        rows = 0;
                    }

                    bool requireRows;
                    if (!this.requireRowsByCsvPath.TryGetValue(path, out requireRows))
                    {
                        requireRows = true;
                    }

                    if (requireRows)
                    {
                        anyExpected = true;
                        if (rows < 1)
                        {
                            throw new InvalidOperationException(
                                "Export produced no coincidence rows for CSV '" + path
                                + "' where inject Δ is inside left-exclusive lookback (−W, 0]. "
                                + "Successful matching-W runs require at least one Core-emitted C.");
                        }
                    }
                    else if (rows > 0)
                    {
                        throw new InvalidOperationException(
                            "Export produced coincidence rows for CSV '" + path
                            + "' where inject Δ is outside lookback (−W, 0] (ExpectCoincidenceRows=false). "
                            + "Negative-case W must stay header-only.");
                    }
                }

                if (!anyExpected)
                {
                    throw new InvalidOperationException(
                        "No CSV path expects coincidence rows for the inject schedule "
                        + "(all resolved W values are too small). Include at least one W that admits B_near.");
                }
            }
        }

        /// <summary>
        /// Marks the run successful so dispose leaves artifacts in place.
        /// </summary>
        public void MarkCompleted()
        {
            this.completedSuccessfully = true;
        }

        /// <summary>
        /// Flushes and closes CSV writers after pipeline completion.
        /// </summary>
        public void CloseWriters()
        {
            lock (this.writerGate)
            {
                if (this.writersClosed)
                {
                    return;
                }

                this.writersClosed = true;
                for (int i = 0; i < this.streamWriters.Count; i++)
                {
                    StreamWriter? writer = this.streamWriters[i];
                    if (writer == null)
                    {
                        continue;
                    }

                    try
                    {
                        writer.Flush();
                        writer.Dispose();
                    }
                    catch (Exception ex)
                    {
                        this.TryLog("CSV writer close warning: " + ex.Message);
                    }

                    this.streamWriters[i] = null;
                }
            }
        }

        /// <summary>
        /// Marks or deletes partial CSV/store outputs after abort/failure.
        /// </summary>
        public void CleanupIncomplete()
        {
            if (this.completedSuccessfully)
            {
                return;
            }

            this.CloseWriters();
            IncompleteExportCleanup.MarkIncompleteOrDelete(this.csvPaths, this.TryLog);
            for (int i = 0; i < this.storeDirectories.Count; i++)
            {
                IncompleteExportCleanup.MarkIncompleteOrDeleteDirectory(this.storeDirectories[i], this.TryLog);
            }
        }

        /// <summary>
        /// Logs successful export artifact paths.
        /// </summary>
        public void LogSuccessPaths()
        {
            this.log("=== Export success paths ===");
            for (int i = 0; i < this.csvPaths.Count; i++)
            {
                this.log("CSV: " + this.csvPaths[i]);
            }

            for (int i = 0; i < this.storeDirectories.Count; i++)
            {
                this.log("Derived store: " + this.storeDirectories[i]);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            if (!this.completedSuccessfully)
            {
                this.CleanupIncomplete();
            }
            else
            {
                this.CloseWriters();
            }
        }

        private void TryLog(string message)
        {
            try
            {
                this.log(message);
            }
            catch
            {
                // Never let logging abort cleanup.
            }
        }
    }
}
