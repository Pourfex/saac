// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Poc
{
    /// <summary>
    /// Derived coincidence product C for A∧B-within-W (AD-7 Core analysis DTO).
    /// OriginatingTime policy (documented once): C is posted at B's OriginatingTime
    /// (equals the hopping-window anchor when emission is gated on the latest window message being B).
    /// </summary>
    public sealed class PocCoincidenceC
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PocCoincidenceC"/> class.
        /// </summary>
        /// <param name="windowMs">Window length W that produced this coincidence.</param>
        public PocCoincidenceC(int windowMs)
        {
            this.WindowMs = windowMs;
        }

        /// <summary>
        /// Gets the window length W (milliseconds) closed over by the producing POC instance.
        /// </summary>
        public int WindowMs { get; }
    }
}
