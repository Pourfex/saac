// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.PsiStudioPlugin
{
    using System;
    using System.ComponentModel;
    using System.Windows;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using Microsoft.Psi.PsiStudio.PipelinePlugin;
    using SaacAnalysisCasper.Core;
    using SaacAnalysisCasper.Core.Config;

    /// <summary>
    /// Thin PsiStudio pipeline plugin stub. Must derive from <see cref="Window"/> for discovery.
    /// Full host-swap proof is Story 1.8.
    /// </summary>
    public partial class CasperPipelinePluginWindow : Window, IPsiStudioPipeline
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CasperPipelinePluginWindow"/> class.
        /// </summary>
        public CasperPipelinePluginWindow()
        {
            // Story 1.2: hosts deserialize Core schema only (UI binding is Story 1.8).
            _ = AnalysisRunConfig.Parse("{\"windowMs\":1000,\"outputRoot\":\"stub\",\"graphs\":[\"Poc\"]}");

            try
            {
                this.InitializeComponent();
            }
            catch
            {
                this.Title = "SaacAnalysisCasper PsiStudio Plugin (" + ScaffoldStub.Marker + ")";
                this.Width = 520;
                this.Height = 260;
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
        }

        /// <inheritdoc/>
        public void StopPipeline()
        {
            if (this.IsVisible)
            {
                this.Close();
            }
        }

        /// <inheritdoc/>
        public DateTime GetStartTime() => DateTime.UtcNow;

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

            if (this.IsVisible)
            {
                this.Close();
            }
        }

        /// <inheritdoc/>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
        }
    }
}
