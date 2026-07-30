// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Mapping
{
    /// <summary>
    /// Stable composition port role identifiers (AD-12). Compositions declare these; hosts resolve topics via <see cref="PortTopicMap"/>.
    /// </summary>
    public static class PortRoleIds
    {
        /// <summary>
        /// Select-module event role (<c>M1-SelectModule</c> / <c>M2-SelectModule</c>).
        /// </summary>
        public const string SelectModule = "SelectModule";

        /// <summary>
        /// Validation event role (<c>M1-Validation</c> / <c>M2-Validation</c>).
        /// </summary>
        public const string Validation = "Validation";

        /// <summary>
        /// Module-out event role (<c>M1-ModuleOut</c> / <c>M2-ModuleOut</c>).
        /// </summary>
        public const string ModuleOut = "ModuleOut";

        /// <summary>
        /// Module-out-zone event role (<c>M1-ModuleOutZone</c> / <c>M2-ModuleOutZone</c>).
        /// </summary>
        public const string ModuleOutZone = "ModuleOutZone";
    }
}
