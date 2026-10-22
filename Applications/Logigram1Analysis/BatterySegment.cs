// <copyright file="BatterySegment.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;

    /// <summary>
    /// One closed Battery window from the Spawn→Finish boundary rule (AD-6).
    /// </summary>
    public sealed class BatterySegment
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BatterySegment"/> class.
        /// </summary>
        /// <param name="batteryId">Canonical id from <c>BatterySpawn.BatteryId</c>.</param>
        /// <param name="startOriginatingTime">Spawn originating time.</param>
        /// <param name="endOriginatingTime">Paired Finish originating time.</param>
        public BatterySegment(int batteryId, DateTime startOriginatingTime, DateTime endOriginatingTime)
        {
            this.BatteryId = batteryId;
            this.StartOriginatingTime = startOriginatingTime;
            this.EndOriginatingTime = endOriginatingTime;
        }

        /// <summary>Gets the canonical Battery id (from Spawn only).</summary>
        public int BatteryId { get; }

        /// <summary>Gets the segment start (BatterySpawn originating time).</summary>
        public DateTime StartOriginatingTime { get; }

        /// <summary>Gets the segment end (paired BatteryFinish originating time).</summary>
        public DateTime EndOriginatingTime { get; }
    }
}
