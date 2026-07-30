// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Replay.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using SAAC;
    using SaacAnalysisCasper.Core.Config;
    using SaacAnalysisCasper.Core.Export;

    /// <summary>
    /// Host-owned derived store + CSV sinks for a Replay run (AD-5 / AD-9).
    /// Opens paths/stores; Core helpers format names and write rows.
    /// </summary>
    public sealed class DerivedExportSession : IDisposable
    {
        /// <summary>
        /// Psi store name inside the AD-5 derived directory (directory already includes session attribution).
        /// </summary>
        private const string DerivedStoreLeafName = "derived";

        private readonly LogStatus log;
        private readonly List<string> csvPaths;
        private readonly List<string> storeDirectories;
        private readonly List<StreamWriter?> streamWriters;
        private readonly Dictionary<string, int> rowsByCsvPath;
        private readonly object writerGate;
        private bool completedSuccessfully;
        private bool writersClosed;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="DerivedExportSession"/> class.
        /// </summary>
        /// <param name="log">Host logging delegate.</param>
        public DerivedExportSession(LogStatus log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.csvPaths = new List<string>();
            this.storeDirectories = new List<string>();
            this.streamWriters = new List<StreamWriter?>();
            this.rowsByCsvPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
        /// Creates output root, derived store(s), and per graph×participant CSV writers;
        /// wires store Write + CSV <c>Do</c> sinks before pipeline start.
        /// </summary>
        /// <param name="pipeline">Analysis pipeline that owns emitters.</param>
        /// <param name="branches">Bound Poc branches from <see cref="DualUserGraphBinder"/>.</param>
        /// <param name="runConfig">Core run-config (outputRoot, W).</param>
        /// <param name="sessionName">Session name for store attribution.</param>
        /// <param name="datasetIdentity">Dataset path/name for attribution logs.</param>
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

            int windowMs = ResolvePrimaryWindowMs(runConfig);
            if (runConfig.WindowMsSweep != null && runConfig.WindowMsSweep.Count > 1)
            {
                this.log(
                    "windowMsSweep has " + runConfig.WindowMsSweep.Count
                    + " values; Story 1.5 writes CSV for primary W=" + windowMs
                    + " only (per-W science files land in Story 1.7).");
            }

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

                    // Path = attributed folder; leaf store name is fixed so Psi does not nest
                    // {name}/{name}/ under the same leaf string used as both path and store name.
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
                string streamName = ExportPathFormatter.FormatStreamName(
                    branch.GraphId,
                    branch.Participant,
                    ExportStreamRoles.Marker);
                StoreExportHelper.Write(graphExporter, branch.MessageCountOut, streamName);
                this.log(
                    "Store stream wired: " + streamName
                    + " participant=" + branch.Participant
                    + " graph=" + branch.GraphId);

                string csvPath = ExportPathFormatter.FormatCsvPath(
                    runConfig.OutputRoot,
                    branch.GraphId,
                    branch.Participant,
                    windowMs);

                StreamWriter streamWriter = new StreamWriter(csvPath, append: false, CsvExportFormat.Utf8WithoutBom);
                CsvExportWriter csvWriter = new CsvExportWriter(streamWriter);
                csvWriter.WriteHeader();
                this.streamWriters.Add(streamWriter);
                this.csvPaths.Add(csvPath);
                this.rowsByCsvPath[csvPath] = 0;

                CsvExportWriter capturedWriter = csvWriter;
                string capturedPath = csvPath;
                object gate = this.writerGate;
                branch.MessageCountOut.Do(
                    (count, envelope) =>
                    {
                        lock (gate)
                        {
                            if (this.writersClosed)
                            {
                                return;
                            }

                            capturedWriter.WriteMarkerRow(envelope.OriginatingTime, count);
                            this.rowsByCsvPath[capturedPath] = this.rowsByCsvPath[capturedPath] + 1;
                        }
                    },
                    DeliveryPolicy.Unlimited);

                this.log(
                    "CSV export wired: participant=" + branch.Participant
                    + " W=" + windowMs
                    + " session=" + sessionName
                    + " dataset=" + datasetIdentity
                    + " graph=" + branch.GraphId
                    + " path=" + csvPath);
            }
        }

        /// <summary>
        /// Ensures each bound branch produced at least one CSV data row (AC#1).
        /// </summary>
        public void EnsureExportRowsPresent()
        {
            lock (this.writerGate)
            {
                for (int i = 0; i < this.csvPaths.Count; i++)
                {
                    string path = this.csvPaths[i];
                    int rows;
                    if (!this.rowsByCsvPath.TryGetValue(path, out rows) || rows < 1)
                    {
                        throw new InvalidOperationException(
                            "Export produced no data rows for CSV '" + path
                            + "'. Successful runs require at least one Core-emitted message per branch.");
                    }
                }
            }
        }

        /// <summary>
        /// Marks the run successful so dispose leaves artifacts in place.
        /// Call only after writers are closed and the derived store exporter is released.
        /// </summary>
        public void MarkCompleted()
        {
            this.completedSuccessfully = true;
        }

        /// <summary>
        /// Flushes and closes CSV writers after pipeline WaitAll (store disposed with pipeline).
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
        /// Marks or deletes partial CSV/store outputs after abort/failure (not reported as success).
        /// Prefer calling after the Replay pipeline/exporter is stopped so store files are unlocked.
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

        private static int ResolvePrimaryWindowMs(AnalysisRunConfig runConfig)
        {
            if (runConfig.WindowMs.HasValue)
            {
                return runConfig.WindowMs.Value;
            }

            if (runConfig.WindowMsSweep != null && runConfig.WindowMsSweep.Count > 0)
            {
                return runConfig.WindowMsSweep[0];
            }

            throw new InvalidOperationException(
                "Run-config has neither windowMs nor windowMsSweep; cannot resolve CSV W values.");
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
