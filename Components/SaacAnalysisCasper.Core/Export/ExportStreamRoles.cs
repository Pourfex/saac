// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Export
{
    /// <summary>
    /// Stable stream-role segments for AD-5 store names (<c>{graphId}/{participant}/{streamRole}</c>).
    /// </summary>
    public static class ExportStreamRoles
    {
        /// <summary>
        /// Marker / count stream role (e.g. <c>Poc/M1/Marker</c>). Story 1.5 stub; optional after 1.7.
        /// </summary>
        public const string Marker = "Marker";

        /// <summary>
        /// Coincidence product C stream-role prefix (e.g. <c>Poc/M1/C_W1000</c>).
        /// </summary>
        public const string C = "C";

        /// <summary>
        /// Formats the per-W coincidence stream role: <c>C_W{Wms}</c> (avoids multi-W collisions in one store).
        /// </summary>
        /// <param name="windowMs">Window length W in milliseconds.</param>
        /// <returns>Stream role segment for AD-5 store names.</returns>
        public static string CoincidenceForWindow(int windowMs)
        {
            return C + "_W" + windowMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
