// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Replay
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using System.Windows;
    using Microsoft.Win32;
    using SaacAnalysisCasper.Core.Config;
    using SaacAnalysisCasper.Replay.Services;

    /// <summary>
    /// Operator host: browse dataset/session, load Core run-config, open FullSpeed offline Replay.
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly string DefaultDatasetDirectory =
            @"C:\Users\alexi\Documents\Dev\Ressources\Casper81\Casper5";

        private static readonly string DefaultRunConfigPath =
            @"C:\Users\alexi\Documents\Dev\saac\Components\SaacAnalysisCasper.Core\Config\Samples\analysis-run.sample.json";

        private readonly List<string> sessionNames = new List<string>();
        private bool isRunning;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            this.InitializeComponent();
            this.RunConfigPathTextBox.Text = DefaultRunConfigPath;

            if (Directory.Exists(DefaultDatasetDirectory))
            {
                string defaultPds = Path.Combine(DefaultDatasetDirectory, "Casper5.pds");
                if (File.Exists(defaultPds))
                {
                    this.DatasetPathTextBox.Text = defaultPds;
                    this.TryLoadSessions(defaultPds, showMessageBox: false);
                }
            }

            this.AppendLog("Ready. Browse a .pds (or use starter default), select a session, confirm run-config, then Run.");
        }

        private void BrowseDataset_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Psi Dataset (*.pds)|*.pds|All files (*.*)|*.*",
                Title = "Select Dataset File",
                CheckFileExists = true,
            };

            if (Directory.Exists(DefaultDatasetDirectory))
            {
                dialog.InitialDirectory = DefaultDatasetDirectory;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            this.DatasetPathTextBox.Text = dialog.FileName;
            this.TryLoadSessions(dialog.FileName, showMessageBox: true);
        }

        private void BrowseRunConfig_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Select AnalysisRunConfig JSON",
                CheckFileExists = true,
            };

            string current = this.RunConfigPathTextBox.Text;
            if (!string.IsNullOrWhiteSpace(current))
            {
                string dir = Path.GetDirectoryName(current);
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

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (this.isRunning)
            {
                this.AppendLog("A replay is already running.");
                return;
            }

            string pdsPath = this.DatasetPathTextBox.Text;
            string selectedSession = this.SessionComboBox.SelectedItem as string;
            string runConfigPath = this.RunConfigPathTextBox.Text;

            if (string.IsNullOrWhiteSpace(pdsPath))
            {
                this.Fail("Please browse to a .pds dataset file first.");
                return;
            }

            if (!File.Exists(pdsPath))
            {
                this.Fail("Dataset file not found: " + pdsPath);
                return;
            }

            if (this.sessionNames.Count == 0)
            {
                this.Fail("No sessions available. Load a valid .pds that contains at least one session.");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedSession))
            {
                this.Fail("Please select a session before Run.");
                return;
            }

            if (string.IsNullOrWhiteSpace(runConfigPath) || !File.Exists(runConfigPath))
            {
                this.Fail("Run-config JSON missing or not found: " + runConfigPath);
                return;
            }

            AnalysisRunConfig runConfig;
            try
            {
                runConfig = AnalysisRunConfig.Load(runConfigPath);
            }
            catch (Exception ex)
            {
                this.Fail("Failed to load Core AnalysisRunConfig: " + ex.Message);
                return;
            }

            this.isRunning = true;
            this.SetUiEnabled(false);
            this.StatusTextBlock.Text = "Running…";
            this.AppendLog("--- Run started ---");

            List<string> sessionsSnapshot = new List<string>(this.sessionNames);
            try
            {
                await Task.Run(() =>
                {
                    ReplaySessionOpener opener = new ReplaySessionOpener(this.AppendLogFromBackground);
                    opener.OpenAndSmokeRun(pdsPath, selectedSession, runConfig, sessionsSnapshot);
                }).ConfigureAwait(true);

                this.StatusTextBlock.Text = "Done";
                this.AppendLog("--- Run finished ---");
            }
            catch (Exception ex)
            {
                this.StatusTextBlock.Text = "Failed";
                this.Fail(ex.Message);
            }
            finally
            {
                this.isRunning = false;
                if (!this.Dispatcher.HasShutdownStarted)
                {
                    this.SetUiEnabled(true);
                }
            }
        }

        private void TryLoadSessions(string pdsPath, bool showMessageBox)
        {
            this.sessionNames.Clear();
            this.SessionComboBox.Items.Clear();

            try
            {
                IReadOnlyList<string> names = ReplaySessionOpener.EnumerateSessionNames(pdsPath);
                if (names.Count == 0)
                {
                    this.Fail("No sessions found in dataset: " + pdsPath, showMessageBox);
                    return;
                }

                foreach (string name in names)
                {
                    this.sessionNames.Add(name);
                    this.SessionComboBox.Items.Add(name);
                }

                this.SessionComboBox.SelectedIndex = 0;
                this.AppendLog("Dataset loaded: " + pdsPath);
                this.AppendLog("Sessions: " + string.Join(", ", this.sessionNames));
                this.AppendLog("Selected session: " + this.SessionComboBox.SelectedItem);
            }
            catch (Exception ex)
            {
                this.Fail("Error loading dataset: " + ex.Message, showMessageBox);
            }
        }

        private void SetUiEnabled(bool enabled)
        {
            this.BrowseDatasetButton.IsEnabled = enabled;
            this.BrowseRunConfigButton.IsEnabled = enabled;
            this.SessionComboBox.IsEnabled = enabled;
            this.RunButton.IsEnabled = enabled;
            this.RunConfigPathTextBox.IsEnabled = enabled;
        }

        private void Fail(string message, bool showMessageBox = true)
        {
            this.StatusTextBlock.Text = "Failed";
            this.AppendLog("ERROR: " + message);
            if (showMessageBox && !this.Dispatcher.HasShutdownStarted)
            {
                MessageBox.Show(this, message, "SaacAnalysisCasper Replay", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AppendLog(string message)
        {
            if (this.Dispatcher.HasShutdownStarted)
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
                this.Dispatcher.BeginInvoke(new Action(() => this.AppendLog(message)));
                return;
            }

            this.AppendLog(message);
        }
    }
}
