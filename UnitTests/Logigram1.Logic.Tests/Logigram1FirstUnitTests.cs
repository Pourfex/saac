// <copyright file="Logigram1FirstUnitTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1.Logic.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using Microsoft.Psi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SAAC.Logigram1;
    using SAAC.Logigram1.Logic;

    /// <summary>
    /// Tests for <see cref="Logigram1FirstUnit"/> node 1–2 derivation and Gamma on node-2.
    /// </summary>
    /// <remarks>
    /// Psi FullSpeed pipelines with overlapping virtual clocks are not safe under MSTest
    /// method parallelization — keep this class sequential.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class Logigram1FirstUnitTests
    {
        private static int startSequence;

        /// <summary>
        /// Distinct virtual timeline per test so sequential Psi FullSpeed runs do not share clocks.
        /// </summary>
        private static DateTime UniqueStart()
        {
            int n = System.Threading.Interlocked.Increment(ref startSequence);
            return new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc).AddHours(n);
        }

        /// <summary>Module status maps to node 1; door/select/validation map to node 2.</summary>
        [TestMethod]
        public void NodeLabelForTopic_MapsAd7TopicsToNodes1And2()
        {
            Assert.AreEqual("1", Logigram1FirstUnit.NodeLabelForTopic("Module status"));
            Assert.AreEqual("2", Logigram1FirstUnit.NodeLabelForTopic("Porte1 ouverture"));
            Assert.AreEqual("2", Logigram1FirstUnit.NodeLabelForTopic("M1-SelectModule"));
            Assert.AreEqual("2", Logigram1FirstUnit.NodeLabelForTopic("M1-Validation"));
        }

        /// <summary>New module id this session → node-1 State/Evidence with BatteryId.</summary>
        [TestMethod]
        public void FirstUnit_NewModuleId_FiresNode1WithBatteryScope()
        {
            var states = new List<LogigramState>();
            var evidence = new List<LogigramEvidence>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (7, start, start.AddSeconds(10)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_NewModuleId_FiresNode1WithBatteryScope)))
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

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }

            Assert.AreEqual(1, states.Count);
            Assert.AreEqual(7, states[0].BatteryId);
            StringAssert.Contains(states[0].Status, "node-1");
            StringAssert.Contains(states[0].Status, "id=3");
            Assert.AreEqual(1, evidence.Count);
            StringAssert.Contains(evidence[0].NodeOrPathId, "node-1");
        }

        /// <summary>Same module id again on the same Battery does not re-fire node 1.</summary>
        [TestMethod]
        public void FirstUnit_RepeatModuleId_SameBattery_DoesNotRefireNode1()
        {
            var states = new List<LogigramState>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(5)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_RepeatModuleId_SameBattery_DoesNotRefireNode1)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                unit.StateOut.Do(state => states.Add(CloneState(state)));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(9, "OK"), start.AddSeconds(1)),
                            (new ValueTuple<int, string>(9, "OK"), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
            }

            Assert.AreEqual(1, states.Count);
            Assert.AreEqual(1, states[0].BatteryId);
        }

        /// <summary>Same module id on a later Battery still fires node 1 (per-Battery process).</summary>
        [TestMethod]
        public void FirstUnit_SameModuleId_OtherBattery_FiresNode1Again()
        {
            var states = new List<LogigramState>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(5)),
                (2, start.AddSeconds(10), start.AddSeconds(15)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_SameModuleId_OtherBattery_FiresNode1Again)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                unit.StateOut.Do(state => states.Add(CloneState(state)));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(9, "OK"), start.AddSeconds(1)),
                            (new ValueTuple<int, string>(9, "OK"), start.AddSeconds(11)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(2, unit.Node1FireCount);
            }

            Assert.AreEqual(2, states.Count);
            Assert.AreEqual(1, states[0].BatteryId);
            Assert.AreEqual(2, states[1].BatteryId);
        }

        /// <summary>Battery B door+select without Battery B node 1 must not emit Gamma (even if A had node 1).</summary>
        [TestMethod]
        public void FirstUnit_Node2OnBatteryWithoutItsOwnNode1_NoGamma()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(8)),
                (2, start.AddSeconds(10), start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_Node2OnBatteryWithoutItsOwnNode1_NoGamma)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.RegulationDecisionWithBatteryOut.PipeTo(writer.RegulationDecisionWithBatteryIn);
                writer.Out.Do(o => outcomes.Add(new LogigramOutcome
                {
                    Regulation = o.Regulation,
                    BatteryId = o.BatteryId,
                }));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(3, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, default), start.AddSeconds(12)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            ("6V", start.AddSeconds(12.5)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }

            Assert.AreEqual(0, outcomes.Count);
        }

        /// <summary>Door close + Select within 2s after node 1 → node 2 + Gamma via writer.</summary>
        [TestMethod]
        public void FirstUnit_Node2WithinWindowAfterNode1_FiresGammaDecision()
        {
            var states = new List<LogigramState>();
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (5, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_Node2WithinWindowAfterNode1_FiresGammaDecision)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);
                unit.RegulationDecisionWithBatteryOut.PipeTo(writer.RegulationDecisionWithBatteryIn);
                unit.StateOut.Do(state => states.Add(CloneState(state)));
                writer.Out.Do(o => outcomes.Add(new LogigramOutcome
                {
                    Regulation = o.Regulation,
                    BatteryId = o.BatteryId,
                }));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(1, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            ("12V", start.AddSeconds(3)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
                Assert.AreEqual(1, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(2, states.Count);
            StringAssert.Contains(states[0].Status, "node-1");
            StringAssert.Contains(states[1].Status, "node-2");
            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(LogigramRegulation.Gamma, outcomes[0].Regulation);
            Assert.AreEqual(5, outcomes[0].BatteryId);
        }

        /// <summary>Validation true within window after node 1 also fires node 2 + Gamma.</summary>
        [TestMethod]
        public void FirstUnit_ValidationWithinWindowAfterNode1_FiresNode2()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_ValidationWithinWindowAfterNode1_FiresNode2)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(2, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte2 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(bool, DateTime)>
                        {
                            (true, start.AddSeconds(3.5)),
                        })
                    .PipeTo(unit.AddTopicInput<bool>("M2-Validation"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>Door+button timing ok but node 1 never fired → no node 2 / no Gamma.</summary>
        [TestMethod]
        public void FirstUnit_Node2WithoutNode1_NoFireNoGamma()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_Node2WithoutNode1_NoFireNoGamma)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);
                unit.RegulationDecisionWithBatteryOut.PipeTo(writer.RegulationDecisionWithBatteryIn);
                writer.Out.Do(o => outcomes.Add(new LogigramOutcome
                {
                    Regulation = o.Regulation,
                    BatteryId = o.BatteryId,
                }));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            ("6V", start.AddSeconds(1.5)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(0, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
                Assert.AreEqual(0, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(0, outcomes.Count);
        }

        /// <summary>Select/Validation more than 2s after door-close arm → no node 2.</summary>
        [TestMethod]
        public void FirstUnit_Node2Late_NoFire()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(30)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_Node2Late_NoFire)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(1, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            ("6V", start.AddSeconds(5)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>Door open does not arm; select after open does not fire node 2.</summary>
        [TestMethod]
        public void FirstUnit_DoorOpen_DoesNotArmNode2()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_DoorOpen_DoesNotArmNode2)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(1, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(true, Vector3.Zero), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            ("6V", start.AddSeconds(2.5)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
            }
        }

        /// <summary>Door open clears a prior arm; select without a new close does not fire node 2.</summary>
        [TestMethod]
        public void FirstUnit_DoorOpen_ClearsArm_SelectWithoutNewClose_NoFire()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_DoorOpen_ClearsArm_SelectWithoutNewClose_NoFire)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "clear-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "clear-door");
                var select = pipeline.CreateEmitter<string>(unit, "clear-select");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                select.PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                // One driver stream forces close → open → select order (no multi-source reorder).
                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(2)),
                            (2, start.AddSeconds(2.5)),
                            (3, start.AddSeconds(3)),
                        })
                    .Do((step, envelope) =>
                    {
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), envelope.OriginatingTime);
                        }
                        else if (step == 1)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), envelope.OriginatingTime);
                        }
                        else if (step == 2)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(true, Vector3.Zero), envelope.OriginatingTime);
                        }
                        else
                        {
                            select.Post("6V", envelope.OriginatingTime);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>Select before door-close is held pending; door arm flushes → node 2 + Gamma.</summary>
        [TestMethod]
        public void FirstUnit_SelectBeforeDoorClose_ThenArm_FiresNode2AndGamma()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_SelectBeforeDoorClose_ThenArm_FiresNode2AndGamma)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "pend-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "pend-door");
                var select = pipeline.CreateEmitter<string>(unit, "pend-select");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                select.PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                // Same callback + same OT: Select handled before door-close, then flush fires.
                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(2)),
                        })
                    .Do((step, envelope) =>
                    {
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), envelope.OriginatingTime);
                        }
                        else
                        {
                            DateTime t = envelope.OriginatingTime;
                            select.Post("12V", t);
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>Empty and whitespace SelectModule do not fire node 2.</summary>
        [TestMethod]
        public void FirstUnit_EmptyOrWhitespaceSelect_DoesNotFireNode2()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_EmptyOrWhitespaceSelect_DoesNotFireNode2)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(1, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            (string.Empty, start.AddSeconds(2.5)),
                            ("   ", start.AddSeconds(2.7)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>Select then Validation in the same armed window → exactly one node-2 / Gamma.</summary>
        [TestMethod]
        public void FirstUnit_SelectThenValidationInSameWindow_FiresNode2Once()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_SelectThenValidationInSameWindow_FiresNode2Once)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(1, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            ("6V", start.AddSeconds(2.5)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                Generators.Sequence(
                        pipeline,
                        new List<(bool, DateTime)>
                        {
                            (true, start.AddSeconds(2.8)),
                        })
                    .PipeTo(unit.AddTopicInput<bool>("M1-Validation"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>Select before node 1 is buffered; node 1 then door-close can still fire node 2.</summary>
        [TestMethod]
        public void FirstUnit_SelectBeforeNode1_ThenNode1AndDoor_CanFireNode2()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_SelectBeforeNode1_ThenNode1AndDoor_CanFireNode2)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "pre-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "pre-door");
                var select = pipeline.CreateEmitter<string>(unit, "pre-select");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                select.PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                // Same callback + same OT: Select before node 1, then node 1 flush, then door flush.
                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(2)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        select.Post("12V", t);
                        module.Post(new ValueTuple<int, string>(1, "OK"), t);
                        door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>Validation false in window does not fire node 2.</summary>
        [TestMethod]
        public void FirstUnit_ValidationFalse_DoesNotFireNode2()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_ValidationFalse_DoesNotFireNode2)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(1, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(bool, DateTime)>
                        {
                            (false, start.AddSeconds(2.5)),
                        })
                    .PipeTo(unit.AddTopicInput<bool>("M1-Validation"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
            }
        }

        /// <summary>
        /// Evidence piped to the writer does not invent Outcome (regulation pipe is the sole path).
        /// </summary>
        [TestMethod]
        public void FirstUnit_EvidenceOnly_DoesNotFabricateOutcome()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.FirstUnit_EvidenceOnly_DoesNotFabricateOutcome)))
            {
                var unit = new Logigram1FirstUnit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);

                // Node-2 may still emit Gamma on RegulationDecisionWithBatteryOut; drain it so Post
                // succeeds, but do not pipe to the writer.
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });
                writer.Out.Do(outcome => outcomes.Add(new LogigramOutcome
                {
                    Regulation = outcome.Regulation,
                    BatteryId = outcome.BatteryId,
                }));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(1, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.IsTrue(writer.EvidenceMessageCount >= 1);
                Assert.AreEqual(0, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(0, outcomes.Count);
        }

        /// <summary>Hits outside all Battery scopes stamp BatteryId 0 (unscoped) on node-1.</summary>
        [TestMethod]
        public void FirstUnit_TopicHitOutsideBattery_UsesUnscopedBatteryIdZero()
        {
            var states = new List<LogigramState>();
            DateTime start = UniqueStart();
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
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(4, "OK"), start.AddSeconds(30)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                pipeline.Run(ReplayDescriptor.ReplayAll);
            }

            Assert.AreEqual(1, states.Count);
            Assert.AreEqual(0, states[0].BatteryId);
            StringAssert.Contains(states[0].Status, "node-1");
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
