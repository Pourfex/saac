// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.PsiStudioPlugin
{
    using System;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using Microsoft.Psi.PsiStudio.PipelinePlugin;

    /// <summary>
    /// Interface required by PsiStudio's pipeline plugin loader.
    /// The loader checks the interface name via reflection ("IPsiStudioPipeline").
    /// </summary>
    public interface IPsiStudioPipeline
    {
        /// <summary>
        /// Gets the dataset exposed to PsiStudio, if any.
        /// </summary>
        /// <returns>The dataset, or null when not yet configured.</returns>
        Dataset GetDataset();

        /// <summary>
        /// Runs the analysis pipeline for the given interval.
        /// </summary>
        /// <param name="timeInterval">Replay time interval selected in PsiStudio.</param>
        void RunPipeline(TimeInterval timeInterval);

        /// <summary>
        /// Stops a running pipeline.
        /// </summary>
        void StopPipeline();

        /// <summary>
        /// Releases plugin resources.
        /// </summary>
        void Dispose();

        /// <summary>
        /// Gets the pipeline start time.
        /// </summary>
        /// <returns>Start time used for replay.</returns>
        DateTime GetStartTime();

        /// <summary>
        /// Gets the PsiStudio replayable mode for this plugin.
        /// </summary>
        /// <returns>Replayable mode enum expected by PsiStudio.</returns>
        PipelineReplaybleMode GetReplaybleMode();
    }
}
