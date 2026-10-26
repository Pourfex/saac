// <copyright file="Logigram1Node3UnitTests.cs" company="SAAC">
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
    /// I/O matrix for <see cref="Logigram1Node3Unit"/> (door+leave, hand+leave, miss cases).
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class Logigram1Node3UnitTests
    {
        private static int startSequence;

        private static DateTime UniqueStart()
        {
            int n = System.Threading.Interlocked.Increment(ref startSequence);
            return new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc).AddHours(n);
        }

        /// <summary>Unit id matches Analysis registry constant.</summary>
        [TestMethod]
        public void Node3_UnitId_MatchesRegistryConstant()
        {
            Assert.AreEqual("Logigram1.Node3", Logigram1Node3Unit.UnitId);
        }

        /// <summary>Point inside AABB → distance 0; outside along axis → expected gap.</summary>
        [TestMethod]
        public void DistancePointToAabb_InsideAndOutside()
        {
            var a = new Vector3(0, 0, 0);
            var b = new Vector3(2, 2, 2);
            Assert.AreEqual(0f, Logigram1Node3Unit.DistancePointToAabb(new Vector3(1, 1, 1), a, b));
            Assert.AreEqual(1f, Logigram1Node3Unit.DistancePointToAabb(new Vector3(3, 1, 1), a, b), 1e-5f);
        }

        /// <summary>Door closed + player leave GeneratorArea → node-3 + Gamma.</summary>
        [TestMethod]
        public void Node3_DoorClosedAndLeave_FiresGamma()
        {
            var states = new List<LogigramState>();
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (9, start, start.AddSeconds(30)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node3_DoorClosedAndLeave_FiresGamma)))
            {
                var unit = new Logigram1Node3Unit(pipeline, scopes);
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

                Assert.AreEqual(1, unit.Node3FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
                Assert.AreEqual(1, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(1, states.Count);
            StringAssert.Contains(states[0].Status, "node-3:");
            Assert.AreEqual(9, states[0].BatteryId);
            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(LogigramRegulation.Gamma, outcomes[0].Regulation);
            Assert.AreEqual(9, outcomes[0].BatteryId);
        }

        /// <summary>Wrist within 1 m of Generateur AABB + leave → node-3 + Gamma (door may be open).</summary>
        [TestMethod]
        public void Node3_HandNearAndLeave_FiresGamma()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (3, start, start.AddSeconds(30)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node3_HandNearAndLeave_FiresGamma)))
            {
                var unit = new Logigram1Node3Unit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "hand-door");
                var bound = pipeline.CreateEmitter<ValueTuple<string, Vector3, Vector3>>(unit, "hand-bound");
                var wrist = pipeline.CreateEmitter<Tuple<Vector3, Vector3>>(unit, "hand-wrist");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "hand-area");
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                bound.PipeTo(unit.AddTopicInput<ValueTuple<string, Vector3, Vector3>>("Object Bound"));
                wrist.PipeTo(unit.AddTopicInput<Tuple<Vector3, Vector3>>("1-LeftWrist"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                // One driver forces door-open → AABB → wrist-near → leave order.
                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(2)),
                            (2, start.AddSeconds(3)),
                            (3, start.AddSeconds(4)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(true, Vector3.Zero), t);
                        }
                        else if (step == 1)
                        {
                            bound.Post(
                                new ValueTuple<string, Vector3, Vector3>(
                                    "Generateur1",
                                    new Vector3(0, 0, 0),
                                    new Vector3(1, 1, 1)),
                                t);
                        }
                        else if (step == 2)
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

        /// <summary>Leave while door open and wrists &gt;1 m from AABB → no node-3 / no Gamma.</summary>
        [TestMethod]
        public void Node3_LeaveWhileOpenAndFar_NoFire()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(30)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node3_LeaveWhileOpenAndFar_NoFire)))
            {
                var unit = new Logigram1Node3Unit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "far-door");
                var bound = pipeline.CreateEmitter<ValueTuple<string, Vector3, Vector3>>(unit, "far-bound");
                var wrist = pipeline.CreateEmitter<Tuple<Vector3, Vector3>>(unit, "far-wrist");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "far-area");
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));
                bound.PipeTo(unit.AddTopicInput<ValueTuple<string, Vector3, Vector3>>("Object Bound"));
                wrist.PipeTo(unit.AddTopicInput<Tuple<Vector3, Vector3>>("1-RightWrist"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(2)),
                            (2, start.AddSeconds(3)),
                            (3, start.AddSeconds(4)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(true, Vector3.Zero), t);
                        }
                        else if (step == 1)
                        {
                            bound.Post(
                                new ValueTuple<string, Vector3, Vector3>(
                                    "Generateur1",
                                    new Vector3(0, 0, 0),
                                    new Vector3(1, 1, 1)),
                                t);
                        }
                        else if (step == 2)
                        {
                            // 10 m away from AABB → not hand-near.
                            wrist.Post(Tuple.Create(new Vector3(10, 0, 0), Vector3.Zero), t);
                        }
                        else
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(0, unit.Node3FireCount);
                Assert.AreEqual(0, unit.GammaDecisionEmitCount);
            }
        }

        /// <summary>Non-GeneratorArea leave does not fire.</summary>
        [TestMethod]
        public void Node3_NonGeneratorLeave_NoFire()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node3_NonGeneratorLeave_NoFire)))
            {
                var unit = new Logigram1Node3Unit(pipeline, scopes);

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
                            (new ValueTuple<int, bool, string>(-1, false, "TVArea"), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(0, unit.Node3FireCount);
            }
        }

        /// <summary>Module exit (id ≥ 0) of GeneratorArea does not fire (player-only).</summary>
        [TestMethod]
        public void Node3_ModuleExit_NoFire()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node3_ModuleExit_NoFire)))
            {
                var unit = new Logigram1Node3Unit(pipeline, scopes);

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte2 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, bool, string>, DateTime)>
                        {
                            (new ValueTuple<int, bool, string>(4, false, "GeneratorArea"), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area2"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(0, unit.Node3FireCount);
            }
        }

        /// <summary>Enter-only GeneratorArea does not fire.</summary>
        [TestMethod]
        public void Node3_EnterOnly_NoFire()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node3_EnterOnly_NoFire)))
            {
                var unit = new Logigram1Node3Unit(pipeline, scopes);

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
                            (new ValueTuple<int, bool, string>(-1, true, "GeneratorArea"), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area1"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(0, unit.Node3FireCount);
            }
        }

        /// <summary>Porte2 closed + Area2 player leave GeneratorArea → node-3 + Gamma.</summary>
        [TestMethod]
        public void Node3_Area2DoorClosedAndLeave_FiresGamma()
        {
            var states = new List<LogigramState>();
            var outcomes = new List<LogigramOutcome>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (4, start, start.AddSeconds(30)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node3_Area2DoorClosedAndLeave_FiresGamma)))
            {
                var unit = new Logigram1Node3Unit(pipeline, scopes);
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);
                unit.RegulationDecisionWithBatteryOut.PipeTo(writer.RegulationDecisionWithBatteryIn);
                unit.StateOut.Do(state => states.Add(CloneState(state)));
                writer.Out.Do(o => outcomes.Add(new LogigramOutcome
                {
                    Regulation = o.Regulation,
                    BatteryId = o.BatteryId,
                }));

                var door = pipeline.CreateEmitter<ValueTuple<bool, Vector3>>(unit, "a2-door");
                var area = pipeline.CreateEmitter<ValueTuple<int, bool, string>>(unit, "a2-area");
                door.PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte2 ouverture"));
                area.PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area2"));

                // One driver forces Porte2 closed → Area2 leave (mirrors Area1 door+leave happy path).
                Generators.Sequence(
                        pipeline,
                        new List<(int, DateTime)>
                        {
                            (0, start.AddSeconds(1)),
                            (1, start.AddSeconds(2)),
                        })
                    .Do((step, envelope) =>
                    {
                        DateTime t = envelope.OriginatingTime;
                        if (step == 0)
                        {
                            door.Post(new ValueTuple<bool, Vector3>(false, Vector3.Zero), t);
                        }
                        else
                        {
                            area.Post(new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), t);
                        }
                    });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, unit.Node3FireCount);
                Assert.AreEqual(1, unit.GammaDecisionEmitCount);
                Assert.AreEqual(1, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(1, states.Count);
            StringAssert.Contains(states[0].Status, "node-3:");
            StringAssert.Contains(states[0].Status, "Area2");
            Assert.AreEqual(4, states[0].BatteryId);
            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(LogigramRegulation.Gamma, outcomes[0].Regulation);
            Assert.AreEqual(4, outcomes[0].BatteryId);
        }

        /// <summary>Area2 leave pairs with Porte2 (not Porte1).</summary>
        [TestMethod]
        public void Node3_Area2PairsWithPorte2()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)>
            {
                (1, start, start.AddSeconds(20)),
            };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Node3_Area2PairsWithPorte2)))
            {
                var unit = new Logigram1Node3Unit(pipeline, scopes);
                unit.RegulationDecisionWithBatteryOut.Do(_ => { });

                // Porte1 closed, Porte2 open — Area2 leave must not fire on Porte1.
                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(false, Vector3.Zero), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte1 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<bool, Vector3>, DateTime)>
                        {
                            (new ValueTuple<bool, Vector3>(true, Vector3.Zero), start.AddSeconds(1.2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<bool, Vector3>>("Porte2 ouverture"));

                Generators.Sequence(
                        pipeline,
                        new List<(ValueTuple<int, bool, string>, DateTime)>
                        {
                            (new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddTopicInput<ValueTuple<int, bool, string>>("Area2"));

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(0, unit.Node3FireCount);
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
