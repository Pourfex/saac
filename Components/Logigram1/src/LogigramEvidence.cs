// <copyright file="LogigramEvidence.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1
{
    /// <summary>
    /// Minimal path/node evidence stub for future Logic units (AD-10 / AD-6).
    /// Evidence producers must not write <see cref="LogigramOutcome.StreamName"/>; only the
    /// designated outcome writer emits regulation terminals.
    /// <see cref="BatteryId"/> follows the sole Spawn→Finish boundary rule.
    /// </summary>
    public class LogigramEvidence
    {
        /// <summary>
        /// Suggested evidence stream name stub for future unit wiring (not the Outcome stream).
        /// </summary>
        public const string StreamName = "Logigram1.Evidence";

        /// <summary>
        /// Gets or sets a path or node identifier observed by a Logic unit.
        /// </summary>
        public string NodeOrPathId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the canonical Battery identity from <c>BatterySpawn.BatteryId</c> (AD-6).
        /// </summary>
        public int BatteryId { get; set; }

        /// <summary>
        /// Gets or sets the participant identity for this Evidence (1 or 2). Default 0 means unset.
        /// </summary>
        public int PersonId { get; set; }
    }
}
