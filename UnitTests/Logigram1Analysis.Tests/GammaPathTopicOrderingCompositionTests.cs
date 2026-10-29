// <copyright file="GammaPathTopicOrderingCompositionTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;
    using Logigram1Analysis;
    using Microsoft.Psi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SAAC.Logigram1;
    using SAAC.Logigram1.Logic;

    /// <summary>
    /// Story 3.6: the runner composition (scrambled arrivals → Order → Sequence → ordered input) and a guard
    /// that the runner keeps calling the merge.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class GammaPathTopicOrderingCompositionTests
    {
        /// <summary>
        /// Casper5 shape: Module status (late OT) and the valid leave arrive before an earlier-OT orphan leave and
        /// the door sample. After ordering, only the leave following node 1 Gammas, leave-aligned.
        /// </summary>
        [TestMethod]
        public void ScrambledArrivals_OrderedThroughUnit_GammaOnlyForLeaveAfterNode1()
        {
            DateTime t0 = new DateTime(2026, 3, 20, 15, 51, 0, DateTimeKind.Utc);
            var arrivals = new List<(GammaPathTopicEvent, DateTime)>
            {
                (new GammaPathTopicEvent("Module status", new ValueTuple<int, string>(20001, "OK")), t0.AddSeconds(77)),
                (new GammaPathTopicEvent("Area1", new ValueTuple<int, bool, string>(-1, false, "GeneratorArea")), t0.AddSeconds(80)),
                (new GammaPathTopicEvent("Area1", new ValueTuple<int, bool, string>(-1, false, "GeneratorArea")), t0.AddSeconds(30)),
                (new GammaPathTopicEvent("Porte1 ouverture", new ValueTuple<bool, Vector3>(false, Vector3.Zero)), t0.AddSeconds(1)),
            };

            IReadOnlyList<(GammaPathTopicEvent Event, DateTime OriginatingTime)> ordered =
                GammaPathTopicOrdering.Order(arrivals);

            var states = new List<(DateTime Time, LogigramState State)>();
            var outcomes = new List<(DateTime Time, LogigramOutcome Outcome)>();
            long outOfOrder;
            long gammaDecisions;

            using (Pipeline pipeline = Pipeline.Create(nameof(this.ScrambledArrivals_OrderedThroughUnit_GammaOnlyForLeaveAfterNode1)))
            {
                var unit = new Logigram1GammaPathUnit(pipeline, Array.Empty<(int, DateTime, DateTime)>());
                var writer = new LogigramOutcomeWriter(pipeline);
                unit.EvidenceOut.PipeTo(writer.EvidenceIn);
                unit.RegulationDecisionWithBatteryOut.PipeTo(writer.RegulationDecisionWithBatteryIn);
                unit.StateOut.Do((state, envelope) => states.Add((
                    envelope.OriginatingTime,
                    new LogigramState { Status = state.Status, BatteryId = state.BatteryId, PersonId = state.PersonId })));
                writer.Out.Do((outcome, envelope) => outcomes.Add((
                    envelope.OriginatingTime,
                    new LogigramOutcome
                    {
                        Regulation = outcome.Regulation,
                        BatteryId = outcome.BatteryId,
                        PersonId = outcome.PersonId,
                    })));

                Generators.Sequence(pipeline, ordered).PipeTo(unit.AddOrderedTopicInput());
                pipeline.Run(ReplayDescriptor.ReplayAll);

                outOfOrder = unit.OutOfOrderInputCount;
                gammaDecisions = unit.GammaDecisionEmitCount;
            }

            Assert.AreEqual(0, outOfOrder);
            Assert.AreEqual(1, gammaDecisions, "The leave at +30s precedes node 1 and must not Gamma.");
            Assert.AreEqual(1, outcomes.Count);
            Assert.AreEqual(1, outcomes[0].Outcome.PersonId);
            Assert.AreEqual(t0.AddSeconds(80), outcomes[0].Time);

            var node3 = states.FindAll(s => s.State.PersonId == 1
                && s.State.Status.StartsWith("node-3:", StringComparison.Ordinal));
            Assert.AreEqual(1, node3.Count);
            Assert.AreEqual(outcomes[0].Time, node3[0].Time);
            Assert.IsTrue(
                states.Exists(s => s.State.PersonId == 1
                    && s.State.Status.StartsWith("node-1:", StringComparison.Ordinal)
                    && s.Time < outcomes[0].Time));
        }

        /// <summary>Removing the OT-ordering merge from the runner must fail CI.</summary>
        [TestMethod]
        public void Runner_StillMergesTopicsThroughGammaPathTopicOrdering()
        {
            string dir = AppContext.BaseDirectory;
            string runnerPath = null;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "Applications", "Logigram1Analysis", "FirstUnitPipelineRunner.cs");
                if (File.Exists(candidate))
                {
                    runnerPath = candidate;
                    break;
                }

                dir = Path.GetDirectoryName(dir);
            }

            Assert.IsNotNull(runnerPath, "FirstUnitPipelineRunner.cs not found above the test output directory.");
            StringAssert.Contains(
                File.ReadAllText(runnerPath),
                "GammaPathTopicOrdering.Order(LoadTopicEvents",
                "Runner must merge topic arrivals through GammaPathTopicOrdering.Order.");
        }
    }
}
