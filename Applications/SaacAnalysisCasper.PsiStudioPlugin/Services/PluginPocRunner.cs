// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.PsiStudioPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Config;

    /// <summary>
    /// Orchestrates inject-only Core Poc under a Plugin-owned <see cref="Pipeline"/> (AD-10 / AD-11).
    /// No PipelineServices / ReplayPipeline — Core inject + Psi Data APIs only.
    /// </summary>
    public sealed class PluginPocRunner
    {
        /// <summary>
        /// Session attribution label for Plugin-derived store paths (not a capture session name).
        /// </summary>
        public const string PluginSessionName = "PluginHost";

        /// <summary>
        /// Dataset identity string for attribution logs.
        /// </summary>
        public const string PluginDatasetIdentity = "SaacAnalysisCasper.PsiStudioPlugin";

        private readonly Action<string> log;
        private readonly object runGate;
        private Pipeline? pipeline;
        private PluginExportSession? exportSession;
        private bool isRunning;
        private bool abortRequested;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginPocRunner"/> class.
        /// </summary>
        /// <param name="log">Host logging delegate.</param>
        public PluginPocRunner(Action<string> log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.runGate = new object();
        }

        /// <summary>
        /// Gets a value indicating whether a run is in progress.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (this.runGate)
                {
                    return this.isRunning;
                }
            }
        }

        /// <summary>
        /// Builds Core Poc graphs, attaches exports, runs FullSpeed, and validates CSV science gates.
        /// </summary>
        /// <param name="runConfig">Validated Core run-config.</param>
        public void Run(AnalysisRunConfig runConfig)
        {
            if (runConfig == null)
            {
                throw new ArgumentNullException(nameof(runConfig));
            }

            lock (this.runGate)
            {
                if (this.isRunning)
                {
                    throw new InvalidOperationException("A Plugin POC run is already in progress.");
                }

                this.isRunning = true;
                this.abortRequested = false;
            }

            this.exportSession = null;
            this.pipeline = null;

            try
            {
                this.ThrowIfAbortRequested();
                this.LogRunIdentity(runConfig);

                Pipeline localPipeline = Pipeline.Create("SaacAnalysisCasper.PluginPoc");
                this.pipeline = localPipeline;

                PluginPocBinder binder = new PluginPocBinder(this.log);
                IReadOnlyList<BoundBranchDescriptor> branches = binder.Bind(
                    localPipeline,
                    runConfig,
                    PocInjectSources.DefaultInjectBaseUtc);
                this.log("Dual-user Plugin graph binding complete (" + branches.Count + " exportable branch(es)).");

                PluginExportSession localExport = new PluginExportSession(this.log);
                this.exportSession = localExport;
                localExport.AttachExports(
                    localPipeline,
                    branches,
                    runConfig,
                    PluginSessionName,
                    PluginDatasetIdentity);

                this.ThrowIfAbortRequested();
                this.log("Starting FullSpeed Plugin POC pipeline (ReplayDescriptor.ReplayAll).");
                localPipeline.Run(ReplayDescriptor.ReplayAll);

                localExport.CloseWriters();
                localExport.EnsureExportRowsPresent();

                this.StopAndDisposePipeline();

                lock (this.runGate)
                {
                    if (this.abortRequested)
                    {
                        throw new OperationCanceledException(
                            "Plugin POC stop requested before export success was committed.");
                    }

                    localExport.MarkCompleted();
                }

                localExport.LogSuccessPaths();
                this.log("Plugin POC completed.");
            }
            catch
            {
                this.StopAndDisposePipeline();

                if (this.exportSession != null && !this.exportSession.CompletedSuccessfully)
                {
                    try
                    {
                        this.exportSession.CleanupIncomplete();
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
                if (this.exportSession != null)
                {
                    try
                    {
                        this.exportSession.Dispose();
                    }
                    catch (Exception ex)
                    {
                        this.TryLog("Export session dispose warning: " + ex.Message);
                    }

                    this.exportSession = null;
                }

                this.StopAndDisposePipeline();
                lock (this.runGate)
                {
                    this.isRunning = false;
                }
            }
        }

        /// <summary>
        /// Stops and disposes any in-flight pipeline (safe to call from StopPipeline / Dispose).
        /// Does not clear <see cref="IsRunning"/> — the active <see cref="Run"/> finally owns that flag
        /// so a concurrent Stop cannot open a window for a second Run while the first is still unwinding.
        /// </summary>
        public void Stop()
        {
            lock (this.runGate)
            {
                this.abortRequested = true;
            }

            this.StopAndDisposePipeline();

            PluginExportSession? session;
            lock (this.runGate)
            {
                session = this.exportSession;
            }

            if (session != null && !session.CompletedSuccessfully)
            {
                try
                {
                    session.CleanupIncomplete();
                }
                catch (Exception ex)
                {
                    this.TryLog("Stop export cleanup warning: " + ex.Message);
                }
            }
        }

        private void ThrowIfAbortRequested()
        {
            lock (this.runGate)
            {
                if (this.abortRequested)
                {
                    throw new OperationCanceledException("Plugin POC stop requested.");
                }
            }
        }

        private void StopAndDisposePipeline()
        {
            Pipeline? local = this.pipeline;
            this.pipeline = null;
            if (local == null)
            {
                return;
            }

            try
            {
                local.Dispose();
            }
            catch (Exception ex)
            {
                this.TryLog("Pipeline dispose warning: " + ex.Message);
            }
        }

        private void LogRunIdentity(AnalysisRunConfig runConfig)
        {
            this.log("=== Plugin run identity ===");
            this.log("Host: " + PluginDatasetIdentity);
            this.log("Session label: " + PluginSessionName);

            IReadOnlyList<int> windows = runConfig.EnumerateWindowMs();
            this.log("windowMs list: [" + string.Join(", ", windows) + "]");

            string graphs = runConfig.Graphs != null ? string.Join(", ", runConfig.Graphs) : string.Empty;
            this.log("graphs: [" + graphs + "]");
            this.log("outputRoot: " + runConfig.OutputRoot);
            this.log("ReplayDescriptor: ReplayAll (FullSpeed); inject-only (no capture open)");
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
