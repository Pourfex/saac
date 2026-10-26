// <copyright file="Logigram1Node3Unit.cs" company="SAAC">
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
    /// <c>Logigram1.Node3</c> — Logigramme1.md node 3 (door-closed leave-zone / hand-near-door → Gamma C).
    /// Independently enableable; does not require FirstUnit, Alpha/Beta, gaze, or speech.
    /// At player leave of <c>GeneratorArea</c> on AreaN, fires when PorteN is closed <b>or</b> a player-N
    /// wrist is within 1 m of <c>Object Bound</c> GenerateurN AABB. Emits Battery-scoped State/Evidence
    /// and pipes Gamma to <see cref="LogigramOutcomeWriter.RegulationDecisionWithBatteryIn"/>.
    /// </summary>
    public class Logigram1Node3Unit
    {
        /// <summary>Canonical enabled-unit id (matches Analysis registry / config).</summary>
        public const string UnitId = "Logigram1.Node3";

        /// <summary>Hand-near distance threshold (meters) to Generateur AABB.</summary>
        public const float HandNearDistanceMeters = 1f;

        /// <summary>Area <c>info</c> value for the generator zone.</summary>
        public const string GeneratorAreaInfo = "GeneratorArea";

        /// <summary>Player leave actor id on Area streams.</summary>
        public const int PlayerAreaId = -1;

        private readonly Pipeline pipeline;
        private readonly string name;
        private readonly IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> batteryScopes;

        // Latest door open flag per side (true = open). Absent → not closed.
        private readonly Dictionary<int, bool> doorOpenBySide = new Dictionary<int, bool>();

        // Latest GenerateurN AABB corners (min/max not normalized until distance check).
        private readonly Dictionary<int, (Vector3 A, Vector3 B)> generateurAabbBySide =
            new Dictionary<int, (Vector3, Vector3)>();

        // Latest wrist world positions: key "N-Left" / "N-Right".
        private readonly Dictionary<string, Vector3> wristPositionByKey = new Dictionary<string, Vector3>(StringComparer.Ordinal);

        private int topicInputCount;
        private DateTime lastStateOriginatingTime = DateTime.MinValue;
        private DateTime lastEvidenceOriginatingTime = DateTime.MinValue;
        private DateTime lastRegulationOriginatingTime = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="Logigram1Node3Unit"/> class.
        /// </summary>
        /// <param name="pipeline">Pipeline hosting the component.</param>
        /// <param name="batteryScopes">
        /// Closed Spawn→Finish segments for BatteryId stamping. Empty → every hit is unscoped (BatteryId 0).
        /// </param>
        /// <param name="name">Name of the component, prefix of its streams.</param>
        public Logigram1Node3Unit(
            Pipeline pipeline,
            IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> batteryScopes,
            string name = nameof(Logigram1Node3Unit))
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
        /// On each node-3 fire posts <see cref="LogigramRegulation.Gamma"/> with BatteryId;
        /// pipe to <see cref="LogigramOutcomeWriter.RegulationDecisionWithBatteryIn"/>.
        /// </summary>
        public Emitter<(LogigramRegulation Regulation, int BatteryId)> RegulationDecisionWithBatteryOut { get; }

        /// <summary>Gets how many topic hits were observed (any input).</summary>
        public long TopicHitCount { get; private set; }

        /// <summary>Gets how many State messages were emitted.</summary>
        public long StateEmitCount { get; private set; }

        /// <summary>Gets how many Evidence messages were emitted.</summary>
        public long EvidenceEmitCount { get; private set; }

        /// <summary>Gets how many derived node-3 events were emitted.</summary>
        public long Node3FireCount { get; private set; }

        /// <summary>Gets how many Gamma decisions were posted for the writer.</summary>
        public long GammaDecisionEmitCount { get; private set; }

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
        /// Adds a typed receiver for one Node3 topic. Call before the pipeline runs.
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

        private static bool TryParseWristKey(string topicName, out string wristKey, out int playerSide)
        {
            wristKey = string.Empty;
            playerSide = 0;

            if (string.Equals(topicName, "1-LeftWrist", StringComparison.Ordinal))
            {
                wristKey = "1-Left";
                playerSide = 1;
                return true;
            }

            if (string.Equals(topicName, "1-RightWrist", StringComparison.Ordinal))
            {
                wristKey = "1-Right";
                playerSide = 1;
                return true;
            }

            if (string.Equals(topicName, "2-LeftWrist", StringComparison.Ordinal))
            {
                wristKey = "2-Left";
                playerSide = 2;
                return true;
            }

            if (string.Equals(topicName, "2-RightWrist", StringComparison.Ordinal))
            {
                wristKey = "2-Right";
                playerSide = 2;
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

        private void OnTopicPayload<T>(string topicName, T payload, DateTime originatingTime)
        {
            this.TopicHitCount++;

            int? doorSide = TryParseDoorSide(topicName);
            if (doorSide.HasValue)
            {
                this.OnDoor((ValueTuple<bool, Vector3>)(object)payload!, doorSide.Value);
                return;
            }

            int? areaSide = TryParseAreaSide(topicName);
            if (areaSide.HasValue)
            {
                this.OnArea((ValueTuple<int, bool, string>)(object)payload!, areaSide.Value, originatingTime);
                return;
            }

            if (string.Equals(topicName, "Object Bound", StringComparison.Ordinal))
            {
                this.OnObjectBound((ValueTuple<string, Vector3, Vector3>)(object)payload!);
                return;
            }

            if (TryParseWristKey(topicName, out string wristKey, out _))
            {
                this.OnWrist(payload!, wristKey);
                return;
            }

            throw new InvalidOperationException(
                $"Unsupported Node3 topic \"{topicName}\".");
        }

        private void OnDoor(ValueTuple<bool, Vector3> door, int side)
        {
            // Item1 true = open; closed = false (latest sampled at leave).
            this.doorOpenBySide[side] = door.Item1;
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

            // Player leave of GeneratorArea only (id=-1, state=false, info=GeneratorArea).
            if (id != PlayerAreaId)
            {
                return;
            }

            if (entered)
            {
                return;
            }

            if (!string.Equals(info, GeneratorAreaInfo, StringComparison.Ordinal))
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
            this.EmitNodeFire(
                status: $"node-3: {branch} + leave {GeneratorAreaInfo} (Area{side})",
                evidenceId: $"node-3:Area{side}:{branch}",
                originatingTime: originatingTime);

            int batteryId = this.ResolveBatteryId(originatingTime);
            DateTime regulationTime = EnsureStrictlyIncreasing(
                originatingTime,
                ref this.lastRegulationOriginatingTime);
            this.RegulationDecisionWithBatteryOut.Post(
                (LogigramRegulation.Gamma, batteryId),
                regulationTime);
            this.GammaDecisionEmitCount++;
        }

        private bool IsDoorClosed(int side)
        {
            // Absent door sample → not closed (do not invent closed).
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

        private void EmitNodeFire(string status, string evidenceId, DateTime originatingTime)
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
        }
    }
}
