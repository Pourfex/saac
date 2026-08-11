// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Logigrammes
{
    using System;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Classification;
    using SaacAnalysisCasper.Core.Indices;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Stateful 9-node Logigramme 1 stepper (<c>Ressources/Logigramme1.md</c> links + Option C emits).
    /// Replaces the Miro flat priority mux.
    /// </summary>
    /// <remarks>
    /// Links: 1→2 hit, 2→1 hit, 2→3 miss, 3→C hit, 3→1 miss, 1→4 miss, 4→6 hit, 4→5 miss,
    /// 5→D hit, 5→7 miss, 6→E hit, 6→7 miss, 7→A hit, 7→8 miss, 8→B hit, 8→9 miss, 9→D hit, 9→4 miss.
    /// Option C: A→Alpha, B→Beta, C→Gamma, E→Gamma, D→no Classification post.
    /// Node evaluation model: on <c>GoToNode(n)</c> clear latches and immediately sample current levels;
    /// node 1 miss = timeout only; nodes 5/6 wait for first DoorClosed sample.
    /// After a sticky outcome (A/B/C/D/E), the stepper resets to node 1 for the next cycle.
    /// </remarks>
    internal sealed class Logigramme1NodeStepper
    {
        private readonly Emitter<ClassificationEvent> output;
        private readonly ParticipantId participant;
        private readonly RepeatedValidationSequenceFilter.Component alphaFilter;
        private readonly DifferentGeneratorButtonFilter.Component betaFilter;
        private readonly TimeSpan windowNode2;
        private readonly TimeSpan windowNode4;
        private readonly TimeSpan windowSequence;
        private readonly TimeSpan defaultMissTimeout;
        private int currentNode = 1;
        private DateTime? nodeEnteredAt;
        private bool? doorClosedLevel;
        private bool moduleSuccessLevel;
        private bool node2HitLevel;
        private bool node3HitLevel;
        private bool gazeHitLevel;
        private bool repeatedValLevel;
        private bool differentButtonLevel;
        private bool doorClosureLevel;
        private bool previousModuleSuccess;
        private bool previousNode2Hit;
        private bool previousNode3Hit;
        private bool previousGazeHit;
        private bool previousRepeatedVal;
        private bool previousDifferentButton;
        private bool previousDoorClosure;
        private DateTime lastEmittedOt = DateTime.MinValue;
        private bool evaluatingEntry;
        private bool pendingEntryReeval;

        private Logigramme1NodeStepper(
            Pipeline pipeline,
            ParticipantId participant,
            RepeatedValidationSequenceFilter.Component alphaFilter,
            DifferentGeneratorButtonFilter.Component betaFilter,
            string name)
        {
            this.participant = participant;
            this.alphaFilter = alphaFilter;
            this.betaFilter = betaFilter;
            this.windowNode2 = TimeSpan.FromMilliseconds(IndexFilterAssumptions.PostDoorSelectOrValidationWindowMs);
            this.windowNode4 = TimeSpan.FromMilliseconds(IndexFilterAssumptions.GazeCombineWindowMs);
            this.windowSequence = TimeSpan.FromMilliseconds(IndexFilterAssumptions.SequenceWindowMs);
            this.defaultMissTimeout = TimeSpan.FromMilliseconds(IndexFilterAssumptions.DefaultNodeMissTimeoutMs);
            this.output = pipeline.CreateEmitter<ClassificationEvent>(this, name + "-Out");
            this.ModuleSuccessIn = pipeline.CreateReceiver<bool>(this, this.ReceiveModuleSuccess, name + "-N1");
            this.PostDoorSelectOrValidationIn = pipeline.CreateReceiver<bool>(this, this.ReceiveNode2Hit, name + "-N2");
            this.Node3HitIn = pipeline.CreateReceiver<bool>(this, this.ReceiveNode3Hit, name + "-N3");
            this.GazeCombinedIn = pipeline.CreateReceiver<bool>(this, this.ReceiveGaze, name + "-N4");
            this.DoorClosedIn = pipeline.CreateReceiver<bool>(this, this.ReceiveDoorClosed, name + "-Door");
            this.RepeatedValidationIn = pipeline.CreateReceiver<bool>(this, this.ReceiveRepeatedValidation, name + "-N7");
            this.DifferentButtonIn = pipeline.CreateReceiver<bool>(this, this.ReceiveDifferentButton, name + "-N8");
            this.DoorClosureIn = pipeline.CreateReceiver<bool>(this, this.ReceiveDoorClosure, name + "-N9");
            this.nodeEnteredAt = null;
        }

        public Receiver<bool> ModuleSuccessIn { get; }

        public Receiver<bool> PostDoorSelectOrValidationIn { get; }

        public Receiver<bool> Node3HitIn { get; }

        public Receiver<bool> GazeCombinedIn { get; }

        public Receiver<bool> DoorClosedIn { get; }

        public Receiver<bool> RepeatedValidationIn { get; }

        public Receiver<bool> DifferentButtonIn { get; }

        public Receiver<bool> DoorClosureIn { get; }

        public Emitter<ClassificationEvent> Out => this.output;

        /// <summary>
        /// Wires predicate streams into the 9-node stepper and returns Classification products.
        /// </summary>
        public static IProducer<ClassificationEvent> Apply(
            Pipeline pipeline,
            ParticipantId participant,
            IProducer<bool> moduleSuccess,
            IProducer<bool> postDoorSelectOrValidation,
            IProducer<bool> node3Hit,
            IProducer<bool> gazeCombined,
            IProducer<bool> doorClosed,
            RepeatedValidationSequenceFilter.Component alphaFilter,
            DifferentGeneratorButtonFilter.Component betaFilter,
            IProducer<bool> doorClosure,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (moduleSuccess == null)
            {
                throw new ArgumentNullException(nameof(moduleSuccess));
            }

            if (postDoorSelectOrValidation == null)
            {
                throw new ArgumentNullException(nameof(postDoorSelectOrValidation));
            }

            if (node3Hit == null)
            {
                throw new ArgumentNullException(nameof(node3Hit));
            }

            if (gazeCombined == null)
            {
                throw new ArgumentNullException(nameof(gazeCombined));
            }

            if (doorClosed == null)
            {
                throw new ArgumentNullException(nameof(doorClosed));
            }

            if (alphaFilter == null)
            {
                throw new ArgumentNullException(nameof(alphaFilter));
            }

            if (betaFilter == null)
            {
                throw new ArgumentNullException(nameof(betaFilter));
            }

            if (doorClosure == null)
            {
                throw new ArgumentNullException(nameof(doorClosure));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(Logigramme1NodeStepper) : name;
            Logigramme1NodeStepper stepper = new Logigramme1NodeStepper(
                pipeline, participant, alphaFilter, betaFilter, operatorName);
            moduleSuccess.PipeTo(stepper.ModuleSuccessIn, DeliveryPolicy.Unlimited);
            postDoorSelectOrValidation.PipeTo(stepper.PostDoorSelectOrValidationIn, DeliveryPolicy.Unlimited);
            node3Hit.PipeTo(stepper.Node3HitIn, DeliveryPolicy.Unlimited);
            gazeCombined.PipeTo(stepper.GazeCombinedIn, DeliveryPolicy.Unlimited);
            doorClosed.PipeTo(stepper.DoorClosedIn, DeliveryPolicy.Unlimited);
            ((IProducer<bool>)alphaFilter.Out).PipeTo(stepper.RepeatedValidationIn, DeliveryPolicy.Unlimited);
            ((IProducer<bool>)betaFilter.Out).PipeTo(stepper.DifferentButtonIn, DeliveryPolicy.Unlimited);
            doorClosure.PipeTo(stepper.DoorClosureIn, DeliveryPolicy.Unlimited);
            return stepper.Out;
        }

        private void ReceiveModuleSuccess(bool success, Envelope envelope)
        {
            // Seed initial node-1 dwell clock from ModuleStatus only (not door/gaze chatter).
            if (this.currentNode == 1 && !this.nodeEnteredAt.HasValue)
            {
                this.nodeEnteredAt = envelope.OriginatingTime;
            }

            this.TryMissTimeout(envelope.OriginatingTime);
            this.moduleSuccessLevel = success;

            bool rising = success && !this.previousModuleSuccess;
            this.previousModuleSuccess = success;

            if (this.currentNode != 1)
            {
                return;
            }

            // Rising edge while on node 1. Already-true on entry is handled in EvaluateNodeOnEntryCore.
            // Already-seen / false ModuleStatus is NOT an immediate 1→4 miss (timeout only).
            if (rising)
            {
                this.GoToNode(2, envelope.OriginatingTime);
            }
        }

        private void ReceiveNode2Hit(bool hit, Envelope envelope)
        {
            this.TryMissTimeout(envelope.OriginatingTime);
            this.node2HitLevel = hit;

            bool rising = hit && !this.previousNode2Hit;
            this.previousNode2Hit = hit;

            if (this.currentNode != 2)
            {
                return;
            }

            if (rising)
            {
                this.GoToNode(1, envelope.OriginatingTime);
            }
        }

        private void ReceiveNode3Hit(bool hit, Envelope envelope)
        {
            this.TryMissTimeout(envelope.OriginatingTime);
            this.node3HitLevel = hit;

            bool rising = hit && !this.previousNode3Hit;
            this.previousNode3Hit = hit;

            if (this.currentNode != 3)
            {
                return;
            }

            if (rising)
            {
                // 3→C → Gamma (Option C)
                this.Emit(ClassificationLabel.Gamma, envelope.OriginatingTime);
                this.GoToNode(1, envelope.OriginatingTime);
            }
        }

        private void ReceiveGaze(bool hit, Envelope envelope)
        {
            this.TryMissTimeout(envelope.OriginatingTime);
            this.gazeHitLevel = hit;

            bool rising = hit && !this.previousGazeHit;
            this.previousGazeHit = hit;

            if (this.currentNode != 4)
            {
                return;
            }

            if (rising)
            {
                this.GoToNode(6, envelope.OriginatingTime);
            }
        }

        private void ReceiveDoorClosed(bool closed, Envelope envelope)
        {
            this.TryMissTimeout(envelope.OriginatingTime);
            this.doorClosedLevel = closed;

            if (this.currentNode == 2 && !closed)
            {
                // Door opens while on node 2 → immediate miss to 3.
                this.GoToNode(3, envelope.OriginatingTime);
                return;
            }

            if (this.currentNode == 5)
            {
                this.DecideDoorClosedNode(hitIsSilentD: true, envelope.OriginatingTime);
                return;
            }

            if (this.currentNode == 6)
            {
                this.DecideDoorClosedNode(hitIsSilentD: false, envelope.OriginatingTime);
            }
        }

        private void ReceiveRepeatedValidation(bool level, Envelope envelope)
        {
            this.TryMissTimeout(envelope.OriginatingTime);
            this.repeatedValLevel = level;

            bool rising = level && !this.previousRepeatedVal;
            this.previousRepeatedVal = level;

            if (this.currentNode != 7)
            {
                return;
            }

            if (rising)
            {
                // 7→A → Alpha
                this.Emit(ClassificationLabel.Alpha, envelope.OriginatingTime);
                this.GoToNode(1, envelope.OriginatingTime);
            }
        }

        private void ReceiveDifferentButton(bool hit, Envelope envelope)
        {
            this.TryMissTimeout(envelope.OriginatingTime);
            this.differentButtonLevel = hit;

            bool rising = hit && !this.previousDifferentButton;
            this.previousDifferentButton = hit;

            if (this.currentNode != 8)
            {
                return;
            }

            if (rising)
            {
                // 8→B → Beta
                this.Emit(ClassificationLabel.Beta, envelope.OriginatingTime);
                this.GoToNode(1, envelope.OriginatingTime);
            }
        }

        private void ReceiveDoorClosure(bool hit, Envelope envelope)
        {
            this.TryMissTimeout(envelope.OriginatingTime);
            this.doorClosureLevel = hit;

            bool rising = hit && !this.previousDoorClosure;
            this.previousDoorClosure = hit;

            if (this.currentNode != 9)
            {
                return;
            }

            if (rising)
            {
                // 9→D → no Classification post
                this.GoToNode(1, envelope.OriginatingTime);
            }
        }

        private void TryMissTimeout(DateTime originatingTime)
        {
            // Entry time is set only by GoToNode (or first ModuleSuccess seed for initial node 1).
            // Do not seed from unrelated receivers — that burned node-1 timeout before ModuleStatus.
            if (!this.nodeEnteredAt.HasValue)
            {
                return;
            }

            TimeSpan elapsed = originatingTime - this.nodeEnteredAt.Value;
            if (elapsed < TimeSpan.Zero)
            {
                return;
            }

            switch (this.currentNode)
            {
                case 1:
                    // Node 1 miss = timeout / non-event (not every already-seen ModuleStatus false).
                    if (elapsed >= this.defaultMissTimeout)
                    {
                        this.GoToNode(4, originatingTime);
                    }

                    break;
                case 2:
                    if (elapsed >= this.windowNode2)
                    {
                        this.GoToNode(3, originatingTime);
                    }

                    break;
                case 3:
                    if (elapsed >= this.defaultMissTimeout)
                    {
                        this.GoToNode(1, originatingTime);
                    }

                    break;
                case 4:
                    if (elapsed >= this.windowNode4)
                    {
                        this.GoToNode(5, originatingTime);
                    }

                    break;
                case 5:
                case 6:
                    // Deferred miss if DoorClosed never arrives.
                    if (elapsed >= this.defaultMissTimeout)
                    {
                        this.GoToNode(7, originatingTime);
                    }

                    break;
                case 7:
                    if (elapsed >= this.windowSequence)
                    {
                        this.GoToNode(8, originatingTime);
                    }

                    break;
                case 8:
                    if (elapsed >= this.windowSequence)
                    {
                        this.GoToNode(9, originatingTime);
                    }

                    break;
                case 9:
                    if (elapsed >= this.defaultMissTimeout)
                    {
                        this.GoToNode(4, originatingTime);
                    }

                    break;
            }
        }

        private void GoToNode(int node, DateTime originatingTime)
        {
            this.currentNode = node;
            this.nodeEnteredAt = originatingTime;

            // Clear rising-edge latches only — keep held levels for immediate entry sampling.
            this.ClearLatchesForNode(node);
            this.ArmSequenceFiltersOnEntry(node, originatingTime);

            if (this.evaluatingEntry)
            {
                // Nested transition (e.g. 2→3 while sampling entry): re-evaluate destination after outer returns.
                this.pendingEntryReeval = true;
                return;
            }

            this.evaluatingEntry = true;
            try
            {
                do
                {
                    this.pendingEntryReeval = false;
                    this.EvaluateNodeOnEntryCore(originatingTime);
                }
                while (this.pendingEntryReeval);
            }
            finally
            {
                this.evaluatingEntry = false;
            }
        }

        private void ClearLatchesForNode(int node)
        {
            switch (node)
            {
                case 1:
                    this.previousModuleSuccess = false;
                    break;
                case 2:
                    this.previousNode2Hit = false;
                    break;
                case 3:
                    this.previousNode3Hit = false;
                    break;
                case 4:
                    this.previousGazeHit = false;
                    break;
                case 7:
                    this.previousRepeatedVal = false;
                    break;
                case 8:
                    this.previousDifferentButton = false;
                    break;
                case 9:
                    this.previousDoorClosure = false;
                    break;
            }
        }

        private void ArmSequenceFiltersOnEntry(int node, DateTime originatingTime)
        {
            if (node == 7)
            {
                this.alphaFilter.Reset(originatingTime);
            }
            else if (node == 8)
            {
                this.betaFilter.Reset(originatingTime);
            }
        }

        private void EvaluateNodeOnEntryCore(DateTime originatingTime)
        {
            switch (this.currentNode)
            {
                case 1:
                    if (this.moduleSuccessLevel)
                    {
                        this.GoToNode(2, originatingTime);
                    }

                    break;
                case 2:
                    // Immediate miss if door already open on entry.
                    if (this.doorClosedLevel.HasValue && !this.doorClosedLevel.Value)
                    {
                        this.GoToNode(3, originatingTime);
                    }
                    else if (this.node2HitLevel)
                    {
                        this.GoToNode(1, originatingTime);
                    }

                    break;
                case 3:
                    if (this.node3HitLevel)
                    {
                        this.Emit(ClassificationLabel.Gamma, originatingTime);
                        this.GoToNode(1, originatingTime);
                    }

                    break;
                case 4:
                    if (this.gazeHitLevel)
                    {
                        this.GoToNode(6, originatingTime);
                    }

                    break;
                case 5:
                    // Wait for first DoorClosed sample — no default-open miss (timeout → 7).
                    if (this.doorClosedLevel.HasValue)
                    {
                        this.DecideDoorClosedNode(hitIsSilentD: true, originatingTime);
                    }

                    break;
                case 6:
                    if (this.doorClosedLevel.HasValue)
                    {
                        this.DecideDoorClosedNode(hitIsSilentD: false, originatingTime);
                    }

                    break;
                case 7:
                    if (this.repeatedValLevel)
                    {
                        this.Emit(ClassificationLabel.Alpha, originatingTime);
                        this.GoToNode(1, originatingTime);
                    }

                    break;
                case 8:
                    if (this.differentButtonLevel)
                    {
                        this.Emit(ClassificationLabel.Beta, originatingTime);
                        this.GoToNode(1, originatingTime);
                    }

                    break;
                case 9:
                    if (this.doorClosureLevel)
                    {
                        this.GoToNode(1, originatingTime);
                    }

                    break;
            }
        }

        private void DecideDoorClosedNode(bool hitIsSilentD, DateTime originatingTime)
        {
            if (!this.doorClosedLevel.HasValue)
            {
                return;
            }

            if (this.doorClosedLevel.Value)
            {
                if (!hitIsSilentD)
                {
                    // 6→E → Gamma (Option C)
                    this.Emit(ClassificationLabel.Gamma, originatingTime);
                }

                // 5→D silence / 6→E done → reset cycle
                this.GoToNode(1, originatingTime);
            }
            else
            {
                this.GoToNode(7, originatingTime);
            }
        }

        private void Emit(ClassificationLabel label, DateTime originatingTime)
        {
            DateTime emitOt = originatingTime;
            if (emitOt <= this.lastEmittedOt)
            {
                emitOt = this.lastEmittedOt.AddTicks(1);
            }

            ClassificationEvent evt = new ClassificationEvent(label, this.participant, Logigramme1Operator.GraphIdValue);
            this.output.Post(evt, emitOt);
            this.lastEmittedOt = emitOt;
        }
    }
}
