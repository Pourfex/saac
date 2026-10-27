// <copyright file="Logigram1GammaPathUnitTests.cs" company="SAAC">
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
    /// I/O matrix for <see cref="Logigram1GammaPathUnit"/> cycle FSM (nodes 1→2→3→C).
    /// </summary>
    /// <remarks>
    /// Psi FullSpeed pipelines with overlapping virtual clocks are not safe under MSTest
    /// method parallelization — keep this class sequential.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class Logigram1GammaPathUnitTests
    {
        private static int startSequence;

        private static DateTime UniqueStart()
        {
            int n = System.Threading.Interlocked.Increment(ref startSequence);
            return new DateTime(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc).AddHours(n);
        }

        [TestMethod]
        public void GammaPath_UnitId_IsLogigram1GammaPath()
        {
            Assert.AreEqual("Logigram1.GammaPath", Logigram1GammaPathUnit.UnitId);
        }

        [TestMethod]
        public void NodeLabelForTopic_MapsAd7TopicsToNodes()
        {
            Assert.AreEqual("1", Logigram1GammaPathUnit.NodeLabelForTopic("Module status"));
            Assert.AreEqual("2", Logigram1GammaPathUnit.NodeLabelForTopic("Porte1 ouverture"));
            Assert.AreEqual("2", Logigram1GammaPathUnit.NodeLabelForTopic("M1-SelectModule"));
            Assert.AreEqual("3", Logigram1GammaPathUnit.NodeLabelForTopic("Area1"));
            Assert.AreEqual("3", Logigram1GammaPathUnit.NodeLabelForTopic("Object Bound"));
            Assert.AreEqual("3", Logigram1GammaPathUnit.NodeLabelForTopic("1-LeftWrist"));
        }

        [TestMethod]
        public void DistancePointToAabb_InsideAndOutside()
        {
            var a = new Vector3(0, 0, 0);
            var b = new Vector3(2, 2, 2);
            Assert.AreEqual(0f, Logigram1GammaPathUnit.DistancePointToAabb(new Vector3(1, 1, 1), a, b));
            Assert.AreEqual(1f, Logigram1GammaPathUnit.DistancePointToAabb(new Vector3(3, 1, 1), a, b), 1e-5f);
        }

        [TestMethod]
        public void Node1Only_OpensWindow_NoGamma()
        {
            var states = new List<LogigramState>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (7, start, start.AddSeconds(10)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node1Only_OpensWindow_NoGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.StateOut.Do(state => states.Add(CloneState(state)));
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

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
                Assert.AreEqual(0, unit.Node3FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }

            Assert.AreEqual(1, states.Count);
            Assert.AreEqual(7, states[0].BatteryId);
            StringAssert.Contains(states[0].Status, "node-1");
        }

        [TestMethod]
        public void Node2_DoorClosedAndSelectWithin2sOfCreate_ResetsWithoutGamma()
        {
            var states = new List<LogigramState>();
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (5, start, start.AddSeconds(20)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node2_DoorClosedAndSelectWithin2sOfCreate_ResetsWithoutGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
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
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(1.5)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            ("12V", start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(0, unit.Node3FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
                Assert.AreEqual(0, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(2, states.Count);
            StringAssert.Contains(states[0].Status, "node-1");
            StringAssert.Contains(states[1].Status, "node-2");
            Assert.AreEqual(0, outcomes.Count);
        }

        [TestMethod]
        public void LeaveDuring2sWindow_NoNode3NoGamma()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(30)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.LeaveDuring2sWindow_NoNode3NoGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "win-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "win-door");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "win-area");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(1.2)),
                            (2, start.AddSeconds(1.5)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), t);
                        }
                        else if (step == 1)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                        else
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node3FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        [TestMethod]
        public void PostWindow_DoorClosedLeave_FiresNode3AndGamma()
        {
            var states = new List<LogigramState>();
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (9, start, start.AddSeconds(30)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.PostWindow_DoorClosedLeave_FiresNode3AndGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);
                unit.RegulationDecisionWithBatteryOut.PipeTo(writer.RegulationDecisionWithBatteryIn);
                unit.StateOut.Do(state => states.Add(CloneState(state)));
                writer.Out.Do(o => outcomes.Add(new LogigramOutcome
                {
                    Regulation = o.Regulation,
                    BatteryId = o.BatteryId,
                }));

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "n3-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "n3-door");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "n3-area");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(1.2)),
                            (2, start.AddSeconds(3.5)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), t);
                        }
                        else if (step == 1)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                        else
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
                Assert.AreEqual(1, unit.Node3FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
                Assert.AreEqual(1, writer.OutcomeEmitCount);
            }

            Assert.IsTrue(states.Exists(s => s.Status != null && s.Status.Contains("node-3:")));
            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(LogigramRegulation.Gamma, outcomes[0].Regulation);
            Assert.AreEqual(9, outcomes[0].BatteryId);
        }

        [TestMethod]
        public void PostWindow_HandNearLeave_FiresGamma()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (3, start, start.AddSeconds(30)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.PostWindow_HandNearLeave_FiresGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "hand-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "hand-door");
                var bound = pipeline.CreateEmitter<ValueTuple<string, Vector3, Vector3>>(unit, "hand-bound");
                var wrist = pipeline.CreateEmitter<Tuple<Vector3, Vector3>>(unit, "hand-wrist");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "hand-area");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                bound.PipeTo(unit.AddTopicInput<ValueTuple<string, Vector3, Vector3>>("Object Bound"));
                wrist.PipeTo(unit.AddTopicInput<Tuple<Vector3, Vector3>>("1-LeftWrist"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(1.1)),
                            (2, start.AddSeconds(1.2)),
                            (3, start.AddSeconds(1.3)),
                            (4, start.AddSeconds(3.5)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), t);
                        }
                        else if (step == 1)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(true, Vector3.Zero), t);
                        }
                        else if (step == 2)
                        {
                            bound.Post(
                                new ValueTuple<string, Vector3, Vector3>(
                                    "Generateur1",
                                    new Vector3(0, 0, 0),
                                    new Vector3(1, 1, 1)),
                                t);
                        }
                        else if (step == 3)
                        {
                            wrist.Post(Tuple.Create(new Vector3(0.5f, 0.5f, 0.5f), Vector3.Zero), t);
                        }
                        else
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node3FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
            }
        }

        [TestMethod]
        public void Neither2Nor3_NoGamma()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(20)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Neither2Nor3_NoGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, string>, DateTime)>
                        {
                            (new ValueTuple<int, string>(1, "OK"), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));

                // Door closed but no Select in window and no leave — honest empty.
                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(1.5)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
                Assert.AreEqual(0, unit.Node3FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        [TestMethod]
        public void LeaveWithoutNode1_NoGamma()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(20)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.LeaveWithoutNode1_NoGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, bool, string>, DateTime)>
                        {
                            (new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(0, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node3FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        [TestMethod]
        public void Node2ThenLeave_NoGamma()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(30)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node2ThenLeave_NoGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "r-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "r-door");
                var select = pipeline.CreateEmitter<string>(unit, "r-select");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "r-area");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                select.PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(1.2)),
                            (2, start.AddSeconds(1.5)),
                            (3, start.AddSeconds(4)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), t);
                        }
                        else if (step == 1)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                        else if (step == 2)
                        {
                            select.Post("6V", t);
                        }
                        else
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(0, unit.Node3FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        [TestMethod]
        public void MultiCycle_GammaThenNode2Reset()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (11, start, start.AddSeconds(60)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.MultiCycle_GammaThenNode2Reset)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);
                unit.RegulationDecisionWithBatteryOut.PipeTo(writer.RegulationDecisionWithBatteryIn);
                writer.Out.Do(o => outcomes.Add(new LogigramOutcome
                {
                    Regulation = o.Regulation,
                    BatteryId = o.BatteryId,
                }));

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "mc-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "mc-door");
                var select = pipeline.CreateEmitter<string>(unit, "mc-select");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "mc-area");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                select.PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                // Cycle A: node1 → unused 2s → leave → Gamma.
                // Cycle B: node1 → door+Select in 2s → reset, no Gamma.
                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(1.2)),
                            (2, start.AddSeconds(3.5)),
                            (3, start.AddSeconds(10)),
                            (4, start.AddSeconds(10.2)),
                            (5, start.AddSeconds(10.5)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), t);
                        }
                        else if (step == 1)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                        else if (step == 2)
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                        else if (step == 3)
                        {
                            module.Post(new ValueTuple<int, string>(2, "OK"), t);
                        }
                        else if (step == 4)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                        else
                        {
                            select.Post("12V", t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(2, unit.Node1FireCount);
                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(1, unit.Node3FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
            }

            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(11, outcomes[0].BatteryId);
        }

        [TestMethod]
        public void SelectWithoutDoorClosedInWindow_NoNode2()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(20)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.SelectWithoutDoorClosedInWindow_NoNode2)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);

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
                            (new ValueTuple<bool, Vector3>(true, Vector3.Zero), start.AddSeconds(1.2)),
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

                Assert.AreEqual(1, unit.Node1FireCount);
                Assert.AreEqual(0, unit.Node2FireCount);
            }
        }

        [TestMethod]
        public void ValidationWithinWindowWithDoorClosed_FiresNode2NoGamma()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(20)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.ValidationWithinWindowWithDoorClosed_FiresNode2NoGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

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
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(1.2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte2 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(bool, DateTime)>
                        {
                            (true, start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<bool>("M2-Validation"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        [TestMethod]
        public void EvidenceOnly_DoesNotFabricateOutcome()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(20)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.EvidenceOnly_DoesNotFabricateOutcome)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);
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

                Assert.IsTrue(writer.EvidenceMessageCount >= 1);
                Assert.AreEqual(0, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(0, outcomes.Count);
        }

        [TestMethod]
        public void RepeatModuleId_SameBattery_DoesNotRefireNode1()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(5)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.RepeatModuleId_SameBattery_DoesNotRefireNode1)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);

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
        }

        [TestMethod]
        public void Area2PairsWithPorte2_NotPorte1()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(30)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Area2PairsWithPorte2_NotPorte1)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "pair-module");
                var door1 = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "pair-door1");
                var door2 = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "pair-door2");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "pair-area");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door1.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                door2.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte2 ouverture"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area2"));

                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(1.1)),
                            (2, start.AddSeconds(1.2)),
                            (3, start.AddSeconds(3.5)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), t);
                        }
                        else if (step == 1)
                        {
                            door1.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                        else if (step == 2)
                        {
                            door2.Post(new ValueTuple<bool, Vector3>(true, Vector3.Zero), t);
                        }
                        else
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(0, unit.Node3FireCount);
            }
        }

        /// <summary>Select while door open is pending; paired door close inside 2s flushes node-2 without Gamma.</summary>
        [TestMethod]
        public void SelectWhileDoorOpen_ThenPairedDoorClosesInWindow_FiresNode2NoGamma()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(20)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.SelectWhileDoorOpen_ThenPairedDoorClosesInWindow_FiresNode2NoGamma)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "pend-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "pend-door");
                var select = pipeline.CreateEmitter<string>(unit, "pend-select");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                select.PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(1.1)),
                            (2, start.AddSeconds(1.3)),
                            (3, start.AddSeconds(1.6)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), t);
                        }
                        else if (step == 1)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(true, Vector3.Zero), t);
                        }
                        else if (step == 2)
                        {
                            select.Post("6V", t);
                        }
                        else
                        {
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>After node-3+Gamma, a second leave without a new module create must not re-fire.</summary>
        [TestMethod]
        public void AfterNode3Gamma_SecondLeaveWithoutNode1_DoesNotRefire()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(30)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.AfterNode3Gamma_SecondLeaveWithoutNode1_DoesNotRefire)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var module = pipeline.CreateEmitter<ValueTuple<int, string>>(unit, "r3-module");
                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "r3-door");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "r3-area");
                module.PipeTo(unit.AddTopicInput<ValueTuple<int, string>>("Module status"));
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(1.2)),
                            (2, start.AddSeconds(3.5)),
                            (3, start.AddSeconds(4.5)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            module.Post(new ValueTuple<int, string>(1, "OK"), t);
                        }
                        else if (step == 1)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                        else if (step == 2)
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                        else
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node3FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>Select then Validation in the same create window → exactly one node-2.</summary>
        [TestMethod]
        public void SelectThenValidationInSameWindow_FiresNode2Once()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(20)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.SelectThenValidationInSameWindow_FiresNode2Once)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, scopes);
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
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(1.2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(string, DateTime)>
                        {
                            ("6V", start.AddSeconds(1.5)),
                        })
                    .PipeTo(unit.AddTopicInput<string>("M1-SelectModule"));

                Generators.Sequence(
                        pipeline,
                        new List<(bool, DateTime)>
                        {
                            (true, start.AddSeconds(1.7)),
                        })
                    .PipeTo(unit.AddTopicInput<bool>("M1-Validation"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node2FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        private static LogigramState CloneState(LogigramState state)
        {
            return new LogigramState
            {
                Status = state.Status,
                BatteryId = state.BatteryId,
            };
        }
    }
}
