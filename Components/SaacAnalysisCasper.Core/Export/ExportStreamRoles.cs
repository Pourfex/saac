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
        /// Classification product stream-role prefix (e.g. <c>Logigramme1/M1/Classification_W1000</c>).
        /// Alpha/Beta/Gamma is a payload field, not three stream roles (AD-13).
        /// </summary>
        public const string Classification = "Classification";

        /// <summary>
        /// Formats the per-W coincidence stream role: <c>C_W{Wms}</c> (avoids multi-W collisions in one store).
        /// </summary>
        /// <param name="windowMs">Window length W in milliseconds.</param>
        /// <returns>Stream role segment for AD-5 store names.</returns>
        public static string CoincidenceForWindow(int windowMs)
        {
            return C + "_W" + windowMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Formats the per-W classification stream role: <c>Classification_W{Wms}</c>
        /// (avoids multi-W collisions in one derived store under <c>windowMsSweep</c>).
        /// </summary>
        /// <param name="windowMs">Window length W in milliseconds.</param>
        /// <returns>Stream role segment for AD-5 store names.</returns>
        public static string ClassificationForWindow(int windowMs)
        {
            return Classification + "_W" + windowMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
