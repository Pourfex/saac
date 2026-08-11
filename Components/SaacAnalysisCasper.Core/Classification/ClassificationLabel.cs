// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Classification
{
    /// <summary>
    /// Piaget classification label for Logigramme outcomes (AD-13).
    /// Alpha / Beta / Gamma only — no Apprentissage, N/A, None, Anticipation*, or scores.
    /// Correct Course Option C sticky→wire collapse (E→Gamma emit, D→no-emit) is documented in
    /// <c>Classification/OptionC.md</c>; do not expand this enum for those stickies.
    /// </summary>
    public enum ClassificationLabel
    {
        /// <summary>
        /// Alpha outcome (spec sticky A).
        /// </summary>
        Alpha = 0,

        /// <summary>
        /// Beta outcome (spec sticky B).
        /// </summary>
        Beta = 1,

        /// <summary>
        /// Gamma outcome (spec sticky C, and Option C emit for sticky E Apprentissage).
        /// </summary>
        Gamma = 2,
    }
}
