// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Replay.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Microsoft.Psi.Data;
    using SAAC;
    using SAAC.PipelineServices;
    using SaacAnalysisCasper.Core.Config;

    /// <summary>
    /// Opens a selected dataset/session via PipelineServices offline <see cref="ReplayPipeline"/> (FullSpeed).
    /// </summary>
    public sealed class ReplaySessionOpener
    {
        private readonly LogStatus log;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReplaySessionOpener"/> class.
        /// </summary>
        /// <param name="log">Host logging delegate.</param>
        public ReplaySessionOpener(LogStatus log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// Enumerates session names from a <c>.pds</c> without auto-saving (browse-time only).
        /// </summary>
        /// <param name="pdsFullPath">Full path to the <c>.pds</c> file.</param>
        /// <returns>Session names in dataset order.</returns>
        public static IReadOnlyList<string> EnumerateSessionNames(string pdsFullPath)
        {
            if (string.IsNullOrWhiteSpace(pdsFullPath) || !File.Exists(pdsFullPath))
            {
                throw new InvalidOperationException("Invalid or missing dataset file: " + pdsFullPath);
            }

            Dataset dataset = Dataset.Load(pdsFullPath, autoSave: false);
            List<string> names = new List<string>();
            foreach (Session session in dataset.Sessions)
            {
                if (session != null && !string.IsNullOrEmpty(session.Name))
                {
                    names.Add(session.Name);
                }
            }

            return names;
        }

        /// <summary>
        /// Loads assemblies that hold Casper stream CLR types so <c>Type.GetType(AQN)</c> inside DatasetLoader succeeds.
        /// </summary>
        private static void EnsureStreamTypeAssembliesLoaded()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] names = new[]
            {
                "PsiFormats.dll",
                "Microsoft.Psi.Audio.dll",
                "Microsoft.Psi.Imaging.dll",
                "Microsoft.Psi.Spatial.Euclidean.dll",
            };

            for (int i = 0; i < names.Length; i++)
            {
                string path = Path.Combine(baseDir, names[i]);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    Assembly.LoadFrom(path);
                }
                catch (Exception ex)
                {
                    // Best-effort preload; DatasetLoader still reports per-stream failures.
                    System.Diagnostics.Debug.WriteLine("Stream type assembly preload failed for " + path + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Opens the selected store, loads connectors for the session, runs FullSpeed offline replay, then disposes.
        /// </summary>
        /// <param name="pdsFullPath">Full path to the <c>.pds</c> file.</param>
        /// <param name="sessionName">Exact session name to open.</param>
        /// <param name="runConfig">Core run-config (science knobs) already loaded by the host.</param>
        /// <param name="availableSessionNames">Session names known from UI enumeration (for clear failure messages).</param>
        public void OpenAndSmokeRun(
            string pdsFullPath,
            string sessionName,
            AnalysisRunConfig runConfig,
            IReadOnlyList<string> availableSessionNames)
        {
            if (string.IsNullOrWhiteSpace(pdsFullPath))
            {
                throw new InvalidOperationException("Dataset path is empty. Browse to a .pds file first.");
            }

            if (!File.Exists(pdsFullPath))
            {
                throw new InvalidOperationException("Dataset file not found: " + pdsFullPath);
            }

            if (string.IsNullOrWhiteSpace(sessionName))
            {
                throw new InvalidOperationException("No session selected. Select a session before Run.");
            }

            if (runConfig == null)
            {
                throw new ArgumentNullException(nameof(runConfig));
            }

            string datasetPath = Path.GetDirectoryName(pdsFullPath);
            string datasetName = Path.GetFileName(pdsFullPath);
            if (string.IsNullOrEmpty(datasetPath) || string.IsNullOrEmpty(datasetName))
            {
                throw new InvalidOperationException("Could not parse dataset directory/name from: " + pdsFullPath);
            }

            if (!this.SessionExists(availableSessionNames, sessionName))
            {
                string listed = availableSessionNames != null && availableSessionNames.Count > 0
                    ? string.Join(", ", availableSessionNames)
                    : "(none)";
                throw new InvalidOperationException(
                    "Session '" + sessionName + "' not found. Available sessions: " + listed);
            }

            this.LogRunIdentity(datasetPath, datasetName, sessionName, runConfig);

            ReplayPipelineConfiguration config = new ReplayPipelineConfiguration
            {
                DatasetPath = datasetPath,
                DatasetName = datasetName,
                ReplayType = ReplayPipeline.ReplayType.FullSpeed,
                DatasetBackup = false,
                AutomaticPipelineRun = false,
            };

            EnsureStreamTypeAssembliesLoaded();

            string datasetIdentity = Path.Combine(datasetPath, datasetName);
            ReplayPipeline replay = null;
            DerivedExportSession exportSession = null;
            try
            {
                replay = new ReplayPipeline(config, log: this.log);
                if (replay.Dataset == null)
                {
                    throw new InvalidOperationException(
                        "ReplayPipeline opened with null Dataset for " + datasetIdentity
                        + ". Missing/invalid .pds must not create a silent empty capture.");
                }

                bool sessionOnDisk = false;
                foreach (Session session in replay.Dataset.Sessions)
                {
                    if (session != null && string.Equals(session.Name, sessionName, StringComparison.Ordinal))
                    {
                        sessionOnDisk = true;
                        break;
                    }
                }

                if (!sessionOnDisk)
                {
                    IEnumerable<string> onDiskNames = replay.Dataset.Sessions
                        .Where(session => session != null && !string.IsNullOrEmpty(session.Name))
                        .Select(session => session.Name);
                    throw new InvalidOperationException(
                        "Session '" + sessionName + "' not found in opened dataset. Available sessions: "
                        + (onDiskNames.Any() ? string.Join(", ", onDiskNames) : "(none)"));
                }

                if (!replay.LoadDatasetAndConnectors(sessionName: sessionName))
                {
                    IEnumerable<string> listedNames = availableSessionNames ?? Array.Empty<string>();
                    throw new InvalidOperationException(
                        "LoadDatasetAndConnectors failed for session '" + sessionName
                        + "'. Available sessions: "
                        + (listedNames.Any() ? string.Join(", ", listedNames) : "(none)"));
                }

                this.log("Dataset connectors loaded for session '" + sessionName + "'.");

                DualUserGraphBinder binder = new DualUserGraphBinder(this.log);
                IReadOnlyList<BoundBranchDescriptor> branches = binder.Bind(replay, runConfig);
                this.log("Dual-user graph binding complete (" + branches.Count + " exportable branch(es)).");

                exportSession = new DerivedExportSession(this.log);
                exportSession.AttachExports(
                    replay.Pipeline,
                    branches,
                    runConfig,
                    sessionName,
                    datasetIdentity);

                this.log("Starting FullSpeed offline replay.");
                if (!replay.RunPipelineAndSubpipelines())
                {
                    throw new InvalidOperationException("RunPipelineAndSubpipelines failed to start the offline replay pipeline.");
                }

                replay.Pipeline.WaitAll();
                exportSession.CloseWriters();
                exportSession.EnsureExportRowsPresent();

                // Release PsiExporter file locks before declaring success or running abort cleanup.
                this.StopAndDisposeReplay(ref replay);

                exportSession.MarkCompleted();
                exportSession.LogSuccessPaths();
                this.log("Replay completed.");
            }
            catch
            {
                this.StopAndDisposeReplay(ref replay);

                if (exportSession != null && !exportSession.CompletedSuccessfully)
                {
                    try
                    {
                        exportSession.CleanupIncomplete();
                    }
                    catch (Exception cleanupEx)
                    {
                        this.TryLog("Export incomplete cleanup warning: " + cleanupEx.Message);
                    }
                }

                throw;
            }
            finally
            {
                if (exportSession != null)
                {
                    try
                    {
                        exportSession.Dispose();
                    }
                    catch (Exception ex)
                    {
                        this.TryLog("Export session dispose warning: " + ex.Message);
                    }
                }

                this.StopAndDisposeReplay(ref replay);
            }
        }

        private void StopAndDisposeReplay(ref ReplayPipeline replay)
        {
            if (replay == null)
            {
                return;
            }

            ReplayPipeline local = replay;
            replay = null;

            try
            {
                local.Stop(maxWaitingTime: 5000);
            }
            catch (Exception ex)
            {
                this.TryLog("Stop warning: " + ex.Message);
            }

            try
            {
                local.Dispose();
            }
            catch (Exception ex)
            {
                this.TryLog("Dispose warning: " + ex.Message);
            }
        }

        private void LogRunIdentity(string datasetPath, string datasetName, string sessionName, AnalysisRunConfig runConfig)
        {
            this.log("=== Run identity ===");
            this.log("Dataset: " + Path.Combine(datasetPath, datasetName));
            this.log("Session: " + sessionName);
            if (runConfig.WindowMs.HasValue)
            {
                this.log("windowMs: " + runConfig.WindowMs.Value);
            }
            else
            {
                string sweep = runConfig.WindowMsSweep != null
                    ? string.Join(", ", runConfig.WindowMsSweep)
                    : string.Empty;
                this.log("windowMsSweep pending: [" + sweep + "]");
            }

            string graphs = runConfig.Graphs != null ? string.Join(", ", runConfig.Graphs) : string.Empty;
            this.log("graphs: [" + graphs + "]");
            this.log("outputRoot: " + runConfig.OutputRoot);
            this.log("ReplayType: FullSpeed (ReplayAll); DatasetBackup: false");
        }

        private bool SessionExists(IReadOnlyList<string> availableSessionNames, string sessionName)
        {
            if (availableSessionNames == null || availableSessionNames.Count == 0)
            {
                return false;
            }

            return availableSessionNames.Any(name => string.Equals(name, sessionName, StringComparison.Ordinal));
        }

        private void TryLog(string message)
        {
            try
            {
                this.log(message);
            }
            catch
            {
                // Never let logging abort Stop/Dispose cleanup.
            }
        }
    }
}
