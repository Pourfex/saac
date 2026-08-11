// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Classification
{
    using Microsoft.Psi;

    /// <summary>
    /// Host-facing export surface for classification product graphs (e.g. Logigramme1).
    /// Parallel to <c>IPocExportSurface</c> — hosts must not hard-cast L1 to Poc.
    /// </summary>
    public interface IClassificationExportSurface
    {
        /// <summary>
        /// Gets the window length W closed over by this composition instance (AD-8 attribution).
        /// </summary>
        int WindowMs { get; }

        /// <summary>
        /// Gets the Alpha/Beta/Gamma classification product stream.
        /// </summary>
        IProducer<ClassificationEvent> ClassificationOut { get; }
    }
}
