// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.PsiStudioPlugin
{
    using System;
    using System.ComponentModel;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Threading;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using Microsoft.Psi.PsiStudio.PipelinePlugin;
    using Microsoft.Win32;
    using SaacAnalysisCasper.Core.Config;
    using SaacAnalysisCasper.PsiStudioPlugin.Services;

    /// <summary>
    /// Thin PsiStudio pipeline plugin host. Must derive from <see cref="Window"/> for discovery.
    /// Runs the same Core Poc graph as Replay (Story 1.8 host-swap proof).
    /// </summary>
    public partial class CasperPipelinePluginWindow : Window, IPsiStudioPipeline
    {
        private const int StopJoinTimeoutMs = 30000;

        private readonly PluginPocRunner runner;
        private readonly object startGate;
        private bool uiInitialized;
        private bool isRunning;

        /// <summary>
        /// Initializes a new instance of the <see cref="CasperPipelinePluginWindow"/> class.
        /// </summary>
        public CasperPipelinePluginWindow()
        {
            this.runner = new PluginPocRunner(this.AppendLogFromBackground);
            this.startGate = new object();

            try
            {
                this.InitializeComponent();
                this.uiInitialized = true;
                this.RunConfigPathTextBox.Text = PluginRunConfigLoader.DefaultRunConfigPath;
                this.AppendLog(
                    "Ready. Confirm run-config (AD-8), then click Run POC. "
                    + "PsiStudio RunPipeline auto-starts the same inject-only path with the path shown here.");
            }
            catch
            {
                this.uiInitialized = false;
                this.Title = "SaacAnalysisCasper PsiStudio Plugin";
                this.Width = 640;
                this.Height = 420;
            }
        }

        /// <inheritdoc/>
        public Dataset GetDataset() => null!;

        /// <inheritdoc/>
        public void RunPipeline(TimeInterval timeInterval)
        {
            if (!this.IsVisible)
            {
                this.Show();
            }

            // PsiStudio entry: start the same inject-only Core Poc proof as the Run POC button.
            this.StartPocRun();
        }

        /// <inheritdoc/>
        public void StopPipeline()
        {
            this.RequestStopAndJoin();

            this.SetStatusText("Stopped");
            this.SetUiEnabled(true);

            if (this.IsVisible)
            {
                this.Close();
            }
        }

        /// <inheritdoc/>
        public DateTime GetStartTime() => PocInjectSources.DefaultInjectBaseUtc;

        /// <inheritdoc/>
        public PipelineReplaybleMode GetReplaybleMode() => PipelineReplaybleMode.Not;

        /// <inheritdoc/>
        public new void Dispose()
        {
            if (!this.Dispatcher.CheckAccess())
            {
                this.Dispatcher.Invoke(() => this.Dispose());
                return;
            }

            this.RequestStopAndJoin();

            if (this.IsVisible)
            {
                this.Close();
            }
        }

        /// <inheritdoc/>
        protected override void OnClosing(CancelEventArgs e)
        {
            this.RequestStopAndJoin();
            base.OnClosing(e);
        }

        private void BrowseRunConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!this.uiInitialized || this.RunConfigPathTextBox == null)
            {
                this.Fail("UI unavailable (XAML failed to load); cannot browse run-config.");
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Select AnalysisRunConfig JSON",
                CheckFileExists = true,
            };

            string current = this.RunConfigPathTextBox.Text;
            if (!string.IsNullOrWhiteSpace(current))
            {
                string? dir = null;
                try
                {
                    dir = Path.GetDirectoryName(current);
                }
                catch (ArgumentException)
                {
                    dir = null;
                }

                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                }
            }

            if (dialog.ShowDialog(this) == true)
            {
                this.RunConfigPathTextBox.Text = dialog.FileName;
                this.AppendLog("Run-config path set: " + dialog.FileName);
            }
        }

        private void RunPoc_Click(object sender, RoutedEventArgs e)
        {
            this.StartPocRun();
        }

        private async void StartPocRun()
        {
            if (!this.uiInitialized)
            {
                this.Fail("UI unavailable (XAML failed to load); refusing Plugin POC run.");
                return;
            }

            lock (this.startGate)
            {
                if (this.isRunning || this.runner.IsRunning)
                {
                    this.AppendLog("A Plugin POC run is already in progress.");
                    return;
                }

                this.isRunning = true;
            }

            string runConfigPath = this.RunConfigPathTextBox != null
                ? this.RunConfigPathTextBox.Text
                : PluginRunConfigLoader.DefaultRunConfigPath;

            AnalysisRunConfig runConfig;
            try
            {
                if (string.IsNullOrWhiteSpace(runConfigPath) || !File.Exists(runConfigPath))
                {
                    throw new FileNotFoundException(
                        "Run-config JSON missing or not found: " + runConfigPath,
                        runConfigPath);
                }

                runConfig = PluginRunConfigLoader.Load(runConfigPath);
            }
            catch (Exception ex)
            {
                lock (this.startGate)
                {
                    this.isRunning = false;
                }

                this.Fail("Failed to load Core AnalysisRunConfig: " + ex.Message);
                return;
            }

            try
            {
                this.SetUiEnabled(false);
                this.SetStatusText("Running…");
                this.AppendLog("--- Plugin POC started ---");

                await Task.Run(() => this.runner.Run(runConfig)).ConfigureAwait(true);

                this.SetStatusText("Done");
                this.AppendLog("--- Plugin POC finished ---");
            }
            catch (Exception ex)
            {
                this.Fail(ex.Message);
            }
            finally
            {
                lock (this.startGate)
                {
                    this.isRunning = false;
                }

                if (!this.Dispatcher.HasShutdownStarted)
                {
                    this.SetUiEnabled(true);
                }
            }
        }

        private void RequestStopAndJoin()
        {
            try
            {
                this.runner.Stop();
            }
            catch (Exception ex)
            {
                this.AppendLog("Stop warning: " + ex.Message);
            }

            this.WaitForRunnerIdle();
        }

        private void WaitForRunnerIdle()
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(StopJoinTimeoutMs);
            while (this.runner.IsRunning && DateTime.UtcNow < deadline)
            {
                // Pump the dispatcher so StartPocRun's ConfigureAwait(true) continuation can finish
                // while StopPipeline/Dispose runs on the UI thread.
                if (this.Dispatcher.CheckAccess())
                {
                    DispatcherFrame frame = new DispatcherFrame();
                    this.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => { frame.Continue = false; }));
                    Dispatcher.PushFrame(frame);
                }
                else
                {
                    Thread.Sleep(25);
                }
            }

            if (this.runner.IsRunning)
            {
                this.AppendLog(
                    "Stop warning: Plugin POC runner still busy after "
                    + StopJoinTimeoutMs + "ms; continuing host teardown.");
            }
        }

        private void SetUiEnabled(bool enabled)
        {
            if (this.BrowseRunConfigButton != null)
            {
                this.BrowseRunConfigButton.IsEnabled = enabled;
            }

            if (this.RunPocButton != null)
            {
                this.RunPocButton.IsEnabled = enabled;
            }

            if (this.RunConfigPathTextBox != null)
            {
                this.RunConfigPathTextBox.IsEnabled = enabled;
            }
        }

        private void Fail(string message)
        {
            this.SetStatusText("Failed");
            this.AppendLog("ERROR: " + message);
            if (!this.Dispatcher.HasShutdownStarted && this.uiInitialized)
            {
                MessageBox.Show(
                    this,
                    message,
                    "SaacAnalysisCasper PsiStudio Plugin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SetStatusText(string text)
        {
            if (this.Dispatcher.HasShutdownStarted || this.StatusTextBlock == null)
            {
                return;
            }

            this.StatusTextBlock.Text = text;
        }

        private void AppendLog(string message)
        {
            if (this.Dispatcher.HasShutdownStarted || this.LogTextBox == null)
            {
                return;
            }

            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine;
            this.LogTextBox.AppendText(line);
            this.LogTextBox.ScrollToEnd();
        }

        private void AppendLogFromBackground(string message)
        {
            if (this.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (!this.Dispatcher.CheckAccess())
            {
                try
                {
                    this.Dispatcher.BeginInvoke(new Action(() => this.AppendLog(message)));
                }
                catch (InvalidOperationException)
                {
                    // Dispatcher shutting down between the check and BeginInvoke.
                }

                return;
            }

            this.AppendLog(message);
        }
    }
}
