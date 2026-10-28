// <copyright file="LogigramState.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1
{
    /// <summary>
    /// Battery-scoped Logigram 1 progress / position state message (AD-11 / AD-6).
    /// Regulation terminals live on <see cref="LogigramOutcome"/> / <c>Logigram1.Outcome</c>, not here.
    /// <see cref="BatteryId"/> follows the sole Spawn→Finish boundary rule (replaces AD-12 session-only envelopes).
    /// </summary>
    public class LogigramState
    {
        /// <summary>
        /// Canonical AD-11 state stream name. Producers and consumers must use this sole name.
        /// </summary>
        public const string StreamName = "Logigram1.State";

        /// <summary>
        /// Gets or sets the display/status string for the current Battery-scoped state.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the canonical Battery identity from <c>BatterySpawn.BatteryId</c> (AD-6).
        /// </summary>
        public int BatteryId { get; set; }

        /// <summary>
        /// Gets or sets the participant identity for this State (1 or 2). Default 0 means unset.
        /// Independent Gamma cycles run per (BatteryId, PersonId).
        /// </summary>
        public int PersonId { get; set; }
    }
}
