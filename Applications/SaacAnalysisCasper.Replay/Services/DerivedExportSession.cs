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
    using SaacAnalysisCasper.Core.Mapping;
    using SaacAnalysisCasper.Core.Poc;

    /// <summary>
    /// Host-owned derived store + CSV sinks for a Replay run (AD-5 / AD-9).
    /// Opens paths/stores; Core helpers format names and write rows.
    /// Per-W attribution: one CSV per branch (graph × session × participant × W) via <see cref="ExportPathFormatter.FormatCsvPath"/>.
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
        private readonly Dictionary<string, bool> requireRowsByCsvPath;
        private readonly object writerGate;
        private bool completedSuccessfully;
        private bool writersClosed;
        private bool writersFlushedOk;
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
        /// Creates output root, derived store(s), and per graph×session×participant×W CSV writers;
        /// wires store Write + CSV <c>Do</c> sinks before pipeline start.
        /// </summary>
        /// <param name="pipeline">Analysis pipeline that owns emitters.</param>
        /// <param name="branches">Bound Poc branches from <see cref="DualUserGraphBinder"/>.</param>
        /// <param name="runConfig">Core run-config (outputRoot, W list).</param>
        /// <param name="sessionName">Session name for store attribution.</param>
        /// <param name="datasetIdentity">Dataset path/name for attribution logs.</param>
        public void AttachExports(
            Pipeline pipeline,
            IReadOnlyList<BoundBranchDescriptor> branches,
            AnalysisRunConfig runConfig,
            string sessionName,
            string datasetIdentity)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(DerivedExportSession));
            }

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

            if (string.IsNullOrWhiteSpace(runConfig.OutputRoot))
            {
                throw new ArgumentException("runConfig.OutputRoot must be non-empty.", nameof(runConfig));
            }

            if (branches.Count == 0)
            {
                throw new InvalidOperationException(
                    "No bound branches available for derived store/CSV export.");
            }

            RequireBothParticipants(branches);

            Directory.CreateDirectory(runConfig.OutputRoot);

            Dictionary<string, PsiExporter> exportersByGraph =
                new Dictionary<string, PsiExporter>(StringComparer.Ordinal);
            HashSet<string> csvPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                    PsiExporter exporter;
                    try
                    {
                        // Path = attributed folder; leaf store name is fixed so Psi does not nest
                        // {name}/{name}/ under the same leaf string used as both path and store name.
                        exporter = PsiStore.Create(pipeline, DerivedStoreLeafName, storeDirectory);
                    }
                    catch
                    {
                        TryDeleteDirectory(storeDirectory);
                        throw;
                    }

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

                // Store payload: WindowMs int (primitive; avoids custom DTO serializer registration).
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
                    sessionName,
                    branch.Participant,
                    branch.WindowMs);

                if (!csvPathSet.Add(csvPath))
                {
                    throw new InvalidOperationException(
                        "Duplicate CSV export path '" + csvPath
                        + "' — each graph×session×participant×W branch must map to a unique file.");
                }

                StreamWriter streamWriter = new StreamWriter(csvPath, append: false, CsvExportFormat.Utf8WithoutBom);

                // Track before WriteHeader so abort cleanup sees the file if header write fails.
                this.streamWriters.Add(streamWriter);
                this.csvPaths.Add(csvPath);
                this.rowsByCsvPath[csvPath] = 0;
                this.requireRowsByCsvPath[csvPath] = branch.ExpectCoincidenceRows;

                CsvExportWriter csvWriter = new CsvExportWriter(streamWriter);
                csvWriter.WriteCoincidenceHeader();

                CsvExportWriter capturedWriter = csvWriter;
                string capturedPath = csvPath;
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

                            if (coincidence == null)
                            {
                                throw new InvalidOperationException(
                                    "Null PocCoincidenceC on CSV sink for '" + capturedPath
                                    + "'; refusing to forge a coincidence row.");
                            }

                            capturedWriter.WriteCoincidenceRow(envelope.OriginatingTime, coincidence.WindowMs);
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
        /// Ensures CSV science gates for the inject schedule.
        /// Matching-W branches (<see cref="BoundBranchDescriptor.ExpectCoincidenceRows"/> true) require ≥1 C row.
        /// Non-matching branches must stay at 0 rows (fail-closed if a buggy POC still emits C when Δ &gt; W).
        /// At least one matching-W branch must exist so an all-small-W config cannot vacuous-succeed.
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
        /// Call only after writers are closed and the derived store exporter is released.
        /// </summary>
        public void MarkCompleted()
        {
            if (!this.writersFlushedOk)
            {
                throw new InvalidOperationException(
                    "MarkCompleted requires CloseWriters to finish flush/dispose successfully "
                    + "so success is not declared after a failed close.");
            }

            this.completedSuccessfully = true;
        }

        /// <summary>
        /// Flushes and closes CSV writers after pipeline WaitAll (store disposed with pipeline).
        /// Stops new CSV rows immediately; sets <see cref="writersFlushedOk"/> only when every writer closes cleanly.
        /// </summary>
        public void CloseWriters()
        {
            lock (this.writerGate)
            {
                if (this.writersFlushedOk)
                {
                    return;
                }

                // Stop Do sinks from writing while we flush; do not treat this as success yet.
                this.writersClosed = true;
                Exception? firstFailure = null;
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
                        this.streamWriters[i] = null;
                    }
                    catch (Exception ex)
                    {
                        if (firstFailure == null)
                        {
                            firstFailure = ex;
                        }

                        this.TryLog("CSV writer close failure: " + ex.Message);
                    }
                }

                if (firstFailure != null)
                {
                    throw new InvalidOperationException(
                        "Failed to flush/close one or more CSV writers; refusing to mark export success.",
                        firstFailure);
                }

                this.writersFlushedOk = true;
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

            try
            {
                this.CloseWriters();
            }
            catch (Exception ex)
            {
                this.TryLog("CSV close during incomplete cleanup: " + ex.Message);
            }

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
                try
                {
                    this.CloseWriters();
                }
                catch (Exception ex)
                {
                    this.TryLog("CSV close during dispose: " + ex.Message);
                }
            }
        }

        private static void RequireBothParticipants(IReadOnlyList<BoundBranchDescriptor> branches)
        {
            Dictionary<int, bool> hasM1ByW = new Dictionary<int, bool>();
            Dictionary<int, bool> hasM2ByW = new Dictionary<int, bool>();

            for (int i = 0; i < branches.Count; i++)
            {
                BoundBranchDescriptor branch = branches[i];
                if (branch == null)
                {
                    continue;
                }

                int windowMs = branch.WindowMs;
                if (!hasM1ByW.ContainsKey(windowMs))
                {
                    hasM1ByW[windowMs] = false;
                    hasM2ByW[windowMs] = false;
                }

                if (branch.Participant == ParticipantId.M1)
                {
                    hasM1ByW[windowMs] = true;
                }
                else if (branch.Participant == ParticipantId.M2)
                {
                    hasM2ByW[windowMs] = true;
                }
            }

            if (hasM1ByW.Count == 0)
            {
                throw new InvalidOperationException(
                    "Derived export requires both M1 and M2 bound branches; refusing one-sided success.");
            }

            foreach (KeyValuePair<int, bool> entry in hasM1ByW)
            {
                bool hasM2;
                if (!entry.Value
                    || !hasM2ByW.TryGetValue(entry.Key, out hasM2)
                    || !hasM2)
                {
                    throw new InvalidOperationException(
                        "Derived export requires both M1 and M2 bound branches for every W; "
                        + "missing participant pair for W=" + entry.Key + ".");
                }
            }
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Best-effort orphan cleanup; caller still rethrows the original create failure.
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
