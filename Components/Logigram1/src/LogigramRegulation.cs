// <copyright file="LogigramRegulation.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1
{
    /// <summary>
    /// Typed regulation / terminal-outcome alphabet for Logigram 1 (AD-10).
    /// Visualizations and CSV must not invent values outside this set.
    /// </summary>
    public enum LogigramRegulation
    {
        /// <summary>Regulation Alpha.</summary>
        Alpha = 0,

        /// <summary>Regulation Beta.</summary>
        Beta = 1,

        /// <summary>Regulation Gamma.</summary>
        Gamma = 2,

        /// <summary>Not applicable (contract label <c>N/A</c>).</summary>
        NA = 3,

        /// <summary>Apprentissage terminal.</summary>
        Apprentissage = 4,
    }

    /// <summary>
    /// Contract labels for <see cref="LogigramRegulation"/> (including the <c>N/A</c> spelling).
    /// </summary>
    public static class LogigramRegulationLabels
    {
        /// <summary>Gets the locked contract label for a regulation value.</summary>
        /// <param name="regulation">The typed regulation.</param>
        /// <returns>The alphabet label string.</returns>
        public static string ToContractLabel(LogigramRegulation regulation)
        {
            switch (regulation)
            {
                case LogigramRegulation.Alpha:
                    return "Alpha";
                case LogigramRegulation.Beta:
                    return "Beta";
                case LogigramRegulation.Gamma:
                    return "Gamma";
                case LogigramRegulation.NA:
                    return "N/A";
                case LogigramRegulation.Apprentissage:
                    return "Apprentissage";
                default:
                    return regulation.ToString();
            }
        }
    }
}
