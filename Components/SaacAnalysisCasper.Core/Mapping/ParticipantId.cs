// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Mapping
{
    /// <summary>
    /// Dual-user participant identifiers used by Core maps (AD-3).
    /// </summary>
    public enum ParticipantId
    {
        /// <summary>
        /// Participant M1 (catalog prefixes <c>M1-</c> / <c>1-</c>, suffix <c>_1</c>).
        /// </summary>
        M1 = 1,

        /// <summary>
        /// Participant M2 (catalog prefixes <c>M2-</c> / <c>2-</c>, suffix <c>_2</c>).
        /// </summary>
        M2 = 2,
    }
}
