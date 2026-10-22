// <copyright file="Logigram1FirstUnit.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1.Logic
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using Microsoft.Psi;
    using SAAC.Logigram1;

    /// <summary>
    /// <c>Logigram1.FirstUnit</c> spanning Logigramme1.md nodes 1–2 (generation-success / proactive-close).
    /// Derives node 1 from session-new <c>Module status</c> ids; derives node 2 only after node 1 when
    /// door-closed arms a 2s window and SelectModule / Validation fires inside it; on each node-2 fire
    /// emits Battery-scoped Gamma for <see cref="LogigramOutcomeWriter.RegulationDecisionWithBatteryIn"/>.
    /// Does not invent Outcome — writer remains sole Outcome producer. No node 3 / Alpha / Beta / gaze / speech.
    /// </summary>
    public class Logigram1FirstUnit
    {
        /// <summary>Canonical enabled-unit id (matches Analysis registry / config).</summary>
        public const string UnitId = "Logigram1.FirstUnit";

        /// <summary>Door-closed → Select/Validation timing window (Logigramme1.md node 2).</summary>
        public static readonly TimeSpan Node2SelectValidationWindow = TimeSpan.FromSeconds(2);

        private readonly Pipeline pipeline;
        private readonly string name;
        private readonly IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> batteryScopes;
        private readonly HashSet<int> seenModuleIds = new HashSet<int>();
        private int topicInputCount;
        private bool node1HasFired;
        private DateTime? node2ArmStart;
        private string? pendingNode2Topic;
        private string? pendingNode2Detail;
        private DateTime? pendingNode2Time;
        private DateTime lastStateOriginatingTime = DateTime.MinValue;
        private DateTime lastEvidenceOriginatingTime = DateTime.MinValue;
        private DateTime lastRegulationOriginatingTime = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="Logigram1FirstUnit"/> class.
        /// </summary>
        /// <param name="pipeline">Pipeline hosting the component.</param>
        /// <param name="batteryScopes">
        /// Closed Spawn→Finish segments for BatteryId stamping. Empty → every hit is unscoped (BatteryId 0).
        /// </param>
        /// <param name="name">Name of the component, prefix of its streams.</param>
        public Logigram1FirstUnit(
            Pipeline pipeline,
            IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> batteryScopes,
            string name = nameof(Logigram1FirstUnit))
        {
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            this.name = name;
            this.batteryScopes = batteryScopes ?? Array.Empty<(int, DateTime, DateTime)>();

            this.StateOut = pipeline.CreateEmitter<LogigramState>(this, $"{name}-State");
            this.EvidenceOut = pipeline.CreateEmitter<LogigramEvidence>(this, $"{name}-Evidence");
            this.RegulationDecisionWithBatteryOut = pipeline.CreateEmitter<(LogigramRegulation Regulation, int BatteryId)>(
                this,
                $"{name}-RegulationDecisionWithBattery");
        }

        /// <summary>
        /// Gets the Battery-scoped progress State emitter (AD-11). Persist as <see cref="LogigramState.StreamName"/>.
        /// </summary>
        public Emitter<LogigramState> StateOut { get; }

        /// <summary>
        /// Gets the path/node evidence emitter. Pipe to <see cref="LogigramOutcomeWriter.EvidenceIn"/>;
        /// never write <see cref="LogigramOutcome.StreamName"/> from this unit.
        /// </summary>
        public Emitter<LogigramEvidence> EvidenceOut { get; }

        /// <summary>
        /// Gets Battery-scoped regulation decisions for the designated writer.
        /// On each node-2 fire posts <see cref="LogigramRegulation.Gamma"/> with BatteryId;
        /// pipe to <see cref="LogigramOutcomeWriter.RegulationDecisionWithBatteryIn"/>.
        /// </summary>
        public Emitter<(LogigramRegulation Regulation, int BatteryId)> RegulationDecisionWithBatteryOut { get; }

        /// <summary>Gets how many topic hits were observed (any input).</summary>
        public long TopicHitCount { get; private set; }

        /// <summary>Gets how many State messages were emitted.</summary>
        public long StateEmitCount { get; private set; }

        /// <summary>Gets how many Evidence messages were emitted.</summary>
        public long EvidenceEmitCount { get; private set; }

        /// <summary>Gets how many derived node-1 events were emitted.</summary>
        public long Node1FireCount { get; private set; }

        /// <summary>Gets how many derived node-2 events were emitted.</summary>
        public long Node2FireCount { get; private set; }

        /// <summary>Gets how many Gamma decisions were posted for the writer.</summary>
        public long GammaDecisionEmitCount { get; private set; }

        /// <summary>
        /// Maps an AD-7 topic to Logigramme1.md node 1 or 2 (source-topic documentation).
        /// </summary>
        /// <param name="topicName">Exact AD-7 topic name.</param>
        /// <returns><c>1</c> for Module status; <c>2</c> for door / select / validation topics.</returns>
        public static string NodeLabelForTopic(string topicName)
        {
            if (string.Equals(topicName, "Module status", StringComparison.Ordinal))
            {
                return "1";
            }

            return "2";
        }

        /// <summary>
        /// Adds a typed receiver for one AD-7 topic. Payload fields drive node 1–2 derivation.
        /// Call before the pipeline runs.
        /// </summary>
        /// <typeparam name="T">Catalogue payload type for the topic stream.</typeparam>
        /// <param name="topicName">Exact AD-7 topic / stream name.</param>
        /// <returns>The new topic receiver.</returns>
        public Receiver<T> AddTopicInput<T>(string topicName)
        {
            if (string.IsNullOrEmpty(topicName))
            {
                throw new ArgumentException("Topic name is required.", nameof(topicName));
            }

            int id = this.topicInputCount++;
            return this.pipeline.CreateReceiver<T>(
                this,
                (payload, envelope) => this.OnTopicPayload(topicName, payload, envelope.OriginatingTime),
                $"{this.name}-Topic-{id}-{SanitizeReceiverName(topicName)}");
        }

        /// <summary>
        /// Resolves BatteryId for an originating time against closed Spawn→Finish scopes.
        /// Unscoped → 0 (never invents ids).
        /// </summary>
        /// <param name="originatingTime">Message originating time.</param>
        /// <returns>Matching segment BatteryId, or 0 when outside all scopes.</returns>
        public int ResolveBatteryId(DateTime originatingTime)
        {
            foreach ((int batteryId, DateTime start, DateTime end) in this.batteryScopes)
            {
                if (originatingTime >= start && originatingTime <= end)
                {
                    return batteryId;
                }
            }

            return 0;
        }

        private static DateTime EnsureStrictlyIncreasing(DateTime candidate, ref DateTime lastPosted)
        {
            DateTime time = candidate;
            if (time <= lastPosted)
            {
                time = lastPosted.AddTicks(1);
            }

            lastPosted = time;
            return time;
        }

        private static string SanitizeReceiverName(string topicName)
        {
            char[] chars = topicName.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private void OnTopicPayload<T>(string topicName, T payload, DateTime originatingTime)
        {
            this.TopicHitCount++;

            switch (topicName)
            {
                case "Module status":
                    this.OnModuleStatus((ValueTuple<int, string>)(object)payload!, originatingTime);
                    break;

                case "Porte1 ouverture":
                case "Porte2 ouverture":
                    this.OnDoor((ValueTuple<bool, Vector3>)(object)payload!, topicName, originatingTime);
                    break;

                case "M1-SelectModule":
                case "M2-SelectModule":
                    this.OnSelectModule((string)(object)payload!, topicName, originatingTime);
                    break;

                case "M1-Validation":
                case "M2-Validation":
                    this.OnValidation((bool)(object)payload!, topicName, originatingTime);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported FirstUnit topic \"{topicName}\".");
            }
        }

        private void OnModuleStatus(ValueTuple<int, string> moduleStatus, DateTime originatingTime)
        {
            int moduleId = moduleStatus.Item1;
            if (!this.seenModuleIds.Add(moduleId))
            {
                // SEEN_SCOPE B: id already seen this session (any Battery) — no node-1 fire.
                return;
            }

            this.node1HasFired = true;
            this.Node1FireCount++;
            this.EmitNodeFire(
                status: $"node-1: Module status id={moduleId} (new this session)",
                evidenceId: $"node-1:Module status:id={moduleId}",
                originatingTime: originatingTime);

            // Select/Validation may have arrived before node 1; retry pending once node 1 is live.
            this.TryFlushPendingNode2Trigger();
        }

        private void OnDoor(ValueTuple<bool, Vector3> door, string topicName, DateTime originatingTime)
        {
            bool doorOpen = door.Item1;
            if (doorOpen)
            {
                // Open does not arm and clears any armed window (no node-2 from an open door).
                this.node2ArmStart = null;
                return;
            }

            // Door closed (first bool false) arms / re-arms the 2s Select/Validation window.
            this.node2ArmStart = originatingTime;

            // Select/Validation may have been delivered before this door message despite a later
            // originating time (multi-source replay). Apply a pending trigger if it falls in-window.
            this.TryFlushPendingNode2Trigger();
        }

        private void OnSelectModule(string selection, string topicName, DateTime originatingTime)
        {
            if (string.IsNullOrWhiteSpace(selection))
            {
                return;
            }

            this.TryFireNode2(
                topicName,
                triggerDetail: $"SelectModule=\"{selection}\"",
                originatingTime);
        }

        private void OnValidation(bool validationPressed, string topicName, DateTime originatingTime)
        {
            if (!validationPressed)
            {
                return;
            }

            this.TryFireNode2(
                topicName,
                triggerDetail: "Validation=true",
                originatingTime);
        }

        private void TryFireNode2(string topicName, string triggerDetail, DateTime originatingTime)
        {
            // Buffer even before node 1 so a later node-1 / door-close can flush in-window.
            if (!this.node1HasFired || !this.node2ArmStart.HasValue)
            {
                this.StorePendingNode2TriggerIfEmpty(topicName, triggerDetail, originatingTime);
                return;
            }

            DateTime armStart = this.node2ArmStart.Value;
            if (originatingTime < armStart)
            {
                return;
            }

            if (originatingTime - armStart > Node2SelectValidationWindow)
            {
                // Late: window expired; clear stale arm so a later close can re-arm cleanly.
                this.node2ArmStart = null;
                this.ClearPendingNode2Trigger();
                return;
            }

            // Consume the arm so Select+Validation in the same window yield one node-2 (soit/soit).
            this.node2ArmStart = null;
            this.ClearPendingNode2Trigger();
            this.Node2FireCount++;

            int batteryId = this.EmitNodeFire(
                status: $"node-2: door-closed + {triggerDetail} (within 2s; source {topicName})",
                evidenceId: $"node-2:{topicName}:{triggerDetail}",
                originatingTime: originatingTime);

            DateTime regulationTime = EnsureStrictlyIncreasing(
                originatingTime,
                ref this.lastRegulationOriginatingTime);
            this.RegulationDecisionWithBatteryOut.Post(
                (LogigramRegulation.Gamma, batteryId),
                regulationTime);
            this.GammaDecisionEmitCount++;
        }

        private void TryFlushPendingNode2Trigger()
        {
            if (!this.pendingNode2Time.HasValue
                || this.pendingNode2Topic == null
                || this.pendingNode2Detail == null)
            {
                return;
            }

            DateTime pendingTime = this.pendingNode2Time.Value;
            string pendingTopic = this.pendingNode2Topic;
            string pendingDetail = this.pendingNode2Detail;
            this.ClearPendingNode2Trigger();
            this.TryFireNode2(pendingTopic, pendingDetail, pendingTime);
        }

        private void StorePendingNode2TriggerIfEmpty(
            string topicName,
            string triggerDetail,
            DateTime originatingTime)
        {
            // Keep the first pending trigger; do not overwrite with a later Select/Validation.
            if (this.pendingNode2Time.HasValue)
            {
                return;
            }

            this.pendingNode2Topic = topicName;
            this.pendingNode2Detail = triggerDetail;
            this.pendingNode2Time = originatingTime;
        }

        private void ClearPendingNode2Trigger()
        {
            this.pendingNode2Topic = null;
            this.pendingNode2Detail = null;
            this.pendingNode2Time = null;
        }

        private int EmitNodeFire(
            string status,
            string evidenceId,
            DateTime originatingTime)
        {
            int batteryId = this.ResolveBatteryId(originatingTime);

            DateTime stateTime = EnsureStrictlyIncreasing(originatingTime, ref this.lastStateOriginatingTime);
            DateTime evidenceTime = EnsureStrictlyIncreasing(originatingTime, ref this.lastEvidenceOriginatingTime);

            this.StateOut.Post(
                new LogigramState
                {
                    Status = status,
                    BatteryId = batteryId,
                },
                stateTime);
            this.StateEmitCount++;

            this.EvidenceOut.Post(
                new LogigramEvidence
                {
                    NodeOrPathId = evidenceId,
                    BatteryId = batteryId,
                },
                evidenceTime);
            this.EvidenceEmitCount++;

            return batteryId;
        }
    }
}
