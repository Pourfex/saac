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
    using SaacAnalysisCasper.Core.Classification;
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
        private readonly Dictionary<string, bool> coincidenceCsvByPath;
        private readonly Dictionary<string, HashSet<ClassificationLabel>> labelsByCsvPath;
        private readonly Dictionary<string, ClassificationLabel[]> expectedLabelsByCsvPath;
        private readonly Dictionary<string, ClassificationLabel[]> forbiddenLabelsByCsvPath;
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
            this.coincidenceCsvByPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            this.labelsByCsvPath = new Dictionary<string, HashSet<ClassificationLabel>>(StringComparer.OrdinalIgnoreCase);
            this.expectedLabelsByCsvPath = new Dictionary<string, ClassificationLabel[]>(StringComparer.OrdinalIgnoreCase);
            this.forbiddenLabelsByCsvPath = new Dictionary<string, ClassificationLabel[]>(StringComparer.OrdinalIgnoreCase);
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
            HashSet<string> classificationRegisteredGraphs = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < branches.Count; i++)
            {
                BoundBranchDescriptor branch = branches[i];
                if (branch == null)
                {
                    throw new ArgumentException("branches contains a null entry.", nameof(branches));
                }

                if (branch.CoincidenceOut == null && branch.ClassificationOut == null)
                {
                    throw new InvalidOperationException(
                        "Branch graph=" + branch.GraphId
                        + " participant=" + branch.Participant
                        + " W=" + branch.WindowMs
                        + " has neither CoincidenceOut nor ClassificationOut.");
                }

                PsiExporter graphExporter = this.EnsureGraphExporter(
                    exportersByGraph,
                    pipeline,
                    runConfig.OutputRoot,
                    branch.GraphId,
                    sessionName,
                    datasetIdentity);

                if (branch.CoincidenceOut != null)
                {
                    this.AttachCoincidenceExport(
                        graphExporter,
                        branch,
                        runConfig.OutputRoot,
                        sessionName,
                        datasetIdentity,
                        csvPathSet);
                }

                if (branch.ClassificationOut != null)
                {
                    if (classificationRegisteredGraphs.Add(branch.GraphId))
                    {
                        ClassificationSerialization.EnsureRegistered(graphExporter.Serializers);
                    }

                    this.AttachClassificationExport(
                        graphExporter,
                        branch,
                        runConfig.OutputRoot,
                        sessionName,
                        datasetIdentity,
                        csvPathSet);
                }
            }
        }

        private PsiExporter EnsureGraphExporter(
            Dictionary<string, PsiExporter> exportersByGraph,
            Pipeline pipeline,
            string outputRoot,
            string graphId,
            string sessionName,
            string datasetIdentity)
        {
            PsiExporter existing;
            if (exportersByGraph.TryGetValue(graphId, out existing))
            {
                return existing;
            }

            string storeDirectory = ExportPathFormatter.FormatDerivedStoreDirectory(
                outputRoot,
                graphId,
                sessionName);
            string storeFolderName = ExportPathFormatter.FormatDerivedStoreName(
                graphId,
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

            exportersByGraph.Add(graphId, exporter);
            this.storeDirectories.Add(storeDirectory);

            this.log(
                "Derived store created: graph=" + graphId
                + " session=" + sessionName
                + " dataset=" + datasetIdentity
                + " storeFolder=" + storeFolderName
                + " storeName=" + DerivedStoreLeafName
                + " path=" + storeDirectory);

            return exporter;
        }

        private void AttachCoincidenceExport(
            PsiExporter graphExporter,
            BoundBranchDescriptor branch,
            string outputRoot,
            string sessionName,
            string datasetIdentity,
            HashSet<string> csvPathSet)
        {
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
                outputRoot,
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
            this.coincidenceCsvByPath[csvPath] = true;

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

        private void AttachClassificationExport(
            PsiExporter graphExporter,
            BoundBranchDescriptor branch,
            string outputRoot,
            string sessionName,
            string datasetIdentity,
            HashSet<string> csvPathSet)
        {
            // Story 2.4: known-trace Classification store+CSV inspect; Story 2.5 PreTest catalog reuses this attach.
            string streamRole = ExportStreamRoles.ClassificationForWindow(branch.WindowMs);
            string streamName = ExportPathFormatter.FormatStreamName(
                branch.GraphId,
                branch.Participant,
                streamRole);

            StoreExportHelper.Write(graphExporter, branch.ClassificationOut, streamName);
            this.log(
                "Store stream wired: " + streamName
                + " participant=" + branch.Participant
                + " W=" + branch.WindowMs
                + " graph=" + branch.GraphId
                + " role=" + streamRole);

            // Classification CSV shares AD-5 path shape with coincidence (one file per graph×session×participant×W).
            string csvPath = ExportPathFormatter.FormatCsvPath(
                outputRoot,
                branch.GraphId,
                sessionName,
                branch.Participant,
                branch.WindowMs);

            if (!csvPathSet.Add(csvPath))
            {
                throw new InvalidOperationException(
                    "Duplicate CSV export path '" + csvPath
                    + "' — Classification and coincidence cannot share the same path for one branch; "
                    + "or two classification branches collided.");
            }

            StreamWriter streamWriter = new StreamWriter(csvPath, append: false, CsvExportFormat.Utf8WithoutBom);
            this.streamWriters.Add(streamWriter);
            this.csvPaths.Add(csvPath);
            this.rowsByCsvPath[csvPath] = 0;
            this.requireRowsByCsvPath[csvPath] = branch.ExpectClassificationRows;
            this.coincidenceCsvByPath[csvPath] = false;
            this.labelsByCsvPath[csvPath] = new HashSet<ClassificationLabel>();
            ClassificationLabel[] expected = new ClassificationLabel[branch.ExpectedClassificationLabels.Count];
            for (int i = 0; i < branch.ExpectedClassificationLabels.Count; i++)
            {
                expected[i] = branch.ExpectedClassificationLabels[i];
            }

            this.expectedLabelsByCsvPath[csvPath] = expected;
            ClassificationLabel[] forbidden = new ClassificationLabel[branch.ForbiddenClassificationLabels.Count];
            for (int i = 0; i < branch.ForbiddenClassificationLabels.Count; i++)
            {
                forbidden[i] = branch.ForbiddenClassificationLabels[i];
            }

            this.forbiddenLabelsByCsvPath[csvPath] = forbidden;

            CsvExportWriter csvWriter = new CsvExportWriter(streamWriter);
            csvWriter.WriteClassificationHeader();

            CsvExportWriter capturedWriter = csvWriter;
            string capturedPath = csvPath;
            object gate = this.writerGate;
            branch.ClassificationOut.Do(
                (ClassificationEvent classification, Envelope envelope) =>
                {
                    lock (gate)
                    {
                        if (this.writersClosed)
                        {
                            return;
                        }

                        if (classification == null)
                        {
                            throw new InvalidOperationException(
                                "Null ClassificationEvent on CSV sink for '" + capturedPath
                                + "'; refusing to forge a classification row.");
                        }

                        capturedWriter.WriteClassificationRow(envelope.OriginatingTime, classification);
                        this.rowsByCsvPath[capturedPath] = this.rowsByCsvPath[capturedPath] + 1;
                        this.labelsByCsvPath[capturedPath].Add(classification.Label);
                    }
                },
                DeliveryPolicy.Unlimited);

            this.log(
                "Classification CSV export wired: participant=" + branch.Participant
                + " W=" + branch.WindowMs
                + " session=" + sessionName
                + " dataset=" + datasetIdentity
                + " graph=" + branch.GraphId
                + " expectRows=" + branch.ExpectClassificationRows
                + " path=" + csvPath);
        }

        /// <summary>
        /// Ensures CSV science gates for inject / known-trace schedules.
        /// Coincidence: matching-W branches require ≥1 C row; non-matching must stay at 0.
        /// Classification: documented known-trace hits (<see cref="BoundBranchDescriptor.ExpectClassificationRows"/>)
        /// require ≥1 row (fail-closed on zero); expected/forbidden labels gated when configured.
        /// </summary>
        public void EnsureExportRowsPresent()
        {
            lock (this.writerGate)
            {
                if (this.csvPaths.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No CSV paths were attached; refusing vacuous export success.");
                }

                bool anyCoincidenceExpected = false;
                bool sawCoincidenceCsv = false;

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

                    bool isCoincidence;
                    if (!this.coincidenceCsvByPath.TryGetValue(path, out isCoincidence))
                    {
                        throw new InvalidOperationException(
                            "CSV path '" + path
                            + "' is missing coincidence/classification role tracking; refusing ambiguous export gate.");
                    }

                    if (isCoincidence)
                    {
                        sawCoincidenceCsv = true;
                        if (requireRows)
                        {
                            anyCoincidenceExpected = true;
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
                    else
                    {
                        HashSet<ClassificationLabel> seenLabels;
                        if (!this.labelsByCsvPath.TryGetValue(path, out seenLabels) || seenLabels == null)
                        {
                            seenLabels = new HashSet<ClassificationLabel>();
                        }

                        if (requireRows && rows < 1)
                        {
                            throw new InvalidOperationException(
                                "Export produced no Classification rows for CSV '" + path
                                + "' on a documented known-trace hit (ExpectClassificationRows=true). "
                                + "Fail-closed: hit scenarios must emit ≥1 Alpha/Beta/Gamma row.");
                        }

                        ClassificationLabel[] expectedLabels;
                        if (this.expectedLabelsByCsvPath.TryGetValue(path, out expectedLabels)
                            && requireRows
                            && expectedLabels != null
                            && expectedLabels.Length > 0)
                        {
                            for (int e = 0; e < expectedLabels.Length; e++)
                            {
                                if (!seenLabels.Contains(expectedLabels[e]))
                                {
                                    throw new InvalidOperationException(
                                        "Export Classification CSV '" + path
                                        + "' missing expected label " + expectedLabels[e]
                                        + " (seen labels: " + FormatSeenLabels(seenLabels) + ").");
                                }
                            }
                        }

                        ClassificationLabel[] forbidden;
                        if (this.forbiddenLabelsByCsvPath.TryGetValue(path, out forbidden)
                            && forbidden != null
                            && forbidden.Length > 0)
                        {
                            for (int f = 0; f < forbidden.Length; f++)
                            {
                                if (seenLabels.Contains(forbidden[f]))
                                {
                                    throw new InvalidOperationException(
                                        "Export Classification CSV '" + path
                                        + "' contains forbidden label " + forbidden[f]
                                        + " (seen labels: " + FormatSeenLabels(seenLabels) + ").");
                                }
                            }
                        }
                    }
                }

                if (sawCoincidenceCsv && !anyCoincidenceExpected)
                {
                    throw new InvalidOperationException(
                        "No CSV path expects coincidence rows for the inject schedule "
                        + "(all resolved W values are too small). Include at least one W that admits B_near.");
                }
            }
        }

        private static string FormatSeenLabels(HashSet<ClassificationLabel> seenLabels)
        {
            if (seenLabels == null || seenLabels.Count == 0)
            {
                return "(none)";
            }

            List<string> names = new List<string>(seenLabels.Count);
            foreach (ClassificationLabel label in seenLabels)
            {
                names.Add(label.ToString());
            }

            names.Sort(StringComparer.Ordinal);
            return string.Join(", ", names);
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
