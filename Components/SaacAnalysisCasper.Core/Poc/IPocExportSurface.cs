// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Poc
{
    using Microsoft.Psi;

    /// <summary>
    /// Small Core-facing export/inject surface for the Poc graph so hosts avoid fragile concrete casts.
    /// </summary>
    public interface IPocExportSurface
    {
        /// <summary>
        /// Gets the window length W closed over by this POC instance.
        /// </summary>
        int WindowMs { get; }

        /// <summary>
        /// Gets the inject receiver for the unified A|B tagged stream (host-owned sources PipeTo this).
        /// </summary>
        Receiver<PocTaggedEvent> InjectIn { get; }

        /// <summary>
        /// Gets the coincidence product C emitter for store + CSV export.
        /// </summary>
        IProducer<PocCoincidenceC> CoincidenceOut { get; }
    }
}
