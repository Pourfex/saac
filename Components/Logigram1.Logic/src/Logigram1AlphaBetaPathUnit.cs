// <copyright file="Logigram1AlphaBetaPathUnit.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1.Logic
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SAAC.Logigram1;

    /// <summary>
    /// <c>Logigram1.AlphaBetaPath</c> — Alpha/Beta path unit (Idle scaffold this story).
    /// Posts stamped <c>ab-idle:</c> State so scrub proves wire+stamp. No Alpha/Beta Outcomes,
    /// no gaze/entry/door/press detectors (stories 4.2–4.11). Independent of
    /// <see cref="Logigram1GammaPathUnit"/>.
    /// </summary>
    public class Logigram1AlphaBetaPathUnit
    {
        /// <summary>Canonical enabled-unit id (matches Analysis registry / config).</summary>
        public const string UnitId = "Logigram1.AlphaBetaPath";

        /// <summary>Status prefix for Idle (AB_LABEL; distinct from Gamma <c>node-N:</c>).</summary>
        public const string IdleStatusPrefix = "ab-idle:";

        /// <summary>Status prefix for Gazed (viz skeleton; FSM in later stories).</summary>
        public const string GazedStatusPrefix = "ab-gazed:";

        /// <summary>Status prefix for Not-gazed (viz skeleton; FSM in later stories).</summary>
        public const string NotGazedStatusPrefix = "ab-not-gazed:";

        private readonly Pipeline pipeline;
        private readonly string name;
        private readonly IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> batteryScopes;

        private int topicInputCount;
        private DateTime lastStateOriginatingTime = DateTime.MinValue;
        private DateTime lastEvidenceOriginatingTime = DateTime.MinValue;
        private bool idleEmitted;

        /// <summary>
        /// Initializes a new instance of the <see cref="Logigram1AlphaBetaPathUnit"/> class.
        /// </summary>
        /// <param name="pipeline">Pipeline hosting the component.</param>
        /// <param name="batteryScopes">
        /// Closed Spawn→Finish segments for BatteryId stamping. Empty → Idle posts with BatteryId 0.
        /// </param>
        /// <param name="name">Name of the component, prefix of its streams.</param>
        public Logigram1AlphaBetaPathUnit(
            Pipeline pipeline,
            IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> batteryScopes,
            string name = nameof(Logigram1AlphaBetaPathUnit))
        {
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            this.name = name;
            this.batteryScopes = batteryScopes ?? Array.Empty<(int, DateTime, DateTime)>();

            this.StateOut = pipeline.CreateEmitter<LogigramState>(this, $"{name}-State");
            this.EvidenceOut = pipeline.CreateEmitter<LogigramEvidence>(this, $"{name}-Evidence");
        }

        /// <summary>
        /// Gets the Battery-scoped progress State emitter (AD-11). Persist as <see cref="LogigramState.StreamName"/>.
        /// </summary>
        public Emitter<LogigramState> StateOut { get; }

        /// <summary>
        /// Gets the path/node evidence emitter (unused for Outcomes this story; reserved for later FSM).
        /// </summary>
        public Emitter<LogigramEvidence> EvidenceOut { get; }

        /// <summary>Gets how many topic hits were observed (any input).</summary>
        public long TopicHitCount { get; private set; }

        /// <summary>Gets how many State messages were emitted.</summary>
        public long StateEmitCount { get; private set; }

        /// <summary>Gets how many Evidence messages were emitted.</summary>
        public long EvidenceEmitCount { get; private set; }

        /// <summary>Gets how many Idle States were posted (person × Battery scopes).</summary>
        public long IdleEmitCount { get; private set; }

        /// <summary>
        /// Adds a receiver that primes Idle State emission once (Analysis sibling runner).
        /// Call before the pipeline runs.
        /// </summary>
        /// <returns>The Idle-prime receiver.</returns>
        public Receiver<bool> AddIdlePrimeInput()
        {
            int id = this.topicInputCount++;
            return this.pipeline.CreateReceiver<bool>(
                this,
                (prime, envelope) =>
                {
                    if (prime)
                    {
                        this.EmitIdleStates(envelope.OriginatingTime);
                    }
                },
                $"{this.name}-IdlePrime-{id}");
        }

        /// <summary>
        /// Adds one receiver for a globally originating-time-ordered topic sequence (entry topics now;
        /// gaze later). Scaffold ignores payloads beyond hit counting — no FSM yet.
        /// </summary>
        /// <returns>The ordered topic receiver.</returns>
        public Receiver<AlphaBetaPathTopicEvent> AddOrderedTopicInput()
        {
            int id = this.topicInputCount++;
            return this.pipeline.CreateReceiver<AlphaBetaPathTopicEvent>(
                this,
                (topicEvent, envelope) =>
                {
                    if (topicEvent == null)
                    {
                        throw new ArgumentNullException(nameof(topicEvent));
                    }

                    this.OnTopicPayload(topicEvent.TopicName, envelope.OriginatingTime);
                },
                $"{this.name}-Ordered-{id}");
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

        private void OnTopicPayload(string topicName, DateTime originatingTime)
        {
            this.TopicHitCount++;

            // ENTRY_KNOWN_NOW topics are accepted for wire readiness; FSM detectors land in 4.2+.
            switch (topicName)
            {
                case "Module status":
                case "Porte1 ouverture":
                case "Porte2 ouverture":
                case "M1-SelectModule":
                case "M2-SelectModule":
                case "M1-Validation":
                case "M2-Validation":
                    return;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported AlphaBetaPath topic \"{topicName}\".");
            }
        }

        /// <summary>
        /// Posts stamped <c>ab-idle:</c> State (+ Evidence) once per PersonId under each Battery scope
        /// (or BatteryId 0 when scopes are empty). Idempotent for a single pipeline run.
        /// </summary>
        private void EmitIdleStates(DateTime fallbackOriginatingTime)
        {
            if (this.idleEmitted)
            {
                return;
            }

            this.idleEmitted = true;

            if (this.batteryScopes.Count == 0)
            {
                this.EmitIdleForBattery(batteryId: 0, fallbackOriginatingTime, personId: 1);
                this.EmitIdleForBattery(
                    batteryId: 0,
                    fallbackOriginatingTime.AddMilliseconds(1),
                    personId: 2);
                return;
            }

            foreach ((int batteryId, DateTime start, DateTime _) in this.batteryScopes)
            {
                DateTime time = start != DateTime.MinValue ? start : fallbackOriginatingTime;
                this.EmitIdleForBattery(batteryId, time, personId: 1);
                this.EmitIdleForBattery(batteryId, time.AddMilliseconds(1), personId: 2);
            }
        }

        private void EmitIdleForBattery(int batteryId, DateTime originatingTime, int personId)
        {
            this.EmitNodeFire(
                status: $"{IdleStatusPrefix} AlphaBetaPath",
                evidenceId: $"{IdleStatusPrefix}AlphaBetaPath",
                originatingTime: originatingTime,
                batteryId: batteryId,
                personId: personId);
            this.IdleEmitCount++;
        }

        private DateTime EmitNodeFire(
            string status,
            string evidenceId,
            DateTime originatingTime,
            int batteryId,
            int personId)
        {
            DateTime stateTime = EnsureStrictlyIncreasing(originatingTime, ref this.lastStateOriginatingTime);
            DateTime evidenceTime = EnsureStrictlyIncreasing(originatingTime, ref this.lastEvidenceOriginatingTime);

            this.StateOut.Post(
                new LogigramState
                {
                    Status = status,
                    BatteryId = batteryId,
                    PersonId = personId,
                },
                stateTime);
            this.StateEmitCount++;

            this.EvidenceOut.Post(
                new LogigramEvidence
                {
                    NodeOrPathId = evidenceId,
                    BatteryId = batteryId,
                    PersonId = personId,
                },
                evidenceTime);
            this.EvidenceEmitCount++;
            return stateTime;
        }
    }
}
