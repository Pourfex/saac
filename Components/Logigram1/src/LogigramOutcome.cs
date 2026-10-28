// <copyright file="LogigramOutcome.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1
{
    /// <summary>
    /// Battery-scoped Logigram 1 terminal / regulation outcome message (OUTCOME_SHAPE B).
    /// Only the designated outcome writer may produce the <see cref="StreamName"/> stream (AD-10).
    /// <see cref="BatteryId"/> follows the sole Spawn→Finish boundary rule (AD-6; replaces AD-12).
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

        /// <summary>
        /// Gets or sets the canonical Battery identity from <c>BatterySpawn.BatteryId</c> when a Battery scope is supplied (AD-6).
        /// </summary>
        public int BatteryId { get; set; }

        /// <summary>
        /// Gets or sets the participant identity for this Outcome (1 or 2). Default 0 means unset.
        /// </summary>
        public int PersonId { get; set; }

        /// <summary>
        /// Returns a PsiStudio-friendly label (Messages legend / Outcome viz shell).
        /// Does not invent a regulation when <see cref="Regulation"/> is null.
        /// </summary>
        /// <returns>Regulation contract label + BatteryId + PersonId, or an honest absent label.</returns>
        public override string ToString()
        {
            string regulation = this.Regulation.HasValue
                ? LogigramRegulationLabels.ToContractLabel(this.Regulation.Value)
                : "absent";
            return $"{regulation} BatteryId={this.BatteryId} PersonId={this.PersonId}";
        }
    }
}
