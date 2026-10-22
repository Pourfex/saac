// <copyright file="LogigramState.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1
{
    /// <summary>
    /// Session-scoped Logigram 1 progress / position state message (AD-11).
    /// Regulation terminals live on <see cref="LogigramOutcome"/> / <c>Logigram1.Outcome</c>, not here.
    /// No Battery id/index fields until story 2.3 / OQ3.
    /// </summary>
    public class LogigramState
    {
        /// <summary>
        /// Canonical AD-11 state stream name. Producers and consumers must use this sole name.
        /// </summary>
        public const string StreamName = "Logigram1.State";

        /// <summary>
        /// Gets or sets the display/status string for the current session state.
        /// </summary>
        public string Status { get; set; } = string.Empty;
    }
}
