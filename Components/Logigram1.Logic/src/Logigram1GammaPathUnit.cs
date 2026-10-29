// <copyright file="Logigram1GammaPathUnit.cs" company="SAAC">
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
    /// <c>Logigram1.GammaPath</c> — full Gamma-path cycle FSM (nodes 1→2→3→C) per (Battery, Person).
    /// Node 1 (Battery-new <c>Module status</c> id) fans out to PersonId 1 and 2, each opening an
    /// independent 2s node-2 window. Side-keyed Select/Validation/Area/door/hand events advance only
    /// the matching person. Node 2 → cycle reset, <b>no</b> Gamma. After unused window, leave of
    /// <c>GeneratorArea</c> with door closed or hand-near Generateur AABB → Gamma (C) via the writer.
    /// No Alpha/Beta/gaze/speech. <c>M1-ModuleOut</c> / <c>M2-ModuleOut</c> are not consumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Input contract: topic payloads must reach the unit in non-decreasing originating-time order across
    /// <b>all</b> topics (Analysis feeds <see cref="AddOrderedTopicInput"/> from an OT-sorted sequence).
    /// Per-topic importer receivers replay each stream in file order, which can interleave streams far out of
    /// originating-time order; the unit is causal (State/Evidence/Outcome emitters need increasing times), so
    /// out-of-order delivery would relocate States and could let a leave "complete" a cycle whose node 1 comes
    /// <i>later</i> in originating time. <see cref="OutOfOrderInputCount"/> reports contract violations.
    /// </para>
    /// <para>
    /// Honest scrub: node-3 only fires for a leave that is strictly after the node-1 create + <see cref="Node2Window"/>
    /// of the same (Battery, Person) cycle; the node-3 State and the Gamma Outcome share the leave originating time
    /// (the Outcome is never earlier than its node-3 State).
    /// </para>
    /// </remarks>
    public class Logigram1GammaPathUnit
    {
        /// <summary>Canonical enabled-unit id (matches Analysis registry / config).</summary>
        public const string UnitId = "Logigram1.GammaPath";

        /// <summary>Post–node-1 window for door-closed + Select/Validation together (node 2).</summary>
        public static readonly TimeSpan Node2Window = TimeSpan.FromSeconds(2);

        /// <summary>Hand-near distance threshold (meters) to Generateur AABB.</summary>
        public const float HandNearDistanceMeters = 1f;

        /// <summary>Area <c>info</c> value for the generator zone.</summary>
        public const string GeneratorAreaInfo = "GeneratorArea";

        /// <summary>Player leave actor id on Area streams.</summary>
        public const int PlayerAreaId = -1;

        private enum CyclePhase
        {
            Idle = 0,
            Node2Window = 1,
            Node3Eligible = 2,
        }

        private readonly Pipeline pipeline;
        private readonly string name;
        private readonly IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> batteryScopes;
        private readonly Dictionary<int, HashSet<int>> seenModuleIdsByBattery = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<(int BatteryId, int PersonId), CyclePhase> phaseByCycle =
            new Dictionary<(int, int), CyclePhase>();

        private readonly Dictionary<(int BatteryId, int PersonId), DateTime> node1CreateByCycle =
            new Dictionary<(int, int), DateTime>();

        // Latest door open flag per side (true = open). Absent → not closed.
        private readonly Dictionary<int, bool> doorOpenBySide = new Dictionary<int, bool>();

        // Latest GenerateurN AABB corners.
        private readonly Dictionary<int, (Vector3 A, Vector3 B)> generateurAabbBySide =
            new Dictionary<int, (Vector3, Vector3)>();

        // Latest wrist world positions: key "N-Left" / "N-Right".
        private readonly Dictionary<string, Vector3> wristPositionByKey = new Dictionary<string, Vector3>(StringComparer.Ordinal);

        private int topicInputCount;

        // Pending Select/Validation awaiting door-closed, keyed by (BatteryId, PersonId).
        private readonly Dictionary<(int BatteryId, int PersonId), (string Topic, string Detail, DateTime Time)> pendingNode2ByCycle =
            new Dictionary<(int, int), (string, string, DateTime)>();

        private DateTime lastStateOriginatingTime = DateTime.MinValue;
        private DateTime lastEvidenceOriginatingTime = DateTime.MinValue;
        private DateTime lastRegulationOriginatingTime = DateTime.MinValue;
        private DateTime lastInputOriginatingTime = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="Logigram1GammaPathUnit"/> class.
        /// </summary>
        /// <param name="pipeline">Pipeline hosting the component.</param>
        /// <param name="batteryScopes">
        /// Closed Spawn→Finish segments for BatteryId stamping. Empty → every hit is unscoped (BatteryId 0).
        /// </param>
        /// <param name="name">Name of the component, prefix of its streams.</param>
        public Logigram1GammaPathUnit(
            Pipeline pipeline,
            IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> batteryScopes,
            string name = nameof(Logigram1GammaPathUnit))
        {
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            this.name = name;
            this.batteryScopes = batteryScopes ?? Array.Empty<(int, DateTime, DateTime)>();

            this.StateOut = pipeline.CreateEmitter<LogigramState>(this, $"{name}-State");
            this.EvidenceOut = pipeline.CreateEmitter<LogigramEvidence>(this, $"{name}-Evidence");
            this.RegulationDecisionWithBatteryOut = pipeline.CreateEmitter<(LogigramRegulation Regulation, int BatteryId, int PersonId)>(
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
        /// Gets Battery+Person-scoped regulation decisions for the designated writer.
        /// Posts <see cref="LogigramRegulation.Gamma"/> only on node-3 (C); pipe to
        /// <see cref="LogigramOutcomeWriter.RegulationDecisionWithBatteryIn"/>.
        /// </summary>
        public Emitter<(LogigramRegulation Regulation, int BatteryId, int PersonId)> RegulationDecisionWithBatteryOut { get; }

        /// <summary>Gets how many topic hits were observed (any input).</summary>
        public long TopicHitCount { get; private set; }

        /// <summary>
        /// Gets how many topic hits arrived with an originating time earlier than a previously seen hit
        /// (input-order contract violation; always 0 when fed through <see cref="AddOrderedTopicInput"/>).
        /// </summary>
        public long OutOfOrderInputCount { get; private set; }

        /// <summary>Gets how many State messages were emitted.</summary>
        public long StateEmitCount { get; private set; }

        /// <summary>Gets how many Evidence messages were emitted.</summary>
        public long EvidenceEmitCount { get; private set; }

        /// <summary>Gets how many derived node-1 events were emitted (one per person on fan-out).</summary>
        public long Node1FireCount { get; private set; }

        /// <summary>Gets how many derived node-2 events were emitted.</summary>
        public long Node2FireCount { get; private set; }

        /// <summary>Gets how many derived node-3 events were emitted.</summary>
        public long Node3FireCount { get; private set; }

        /// <summary>Gets how many Gamma decisions were posted for the writer.</summary>
        public long GammaDecisionEmitCount { get; private set; }

        /// <summary>
        /// Maps an AD-7 topic to Logigramme1.md node label (source-topic documentation).
        /// </summary>
        /// <param name="topicName">Exact AD-7 topic name.</param>
        /// <returns><c>1</c>, <c>2</c>, or <c>3</c>.</returns>
        public static string NodeLabelForTopic(string topicName)
        {
            if (string.Equals(topicName, "Module status", StringComparison.Ordinal))
            {
                return "1";
            }

            if (string.Equals(topicName, "Area1", StringComparison.Ordinal)
                || string.Equals(topicName, "Area2", StringComparison.Ordinal)
                || string.Equals(topicName, "ObjectBound", StringComparison.Ordinal)
                || topicName.EndsWith("Wrist", StringComparison.Ordinal))
            {
                return "3";
            }

            return "2";
        }

        /// <summary>
        /// Minimum distance from a point to an AABB defined by two opposite corners.
        /// </summary>
        /// <param name="point">World-space point.</param>
        /// <param name="cornerA">One AABB corner.</param>
        /// <param name="cornerB">Opposite AABB corner.</param>
        /// <returns>Distance in meters (0 when inside or on the box).</returns>
        public static float DistancePointToAabb(Vector3 point, Vector3 cornerA, Vector3 cornerB)
        {
            Vector3 min = Vector3.Min(cornerA, cornerB);
            Vector3 max = Vector3.Max(cornerA, cornerB);
            Vector3 closest = Vector3.Clamp(point, min, max);
            return Vector3.Distance(point, closest);
        }

        /// <summary>
        /// Adds a typed receiver for one AD-7 topic. Call before the pipeline runs.
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
        /// Adds one receiver for the globally originating-time-ordered topic sequence. The caller must deliver
        /// events in non-decreasing originating time across all topics (see class remarks). Call before the pipeline runs.
        /// </summary>
        /// <returns>The ordered topic receiver.</returns>
        public Receiver<GammaPathTopicEvent> AddOrderedTopicInput()
        {
            int id = this.topicInputCount++;
            return this.pipeline.CreateReceiver<GammaPathTopicEvent>(
                this,
                (topicEvent, envelope) => this.OnTopicPayload<object>(
                    topicEvent.TopicName,
                    topicEvent.Payload,
                    envelope.OriginatingTime),
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

        private static int? TryParseDoorSide(string topicName)
        {
            if (string.Equals(topicName, "Porte1 ouverture", StringComparison.Ordinal))
            {
                return 1;
            }

            if (string.Equals(topicName, "Porte2 ouverture", StringComparison.Ordinal))
            {
                return 2;
            }

            return null;
        }

        private static int? TryParseAreaSide(string topicName)
        {
            if (string.Equals(topicName, "Area1", StringComparison.Ordinal))
            {
                return 1;
            }

            if (string.Equals(topicName, "Area2", StringComparison.Ordinal))
            {
                return 2;
            }

            return null;
        }

        private static bool TryParseWristKey(string topicName, out string wristKey)
        {
            wristKey = string.Empty;

            if (string.Equals(topicName, "1-LeftWrist", StringComparison.Ordinal))
            {
                wristKey = "1-Left";
                return true;
            }

            if (string.Equals(topicName, "1-RightWrist", StringComparison.Ordinal))
            {
                wristKey = "1-Right";
                return true;
            }

            if (string.Equals(topicName, "2-LeftWrist", StringComparison.Ordinal))
            {
                wristKey = "2-Left";
                return true;
            }

            if (string.Equals(topicName, "2-RightWrist", StringComparison.Ordinal))
            {
                wristKey = "2-Right";
                return true;
            }

            return false;
        }

        private static int? TryParseGenerateurSide(string objectName)
        {
            if (string.Equals(objectName, "Generateur1", StringComparison.Ordinal))
            {
                return 1;
            }

            if (string.Equals(objectName, "Generateur2", StringComparison.Ordinal))
            {
                return 2;
            }

            return null;
        }

        private static int? TryParseSelectValidationSide(string topicName)
        {
            if (string.Equals(topicName, "M1-SelectModule", StringComparison.Ordinal)
                || string.Equals(topicName, "M1-Validation", StringComparison.Ordinal))
            {
                return 1;
            }

            if (string.Equals(topicName, "M2-SelectModule", StringComparison.Ordinal)
                || string.Equals(topicName, "M2-Validation", StringComparison.Ordinal))
            {
                return 2;
            }

            return null;
        }

        private void OnTopicPayload<T>(string topicName, T payload, DateTime originatingTime)
        {
            this.TopicHitCount++;

            if (originatingTime < this.lastInputOriginatingTime)
            {
                this.OutOfOrderInputCount++;
            }
            else
            {
                this.lastInputOriginatingTime = originatingTime;
            }

            switch (topicName)
            {
                case "Module status":
                    this.OnModuleStatus((ValueTuple<int, string>)(object)payload!, originatingTime);
                    return;

                case "Porte1 ouverture":
                case "Porte2 ouverture":
                    this.OnDoor((ValueTuple<bool, Vector3>)(object)payload!, topicName, originatingTime);
                    return;

                case "M1-SelectModule":
                case "M2-SelectModule":
                    this.OnSelectModule((string)(object)payload!, topicName, originatingTime);
                    return;

                case "M1-Validation":
                case "M2-Validation":
                    this.OnValidation((bool)(object)payload!, topicName, originatingTime);
                    return;

                case "Area1":
                case "Area2":
                    this.OnArea(
                        (ValueTuple<int, bool, string>)(object)payload!,
                        TryParseAreaSide(topicName)!.Value,
                        originatingTime);
                    return;

                case "ObjectBound":
                    this.OnObjectBound((ValueTuple<string, Vector3, Vector3>)(object)payload!);
                    return;

                case "1-LeftWrist":
                case "1-RightWrist":
                case "2-LeftWrist":
                case "2-RightWrist":
                    if (TryParseWristKey(topicName, out string wristKey))
                    {
                        this.OnWrist(payload!, wristKey);
                    }

                    return;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported GammaPath topic \"{topicName}\".");
            }
        }

        private void OnModuleStatus(ValueTuple<int, string> moduleStatus, DateTime originatingTime)
        {
            int moduleId = moduleStatus.Item1;
            int batteryId = this.ResolveBatteryId(originatingTime);
            HashSet<int> seenInBattery = this.GetOrCreateSeenModules(batteryId);
            if (!seenInBattery.Add(moduleId))
            {
                return;
            }

            // FANOUT_BOTH: new module id starts (or restarts) a node-1 cycle on both persons.
            // Dedupe of seen module ids remains once per Battery.

            for (int personId = 1; personId <= 2; personId++)
            {
                (int BatteryId, int PersonId) key = (batteryId, personId);
                this.phaseByCycle[key] = CyclePhase.Node2Window;
                this.node1CreateByCycle[key] = originatingTime;

                this.Node1FireCount++;
                this.EmitNodeFire(
                    status: $"node-1: Module status id={moduleId} (new this Battery)",
                    evidenceId: $"node-1:Module status:id={moduleId}",
                    originatingTime: originatingTime,
                    personId: personId);

                this.TryFlushPendingNode2Trigger(key);
            }
        }

        private void OnDoor(ValueTuple<bool, Vector3> door, string topicName, DateTime originatingTime)
        {
            int? side = TryParseDoorSide(topicName);
            if (!side.HasValue)
            {
                return;
            }

            this.doorOpenBySide[side.Value] = door.Item1;
            int batteryId = this.ResolveBatteryId(originatingTime);
            // Door is side-keyed; advance only the matching person's cycle phase.
            this.AdvancePhaseIfNeeded((batteryId, side.Value), originatingTime);
            this.TryFlushAllPendingNode2Triggers();
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

        private void OnObjectBound(ValueTuple<string, Vector3, Vector3> bound)
        {
            int? side = TryParseGenerateurSide(bound.Item1);
            if (!side.HasValue)
            {
                return;
            }

            this.generateurAabbBySide[side.Value] = (bound.Item2, bound.Item3);
        }

        private void OnWrist<T>(T payload, string wristKey)
        {
            Vector3 position;
            if (payload is Tuple<Vector3, Vector3> classic)
            {
                position = classic.Item1;
            }
            else if (payload is ValueTuple<Vector3, Vector3> valueTuple)
            {
                position = valueTuple.Item1;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Wrist payload must be Tuple/ValueTuple of (Vector3, Vector3); got {typeof(T).FullName}.");
            }

            this.wristPositionByKey[wristKey] = position;
        }

        private void OnArea(ValueTuple<int, bool, string> area, int side, DateTime originatingTime)
        {
            int id = area.Item1;
            bool entered = area.Item2;
            string info = area.Item3;

            if (id != PlayerAreaId || entered
                || !string.Equals(info, GeneratorAreaInfo, StringComparison.Ordinal))
            {
                return;
            }

            int batteryId = this.ResolveBatteryId(originatingTime);
            int personId = side;
            (int BatteryId, int PersonId) key = (batteryId, personId);
            this.AdvancePhaseIfNeeded(key, originatingTime);

            // Node 3 only after unused post–node-1 2s window (active cycle, not reset via node 2).
            if (!this.phaseByCycle.TryGetValue(key, out CyclePhase phase)
                || phase != CyclePhase.Node3Eligible)
            {
                return;
            }

            // Scrub honesty: the leave must come strictly after this cycle's node-1 + 2s window in
            // originating time. A leave at or before node 1 (e.g. out-of-order delivery) never completes it.
            if (!this.node1CreateByCycle.TryGetValue(key, out DateTime node1Time)
                || originatingTime - node1Time <= Node2Window)
            {
                return;
            }

            bool doorClosed = this.IsDoorClosed(side);
            bool handNear = this.IsHandNearGenerateur(side);
            if (!doorClosed && !handNear)
            {
                return;
            }

            string branch = doorClosed && handNear
                ? "door-closed|hand-near"
                : doorClosed
                    ? "door-closed"
                    : "hand-near";

            this.Node3FireCount++;
            DateTime node3StateTime = this.EmitNodeFire(
                status: $"node-3: {branch} + leave {GeneratorAreaInfo} (Area{side})",
                evidenceId: $"node-3:Area{side}:{branch}",
                originatingTime: originatingTime,
                personId: personId);

            // Cycle completes at C — reset so a later leave cannot re-fire without a new node 1.
            this.ResetCycle(key);

            // Outcome never precedes its node-3 State (same leave OT unless a tie bumped the State by ticks).
            DateTime regulationTime = EnsureStrictlyIncreasing(
                node3StateTime > originatingTime ? node3StateTime : originatingTime,
                ref this.lastRegulationOriginatingTime);
            this.RegulationDecisionWithBatteryOut.Post(
                (LogigramRegulation.Gamma, batteryId, personId),
                regulationTime);
            this.GammaDecisionEmitCount++;
        }

        private void TryFireNode2(string topicName, string triggerDetail, DateTime originatingTime)
        {
            int? side = TryParseSelectValidationSide(topicName);
            if (!side.HasValue)
            {
                return;
            }

            int personId = side.Value;
            int batteryId = this.ResolveBatteryId(originatingTime);
            (int BatteryId, int PersonId) key = (batteryId, personId);
            this.AdvancePhaseIfNeeded(key, originatingTime);

            if (!this.phaseByCycle.TryGetValue(key, out CyclePhase phase))
            {
                // No cycle yet — hold Select until a later node-1 (same-OT flush).
                this.StorePendingNode2TriggerIfEmpty(key, topicName, triggerDetail, originatingTime);
                return;
            }

            if (phase == CyclePhase.Node3Eligible)
            {
                // Past the create window — never arm node-2 from a late Select/Validation.
                this.ClearPendingNode2Trigger(key);
                return;
            }

            if (phase != CyclePhase.Node2Window
                || !this.node1CreateByCycle.TryGetValue(key, out DateTime createTime))
            {
                if (phase == CyclePhase.Idle)
                {
                    this.StorePendingNode2TriggerIfEmpty(key, topicName, triggerDetail, originatingTime);
                }

                return;
            }

            if (originatingTime < createTime)
            {
                return;
            }

            if (originatingTime - createTime > Node2Window)
            {
                this.phaseByCycle[key] = CyclePhase.Node3Eligible;
                this.ClearPendingNode2Trigger(key);
                return;
            }

            if (!this.IsDoorClosed(side.Value))
            {
                // Select/Validation arrived but paired door not yet closed — hold for a later door sample.
                this.StorePendingNode2TriggerIfEmpty(key, topicName, triggerDetail, originatingTime);
                return;
            }

            // Door closed + Select/Validation together in the create window → reset, no Gamma.
            this.ClearPendingNode2Trigger(key);
            this.ResetCycle(key);
            this.Node2FireCount++;

            this.EmitNodeFire(
                status: $"node-2: door-closed + {triggerDetail} (within 2s of module create; source {topicName})",
                evidenceId: $"node-2:{topicName}:{triggerDetail}",
                originatingTime: originatingTime,
                personId: personId);
        }

        private void AdvancePhaseIfNeeded((int BatteryId, int PersonId) key, DateTime originatingTime)
        {
            if (!this.phaseByCycle.TryGetValue(key, out CyclePhase phase)
                || phase != CyclePhase.Node2Window
                || !this.node1CreateByCycle.TryGetValue(key, out DateTime createTime))
            {
                return;
            }

            if (originatingTime - createTime > Node2Window)
            {
                this.phaseByCycle[key] = CyclePhase.Node3Eligible;
            }
        }

        private void ResetCycle((int BatteryId, int PersonId) key)
        {
            this.phaseByCycle[key] = CyclePhase.Idle;
            this.node1CreateByCycle.Remove(key);
            this.ClearPendingNode2Trigger(key);
        }

        private bool IsDoorClosed(int side)
        {
            return this.doorOpenBySide.TryGetValue(side, out bool open) && !open;
        }

        private bool IsHandNearGenerateur(int side)
        {
            if (!this.generateurAabbBySide.TryGetValue(side, out (Vector3 A, Vector3 B) aabb))
            {
                return false;
            }

            string leftKey = $"{side}-Left";
            string rightKey = $"{side}-Right";
            float best = float.PositiveInfinity;

            if (this.wristPositionByKey.TryGetValue(leftKey, out Vector3 left))
            {
                best = Math.Min(best, DistancePointToAabb(left, aabb.A, aabb.B));
            }

            if (this.wristPositionByKey.TryGetValue(rightKey, out Vector3 right))
            {
                best = Math.Min(best, DistancePointToAabb(right, aabb.A, aabb.B));
            }

            return best <= HandNearDistanceMeters;
        }

        private HashSet<int> GetOrCreateSeenModules(int batteryId)
        {
            if (!this.seenModuleIdsByBattery.TryGetValue(batteryId, out HashSet<int>? seen))
            {
                seen = new HashSet<int>();
                this.seenModuleIdsByBattery[batteryId] = seen;
            }

            return seen;
        }

        private void TryFlushAllPendingNode2Triggers()
        {
            if (this.pendingNode2ByCycle.Count == 0)
            {
                return;
            }

            var keys = new List<(int BatteryId, int PersonId)>(this.pendingNode2ByCycle.Keys);
            foreach ((int BatteryId, int PersonId) key in keys)
            {
                this.TryFlushPendingNode2Trigger(key);
            }
        }

        private void TryFlushPendingNode2Trigger((int BatteryId, int PersonId) key)
        {
            if (!this.pendingNode2ByCycle.TryGetValue(key, out (string Topic, string Detail, DateTime Time) pending))
            {
                return;
            }

            this.ClearPendingNode2Trigger(key);
            this.TryFireNode2(pending.Topic, pending.Detail, pending.Time);
        }

        private void StorePendingNode2TriggerIfEmpty(
            (int BatteryId, int PersonId) key,
            string topicName,
            string triggerDetail,
            DateTime originatingTime)
        {
            if (this.pendingNode2ByCycle.ContainsKey(key))
            {
                return;
            }

            this.pendingNode2ByCycle[key] = (topicName, triggerDetail, originatingTime);
        }

        private void ClearPendingNode2Trigger((int BatteryId, int PersonId) key)
        {
            this.pendingNode2ByCycle.Remove(key);
        }

        private DateTime EmitNodeFire(string status, string evidenceId, DateTime originatingTime, int personId)
        {
            int batteryId = this.ResolveBatteryId(originatingTime);

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
