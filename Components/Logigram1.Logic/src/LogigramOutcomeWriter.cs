// <copyright file="LogigramOutcomeWriter.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1.Logic
{
    using System;
    using Microsoft.Psi;
    using SAAC.Logigram1;

    /// <summary>
    /// Designated AD-10 aggregator / outcome writer: sole producer of <see cref="LogigramOutcome"/>
    /// on a Psi pipeline (N evidence receivers → one Outcome emitter).
    /// Pattern mirrors CollaborationIndices Fusion N→1 receivers (e.g. InstantStateComponent).
    /// Console wiring: Analysis pipes GammaPath evidence here and persists <see cref="Out"/>.
    /// </summary>
    public class LogigramOutcomeWriter : IProducer<LogigramOutcome>
    {
        private readonly Pipeline pipeline;
        private readonly string name;
        private int evidenceInputCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="LogigramOutcomeWriter"/> class.
        /// </summary>
        /// <param name="pipeline">Pipeline hosting the component.</param>
        /// <param name="name">Name of the component, prefix of its streams.</param>
        public LogigramOutcomeWriter(Pipeline pipeline, string name = nameof(LogigramOutcomeWriter))
        {
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            this.name = name;

            this.EvidenceIn = this.CreateEvidenceReceiver(0);
            this.evidenceInputCount = 1;

            // Optional fusion seed: only this component turns a decided regulation into Outcome.
            this.RegulationDecisionIn = pipeline.CreateReceiver<LogigramRegulation>(
                this,
                this.ReceiveRegulationDecision,
                $"{name}-RegulationDecision");

            // Scoped path for story 2.4: stamps BatteryId on Outcome when a Battery scope is supplied.
            this.RegulationDecisionWithBatteryIn = pipeline.CreateReceiver<(LogigramRegulation Regulation, int BatteryId)>(
                this,
                this.ReceiveRegulationDecisionWithBattery,
                $"{name}-RegulationDecisionWithBattery");

            this.Out = pipeline.CreateEmitter<LogigramOutcome>(this, $"{name}-Outcome");
        }

        /// <summary>
        /// Gets the sole <see cref="LogigramOutcome"/> emitter. Persist with
        /// <see cref="LogigramOutcome.StreamName"/> when wiring to a store (story 2.4).
        /// </summary>
        public Emitter<LogigramOutcome> Out { get; }

        /// <summary>Gets the first path/node evidence input.</summary>
        public Receiver<LogigramEvidence> EvidenceIn { get; }

        /// <summary>
        /// Gets an optional regulation decision input used by this writer's fusion (and tests).
        /// Upstream units must not write <see cref="LogigramOutcome.StreamName"/> themselves;
        /// piping a decision here still keeps this component as the sole Outcome producer.
        /// Unscoped: emitted <see cref="LogigramOutcome.BatteryId"/> remains default (0).
        /// </summary>
        public Receiver<LogigramRegulation> RegulationDecisionIn { get; }

        /// <summary>
        /// Gets a Battery-scoped regulation decision input (AD-6). Emits Outcome with
        /// <see cref="LogigramOutcome.BatteryId"/> stamped from the supplied scope.
        /// </summary>
        public Receiver<(LogigramRegulation Regulation, int BatteryId)> RegulationDecisionWithBatteryIn { get; }

        /// <summary>
        /// Gets the number of evidence messages received (any input). Used for honesty checks.
        /// </summary>
        public long EvidenceMessageCount { get; private set; }

        /// <summary>
        /// Gets the number of <see cref="LogigramOutcome"/> messages emitted.
        /// </summary>
        public long OutcomeEmitCount { get; private set; }

        /// <summary>
        /// Adds another path/node evidence receiver (N→1 pattern). Call before the pipeline runs.
        /// </summary>
        /// <returns>The new evidence receiver.</returns>
        public Receiver<LogigramEvidence> AddEvidenceInput()
        {
            int id = this.evidenceInputCount++;
            return this.CreateEvidenceReceiver(id);
        }

        private Receiver<LogigramEvidence> CreateEvidenceReceiver(int id)
        {
            return this.pipeline.CreateReceiver<LogigramEvidence>(
                this,
                (evidence, envelope) => this.ReceiveEvidence(evidence),
                $"{this.name}-Evidence-{id}");
        }

        private void ReceiveEvidence(LogigramEvidence evidence)
        {
            if (evidence == null)
            {
                return;
            }

            // Path/node evidence alone never invents or re-emits terminals.
            this.EvidenceMessageCount++;
        }

        private void ReceiveRegulationDecision(LogigramRegulation regulation, Envelope envelope)
        {
            this.EmitOutcome(regulation, batteryId: 0, envelope.OriginatingTime);
        }

        private void ReceiveRegulationDecisionWithBattery(
            (LogigramRegulation Regulation, int BatteryId) decision,
            Envelope envelope)
        {
            this.EmitOutcome(decision.Regulation, decision.BatteryId, envelope.OriginatingTime);
        }

        private void EmitOutcome(LogigramRegulation regulation, int batteryId, DateTime originatingTime)
        {
            var outcome = new LogigramOutcome
            {
                Regulation = regulation,
                BatteryId = batteryId,
            };
            this.Out.Post(outcome, originatingTime);
            this.OutcomeEmitCount++;
        }
    }
}
