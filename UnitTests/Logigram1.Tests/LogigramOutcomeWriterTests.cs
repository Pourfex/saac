// <copyright file="LogigramOutcomeWriterTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Microsoft.Psi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SAAC.Logigram1;
    using SAAC.Logigram1.Logic;

    /// <summary>
    /// Tests for the designated <see cref="LogigramOutcomeWriter"/>.
    /// </summary>
    [TestClass]
    public class LogigramOutcomeWriterTests
    {
        /// <summary>
        /// Only the designated writer type exposes an Outcome emitter in the Logic assembly.
        /// </summary>
        [TestMethod]
        public void Writer_IsSoleOutcomeEmitterType_InLogicAssembly()
        {
            Assembly logicAssembly = typeof(LogigramOutcomeWriter).Assembly;
            Type outcomeEmitter = typeof(Emitter<>).MakeGenericType(typeof(LogigramOutcome));

            var typesWithOutcomeEmitter = logicAssembly.GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract)
                .Where(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Any(property => property.PropertyType == outcomeEmitter))
                .ToList();

            Assert.AreEqual(1, typesWithOutcomeEmitter.Count);
            Assert.AreEqual(typeof(LogigramOutcomeWriter), typesWithOutcomeEmitter[0]);
        }

        /// <summary>
        /// Wired with no evidence: does not emit a fabricated Alpha/Beta/Gamma terminal.
        /// </summary>
        [TestMethod]
        public void Writer_EmptyEvidence_DoesNotEmitFakeTerminal()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Writer_EmptyEvidence_DoesNotEmitFakeTerminal)))
            {
                var writer = new LogigramOutcomeWriter(pipeline);
                writer.Out.Do(outcome => outcomes.Add(CloneOutcome(outcome)));

                // Pace the pipeline without feeding evidence — honest absent Outcome.
                Generators.Sequence(
                        pipeline,
                        new List<(bool, DateTime)>
                        {
                            (true, start),
                        });

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(0, writer.EvidenceMessageCount);
                Assert.AreEqual(0, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(0, outcomes.Count);
        }

        /// <summary>
        /// Path/node evidence alone must not invent Alpha/Beta/Gamma.
        /// </summary>
        [TestMethod]
        public void Writer_PathOnlyEvidence_DoesNotEmitAlphaBetaOrGamma()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Writer_PathOnlyEvidence_DoesNotEmitAlphaBetaOrGamma)))
            {
                var writer = new LogigramOutcomeWriter(pipeline);
                writer.Out.Do(outcome => outcomes.Add(CloneOutcome(outcome)));

                Generators.Sequence(
                        pipeline,
                        new List<(LogigramEvidence, DateTime)>
                        {
                            (new LogigramEvidence { NodeOrPathId = "node-1" }, start),
                            (new LogigramEvidence { NodeOrPathId = "node-2" }, start.AddSeconds(1)),
                        })
                    .PipeTo(writer.EvidenceIn);

                pipeline.Run(ReplayDescriptor.ReplayAll);
            }

            Assert.AreEqual(0, outcomes.Count);
            Assert.IsFalse(outcomes.Any(outcome =>
                outcome.Regulation == LogigramRegulation.Alpha
                || outcome.Regulation == LogigramRegulation.Beta
                || outcome.Regulation == LogigramRegulation.Gamma));
        }

        /// <summary>
        /// A regulation decision posted through the writer is the path that emits Outcome.
        /// </summary>
        [TestMethod]
        public void Writer_RegulationDecision_IsSolePathToOutcomeEmission()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Writer_RegulationDecision_IsSolePathToOutcomeEmission)))
            {
                var writer = new LogigramOutcomeWriter(pipeline);
                writer.Out.Do(outcome => outcomes.Add(CloneOutcome(outcome)));

                Generators.Sequence(
                        pipeline,
                        new List<(LogigramEvidence, DateTime)>
                        {
                            (new LogigramEvidence { NodeOrPathId = "path-a" }, start),
                        })
                    .PipeTo(writer.EvidenceIn);

                Generators.Sequence(
                        pipeline,
                        new List<(LogigramRegulation, DateTime)>
                        {
                            (LogigramRegulation.Gamma, start.AddSeconds(1)),
                        })
                    .PipeTo(writer.RegulationDecisionIn);

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, writer.OutcomeEmitCount);
                Assert.IsTrue(writer.EvidenceMessageCount >= 1);
            }

            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(LogigramRegulation.Gamma, outcomes[0].Regulation);
            Assert.AreEqual(0, outcomes[0].BatteryId);
            Assert.AreEqual(0, outcomes[0].PersonId);
        }

        /// <summary>
        /// Scoped regulation decision stamps BatteryId + PersonId on the emitted Outcome.
        /// </summary>
        [TestMethod]
        public void Writer_RegulationDecisionWithBattery_StampsBatteryIdAndPersonIdOnOutcome()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Writer_RegulationDecisionWithBattery_StampsBatteryIdAndPersonIdOnOutcome)))
            {
                var writer = new LogigramOutcomeWriter(pipeline);
                writer.Out.Do(outcome => outcomes.Add(CloneOutcome(outcome)));

                Generators.Sequence(
                        pipeline,
                        new List<((LogigramRegulation, int, int), DateTime)>
                        {
                            ((LogigramRegulation.Alpha, 7, 2), start),
                        })
                    .PipeTo(writer.RegulationDecisionWithBatteryIn);

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(LogigramRegulation.Alpha, outcomes[0].Regulation);
            Assert.AreEqual(7, outcomes[0].BatteryId);
            Assert.AreEqual(2, outcomes[0].PersonId);
        }

        /// <summary>
        /// Evidence after a regulation decision must not duplicate the Outcome terminal.
        /// </summary>
        [TestMethod]
        public void Writer_EvidenceAfterRegulation_DoesNotReEmitOutcome()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Writer_EvidenceAfterRegulation_DoesNotReEmitOutcome)))
            {
                var writer = new LogigramOutcomeWriter(pipeline);
                writer.Out.Do(outcome => outcomes.Add(CloneOutcome(outcome)));

                Generators.Sequence(
                        pipeline,
                        new List<(LogigramRegulation, DateTime)>
                        {
                            (LogigramRegulation.Beta, start),
                        })
                    .PipeTo(writer.RegulationDecisionIn);

                Generators.Sequence(
                        pipeline,
                        new List<(LogigramEvidence, DateTime)>
                        {
                            (new LogigramEvidence { NodeOrPathId = "after" }, start.AddSeconds(1)),
                        })
                    .PipeTo(writer.EvidenceIn);

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(1, writer.OutcomeEmitCount);
                Assert.AreEqual(1, writer.EvidenceMessageCount);
            }

            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(LogigramRegulation.Beta, outcomes[0].Regulation);
            Assert.AreEqual(0, outcomes[0].BatteryId);
            Assert.AreEqual(0, outcomes[0].PersonId);
        }

        /// <summary>
        /// Additional evidence receivers follow the N→1 pattern without emitting Outcome.
        /// </summary>
        [TestMethod]
        public void Writer_AddEvidenceInput_ReceivesWithoutEmittingOutcome()
        {
            var outcomes = new List<LogigramOutcome>();
            DateTime start = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

            using (Pipeline pipeline = Pipeline.Create(nameof(this.Writer_AddEvidenceInput_ReceivesWithoutEmittingOutcome)))
            {
                var writer = new LogigramOutcomeWriter(pipeline);
                Receiver<LogigramEvidence> second = writer.AddEvidenceInput();
                writer.Out.Do(outcome => outcomes.Add(CloneOutcome(outcome)));

                Generators.Sequence(
                        pipeline,
                        new List<(LogigramEvidence, DateTime)>
                        {
                            (new LogigramEvidence { NodeOrPathId = "a" }, start),
                        })
                    .PipeTo(writer.EvidenceIn);

                Generators.Sequence(
                        pipeline,
                        new List<(LogigramEvidence, DateTime)>
                        {
                            (new LogigramEvidence { NodeOrPathId = "b" }, start.AddMilliseconds(10)),
                        })
                    .PipeTo(second);

                pipeline.Run(ReplayDescriptor.ReplayAll);

                Assert.AreEqual(2, writer.EvidenceMessageCount);
                Assert.AreEqual(0, writer.OutcomeEmitCount);
            }

            Assert.AreEqual(0, outcomes.Count);
        }

        private static LogigramOutcome CloneOutcome(LogigramOutcome outcome)
        {
            return new LogigramOutcome
            {
                Regulation = outcome.Regulation,
                BatteryId = outcome.BatteryId,
                PersonId = outcome.PersonId,
            };
        }
    }
}
