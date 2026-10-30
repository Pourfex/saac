// <copyright file="Logigram1AlphaBetaPathUnitTests.cs" company="SAAC">
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
    /// I/O matrix for <see cref="Logigram1AlphaBetaPathUnit"/> Idle scaffold (story 4.1).
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class Logigram1AlphaBetaPathUnitTests
    {
        private static int startSequence;

        private static DateTime UniqueStart()
        {
            int n = System.Threading.Interlocked.Increment(ref startSequence);
            return new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc).AddHours(n);
        }

        [TestMethod]
        public void AlphaBetaPath_UnitId_IsLogigram1AlphaBetaPath()
        {
            Assert.AreEqual("Logigram1.AlphaBetaPath", Logigram1AlphaBetaPathUnit.UnitId);
        }

        [TestMethod]
        public void AlphaBetaPath_StatusPrefixes_UseAbLabel()
        {
            Assert.AreEqual("ab-idle:", Logigram1AlphaBetaPathUnit.IdleStatusPrefix);
            Assert.AreEqual("ab-gazed:", Logigram1AlphaBetaPathUnit.GazedStatusPrefix);
            Assert.AreEqual("ab-not-gazed:", Logigram1AlphaBetaPathUnit.NotGazedStatusPrefix);
        }

        [TestMethod]
        public void IdlePrime_EmitsStampedAbIdle_ForBothPersonsPerBattery()
        {
            var states = new List<LogigramState>();
            var evidence = new List<LogigramEvidence>();
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (11, start, start.AddSeconds(10)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.IdlePrime_EmitsStampedAbIdle_ForBothPersonsPerBattery)))
            {
                var unit = new Logigram1AlphaBetaPathUnit(pipeline, scopes);
                unit.StateOut.Do(state => states.Add(CloneState(state)));
                unit.EvidenceOut.Do(ev => evidence.Add(CloneEvidence(ev)));

                Generators.Sequence(pipeline, new[] { (true, start) })
                    .PipeTo(unit.AddIdlePrimeInput());

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(2, unit.IdleEmitCount);
                Assert.AreEqual(2, unit.StateEmitCount);
                Assert.AreEqual(2, unit.EvidenceEmitCount);
            }

            Assert.AreEqual(2, states.Count);
            Assert.IsTrue(states.Exists(s =>
                s.PersonId == 1
                && s.BatteryId == 11
                && s.Status != null
                && s.Status.StartsWith("ab-idle:", StringComparison.Ordinal)));
            Assert.IsTrue(states.Exists(s =>
                s.PersonId == 2
                && s.BatteryId == 11
                && s.Status != null
                && s.Status.StartsWith("ab-idle:", StringComparison.Ordinal)));
            Assert.IsFalse(states.Exists(s =>
                s.Status != null
                && (s.Status.StartsWith("ab-gazed:", StringComparison.Ordinal)
                    || s.Status.StartsWith("node-1:", StringComparison.Ordinal))));
            Assert.AreEqual(2, evidence.Count);
        }

        [TestMethod]
        public void ResolveBatteryId_OutsideScopes_ReturnsZero()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (9, start, start.AddSeconds(5)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.ResolveBatteryId_OutsideScopes_ReturnsZero)))
            {
                var unit = new Logigram1AlphaBetaPathUnit(pipeline, scopes);
                Assert.AreEqual(9, unit.ResolveBatteryId(start.AddSeconds(1)));
                Assert.AreEqual(0, unit.ResolveBatteryId(start.AddSeconds(10)));
                Assert.AreEqual(0, unit.ResolveBatteryId(start.AddSeconds(-1)));
            }
        }

        [TestMethod]
        public void IdlePrime_IsIdempotent_SecondPrimeDoesNotReEmit()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (3, start, start.AddSeconds(5)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.IdlePrime_IsIdempotent_SecondPrimeDoesNotReEmit)))
            {
                var unit = new Logigram1AlphaBetaPathUnit(pipeline, scopes);
                unit.StateOut.Do(_ => { });
                unit.EvidenceOut.Do(_ => { });

                Generators.Sequence(
                        pipeline,
                        new[]
                        {
                            (true, start),
                            (true, start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddIdlePrimeInput());

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(2, unit.IdleEmitCount);
                Assert.AreEqual(2, unit.StateEmitCount);
            }
        }

        [TestMethod]
        public void OrderedTopicInput_AcceptsEntryTopics_WithoutOutcomes()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(5)) };

            using (Pipeline pipeline = Pipeline.Create(nameof(this.OrderedTopicInput_AcceptsEntryTopics_WithoutOutcomes)))
            {
                var unit = new Logigram1AlphaBetaPathUnit(pipeline, scopes);
                unit.StateOut.Do(_ => { });
                unit.EvidenceOut.Do(_ => { });

                Generators.Sequence(
                        pipeline,
                        new List<(AlphaBetaPathTopicEvent, DateTime)>
                        {
                            (new AlphaBetaPathTopicEvent("M1-Validation", true), start.AddSeconds(1)),
                            (new AlphaBetaPathTopicEvent("Module status", new ValueTuple<int, string>(1, "OK")), start.AddSeconds(2)),
                        })
                    .PipeTo(unit.AddOrderedTopicInput());

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(2, unit.TopicHitCount);
                Assert.AreEqual(0, unit.IdleEmitCount);
                Assert.AreEqual(0, unit.StateEmitCount);
            }
        }

        [TestMethod]
        public void OrderedTopicInput_UnsupportedTopic_FailsLoudWithTopicName()
        {
            DateTime start = UniqueStart();
            var scopes = new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(5)) };
            Exception caught = null;

            using (Pipeline pipeline = Pipeline.Create(nameof(this.OrderedTopicInput_UnsupportedTopic_FailsLoudWithTopicName)))
            {
                var unit = new Logigram1AlphaBetaPathUnit(pipeline, scopes);
                unit.StateOut.Do(_ => { });
                unit.EvidenceOut.Do(_ => { });

                Generators.Sequence(
                        pipeline,
                        new List<(AlphaBetaPathTopicEvent, DateTime)>
                        {
                            (new AlphaBetaPathTopicEvent("Gaze", new ValueTuple<int, bool, string>(1, true, "Door")), start.AddSeconds(1)),
                        })
                    .PipeTo(unit.AddOrderedTopicInput());

                try
                {
                    pipeline.Run(ReplayDescriptor.ReplayAll);
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            }

            Assert.IsNotNull(caught, "Unsupported topic must fail loud.");
            string message = FlattenExceptionMessage(caught);
            StringAssert.Contains(message, "Gaze");
            StringAssert.Contains(message, "Unsupported AlphaBetaPath topic");
        }

        private static string FlattenExceptionMessage(Exception exception)
        {
            if (exception is AggregateException aggregate)
            {
                Exception flat = aggregate.Flatten().InnerException ?? aggregate;
                return flat.ToString();
            }

            return exception.ToString();
        }

        private static LogigramState CloneState(LogigramState state)
        {
            return new LogigramState
            {
                Status = state.Status,
                BatteryId = state.BatteryId,
                PersonId = state.PersonId,
            };
        }

        private static LogigramEvidence CloneEvidence(LogigramEvidence evidence)
        {
            return new LogigramEvidence
            {
                NodeOrPathId = evidence.NodeOrPathId,
                BatteryId = evidence.BatteryId,
                PersonId = evidence.PersonId,
            };
        }
    }
}
