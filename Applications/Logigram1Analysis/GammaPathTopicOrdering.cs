// <copyright file="GammaPathTopicOrdering.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using SAAC.Logigram1.Logic;

    /// <summary>
    /// Merges the per-topic importer deliveries of the Gamma-path topic union into one globally
    /// originating-time-ordered sequence.
    /// </summary>
    /// <remarks>
    /// Psi importers replay each store stream in file order and make no promise about interleaving across
    /// streams: on Casper5 <c>Module status</c> (OT 15:52:17) was delivered before <c>Area1</c> (OT 15:51:30) and the
    /// whole session's Area/Select hits arrived long before earlier-OT hits of other topics. The cycle FSM is causal,
    /// so it must see events in originating-time order — otherwise a leave can complete a cycle whose node 1
    /// occurs later in time (orphan Gamma) and State originating times collapse onto the latest one seen.
    /// </remarks>
    public static class GammaPathTopicOrdering
    {
        /// <summary>
        /// Orders arrivals by originating time (ties: derived inputs, then node 1, then Select/Validation, then Area
        /// leaves, then arrival order) and makes the times strictly increasing (a tie costs one tick, i.e. 100 ns).
        /// </summary>
        /// <param name="arrivals">Topic hits in arbitrary arrival order with their store originating times.</param>
        /// <returns>The same hits, strictly increasing in originating time.</returns>
        public static IReadOnlyList<(GammaPathTopicEvent Event, DateTime OriginatingTime)> Order(
            IEnumerable<(GammaPathTopicEvent Event, DateTime OriginatingTime)> arrivals)
        {
            if (arrivals == null)
            {
                throw new ArgumentNullException(nameof(arrivals));
            }

            var ordered = arrivals
                .Select((arrival, index) => (arrival.Event, arrival.OriginatingTime, Index: index))
                .OrderBy(item => item.OriginatingTime)
                .ThenBy(item => TieRank(item.Event.TopicName))
                .ThenBy(item => item.Index)
                .ToList();

            var result = new List<(GammaPathTopicEvent, DateTime)>(ordered.Count);
            DateTime last = DateTime.MinValue;
            foreach (var item in ordered)
            {
                DateTime time = item.OriginatingTime;
                if (time <= last)
                {
                    time = last.AddTicks(1);
                }

                last = time;
                result.Add((item.Event, time));
            }

            return result;
        }

        private static int TieRank(string topicName)
        {
            switch (topicName)
            {
                case "Module status":
                    return 1;

                case "M1-SelectModule":
                case "M2-SelectModule":
                case "M1-Validation":
                case "M2-Validation":
                    return 2;

                case "Area1":
                case "Area2":
                    return 3;

                default:
                    // Doors, ObjectBound, wrists: latest-known state, applied first.
                    return 0;
            }
        }
    }
}
