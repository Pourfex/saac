// <copyright file="LogigramOutcome.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1
{
    /// <summary>
    /// Session-scoped Logigram 1 terminal / regulation outcome message (OUTCOME_SHAPE B).
    /// Only the designated outcome writer may produce the <see cref="StreamName"/> stream (AD-10).
    /// No Battery id/index fields until story 2.3 / OQ3.
    /// </summary>
    public class LogigramOutcome
    {
        /// <summary>
        /// Canonical AD-10 outcome stream name. The designated writer is the sole producer.
        /// </summary>
        public const string StreamName = "Logigram1.Outcome";

        /// <summary>
        /// Gets or sets the regulation / terminal value from the locked alphabet.
        /// Null means absent / not yet classified — default construction must not invent Alpha.
        /// </summary>
        public LogigramRegulation? Regulation { get; set; }
    }
}
