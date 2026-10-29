// <copyright file="GammaPathTopicOrderingTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Logigram1Analysis;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SAAC.Logigram1.Logic;

    /// <summary>
    /// Story 3.6: importer deliveries interleave streams out of originating-time order (Casper5); the runner
    /// must hand the cycle FSM one globally ordered sequence.
    /// </summary>
    [TestClass]
    public class GammaPathTopicOrderingTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 3, 20, 15, 51, 0, DateTimeKind.Utc);

        /// <summary>Casper5 shape: Area leave (OT 15:51:30) arrives after Module status (OT 15:52:17).</summary>
        [TestMethod]
        public void Order_LateArrivingEarlierLeave_ComesBeforeModuleStatus()
        {
            var arrivals = new List<(GammaPathTopicEvent, DateTime)>
            {
                (Event("Module status"), T0.AddSeconds(77)),
                (Event("Area1"), T0.AddSeconds(30)),
                (Event("Porte1 ouverture"), T0.AddSeconds(100)),
                (Event("M1-SelectModule"), T0.AddSeconds(50)),
            };

            IReadOnlyList<(GammaPathTopicEvent Event, DateTime OriginatingTime)> ordered =
                GammaPathTopicOrdering.Order(arrivals);

            CollectionAssert.AreEqual(
                new[] { "Area1", "M1-SelectModule", "Module status", "Porte1 ouverture" },
                ordered.Select(o => o.Event.TopicName).ToArray());
            Assert.AreEqual(T0.AddSeconds(30), ordered[0].OriginatingTime);
        }

        /// <summary>Times are strictly increasing; same-OT hits are tie-ranked (state, node 1, Select, leave).</summary>
        [TestMethod]
        public void Order_SameOriginatingTime_TieRankedAndStrictlyIncreasing()
        {
            DateTime t = T0.AddSeconds(5);
            var arrivals = new List<(GammaPathTopicEvent, DateTime)>
            {
                (Event("Area1"), t),
                (Event("M1-Validation"), t),
                (Event("Module status"), t),
                (Event("Porte1 ouverture"), t),
            };

            IReadOnlyList<(GammaPathTopicEvent Event, DateTime OriginatingTime)> ordered =
                GammaPathTopicOrdering.Order(arrivals);

            CollectionAssert.AreEqual(
                new[] { "Porte1 ouverture", "Module status", "M1-Validation", "Area1" },
                ordered.Select(o => o.Event.TopicName).ToArray());
            for (int i = 1; i < ordered.Count; i++)
            {
                Assert.IsTrue(ordered[i].OriginatingTime > ordered[i - 1].OriginatingTime);
            }

            Assert.AreEqual(t, ordered[0].OriginatingTime);
            Assert.IsTrue(ordered[3].OriginatingTime - t <= TimeSpan.FromTicks(3));
        }

        /// <summary>One topic keeps its arrival order (stable) when originating times tie.</summary>
        [TestMethod]
        public void Order_SameTopicTies_KeepArrivalOrder()
        {
            var first = new GammaPathTopicEvent("Area1", 1);
            var second = new GammaPathTopicEvent("Area1", 2);
            IReadOnlyList<(GammaPathTopicEvent Event, DateTime OriginatingTime)> ordered =
                GammaPathTopicOrdering.Order(new List<(GammaPathTopicEvent, DateTime)>
                {
                    (first, T0),
                    (second, T0),
                });

            Assert.AreSame(first, ordered[0].Event);
            Assert.AreSame(second, ordered[1].Event);
        }

        /// <summary>Empty input yields an empty sequence; null is rejected.</summary>
        [TestMethod]
        public void Order_EmptyAndNull()
        {
            Assert.AreEqual(0, GammaPathTopicOrdering.Order(new List<(GammaPathTopicEvent, DateTime)>()).Count);
            Assert.ThrowsException<ArgumentNullException>(() => GammaPathTopicOrdering.Order(null));
        }

        private static GammaPathTopicEvent Event(string topic)
        {
            return new GammaPathTopicEvent(topic, new object());
        }
    }
}
