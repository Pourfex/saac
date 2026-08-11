// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Classification
{
    /// <summary>
    /// Piaget classification label for Logigramme outcomes (AD-13).
    /// Alpha / Beta / Gamma only — no Apprentissage, None, Anticipation*, or scores.
    /// </summary>
    public enum ClassificationLabel
    {
        /// <summary>
        /// Alpha outcome.
        /// </summary>
        Alpha = 0,

        /// <summary>
        /// Beta outcome.
        /// </summary>
        Beta = 1,

        /// <summary>
        /// Gamma outcome.
        /// </summary>
        Gamma = 2,
    }
}
