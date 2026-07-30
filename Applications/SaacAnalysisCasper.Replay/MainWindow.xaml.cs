// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Replay
{
    using System.Windows;
    using SaacAnalysisCasper.Core.Config;

    /// <summary>
    /// Placeholder main window until session pick / offline replay open (Story 1.3).
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            this.InitializeComponent();

            // Story 1.2: hosts deserialize Core schema only (UI binding is Story 1.3).
            _ = AnalysisRunConfig.Parse("{\"windowMs\":1000,\"outputRoot\":\"stub\",\"graphs\":[\"Poc\"]}");
        }
    }
}
