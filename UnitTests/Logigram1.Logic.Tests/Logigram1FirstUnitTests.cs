// <copyright file="Logigram1FirstUnitTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1.Logic.Tests
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SAAC.Logigram1;
    using SAAC.Logigram1.Logic;

    /// <summary>
    /// Tests for the <see cref="Logigram1FirstUnit"/> interim stub (nodes 1–2).
    /// </summary>
    [TestClass]
    public class Logigram1FirstUnitTests
    {
        /// <summary>Module status maps to node 1; door/select/validation map to node 2.</summary>
        [TestMethod]
        public void NodeLabelForTopic_MapsAd7TopicsToNodes1And2()
        {
            Assert.AreEqual("1", Logigram1FirstUnit.NodeLabelForTopic("Module status"));
            Assert.AreEqual("2", Logigram1FirstUnit.NodeLabelForTopic("Porte1 ouverture"));
            Assert.AreEqual("2", Logigram1FirstUnit.NodeLabelForTopic("M1-SelectModule"));
            Assert.AreEqual("2", Logigram1FirstUnit.NodeLabelForTopic("M1-Validation"));
        }

        /// <summary>
        /// Topic hits inside a Battery scope emit Battery-keyed State/Evidence with interim-stub labels.
        /// </summary>
        [TestMethod]
        public void FirstUnit_TopicHitInBattery_EmitsLabeledStateAndEvidence()
        {
            var states = new List<LogigramState>();
            var evidence = new List<LogigramEvidence>();
            DateTime start = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (7, start, start.AddSeconds(10)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_TopicHitInBattery_EmitsLabeledStateAndEvidence)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                unit.StateOut.Do(state => states.Add(CloneState(state)));
                unit.EvidenceOut.Do(ev => evidence.Add(CloneEvidence(ev)));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(3, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.TopicHitCount);
                Assert.AreEqual(1, unit.StateEmitCount);
                Assert.AreEqual(1, unit.EvidenceEmitCount);
            }

            Assert.AreEqual(1, states.Count);
            Assert.AreEqual(7, states[0].BatteryId);
            StringAssert.Contains(states[0].Status, "interim-stub");
            StringAssert.Contains(states[0].Status, "node-1");
            StringAssert.Contains(states[0].Status, "Module status");

            Assert.AreEqual(1, evidence.Count);
            Assert.AreEqual(7, evidence[0].BatteryId);
            StringAssert.Contains(evidence[0].NodeOrPathId, "node-1");
        }

        /// <summary>
        /// Evidence piped to the writer does not invent Alpha/Beta/Gamma.
        /// </summary>
        [TestMethod]
        public void FirstUnit_PipedToWriter_DoesNotFabricateOutcome()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(5)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_PipedToWriter_DoesNotFabricateOutcome)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);
                writer.Out.Do(outcome => outcomes.Add(new LogigramOutcome
                {
                    Regulation = outcome.Regulation,
                    BatteryId = outcome.BatteryId,
                }));

                Generators.Sequence(
                        pipeline,
                        new List<(bool, DateTime)>
                        {
                            (true, start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<bool>("M1-Validation"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.IsTrue(writer.EvidenceMessageCount >= 1);
                Assert.AreEqual(0, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(0, outcomes.Count);
        }

        /// <summary>Hits outside all Battery scopes stamp BatteryId 0 (unscoped).</summary>
        [TestMethod]
        public void FirstUnit_TopicHitOutsideBattery_UsesUnscopedBatteryIdZero()
        {
            var states = new List<LogigramState>();
            DateTime start = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (3, start, start.AddSeconds(1)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_TopicHitOutsideBattery_UsesUnscopedBatteryIdZero)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                unit.StateOut.Do(state => states.Add(CloneState(state)));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            ("12V", start.AddSeconds(30)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M2-SelectModule"));

                pipeline.Run(ReplayDescriptor.ReplayAll);
            }

            Assert.AreEqual(1, states.Count);
            Assert.AreEqual(0, states[0].BatteryId);
            StringAssert.Contains(states[0].Status, "node-2");
        }

        /// <summary>Unit id constant matches Analysis registry.</summary>
        [TestMethod]
        public void FirstUnit_UnitId_MatchesRegistryConstant()
        {
            Assert.AreEqual("Logigram1.FirstUnit", Logigram1FirstUnit.UnitId);
        }

        private static LogigramState CloneState(LogigramState state)
        {
            return new LogigramState
            {
                Status = state.Status,
                BatteryId = state.BatteryId,
            };
        }

        private static LogigramEvidence CloneEvidence(LogigramEvidence evidence)
        {
            return new LogigramEvidence
            {
                NodeOrPathId = evidence.NodeOrPathId,
                BatteryId = evidence.BatteryId,
            };
        }
    }
}
