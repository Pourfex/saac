// <copyright file="Logigram1FirstUnit.cs" company="SAAC">
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
    /// Interim stub for <c>Logigram1.FirstUnit</c> spanning Logigramme1.md nodes 1–2 (UNIT_DEPTH B).
    /// Subscribes to the seven AD-7 topics; on each hit emits Battery-scoped
    /// <see cref="LogigramState"/> and <see cref="LogigramEvidence"/> labeled as node 1 or 2.
    /// Does <b>not</b> apply ModuleStatus “new module” derivation or door/button 2s timing (Epic 3).
    /// Does <b>not</b> invent Alpha|Beta|Gamma — evidence only; Outcome remains writer-owned.
    /// </summary>
    public class Logigram1FirstUnit
    {
        /// <summary>Canonical enabled-unit id (matches Analysis registry / config).</summary>
        public const string UnitId = "Logigram1.FirstUnit";

        private readonly Pipeline pipeline;
        private readonly string name;
        private readonly IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> batteryScopes;
        private int topicInputCount;
        private DateTime lastStateOriginatingTime = DateTime.MinValue;
        private DateTime lastEvidenceOriginatingTime = DateTime.MinValue;

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

        /// <summary>Gets how many topic hits were observed (any input).</summary>
        public long TopicHitCount { get; private set; }

        /// <summary>Gets how many State messages were emitted.</summary>
        public long StateEmitCount { get; private set; }

        /// <summary>Gets how many Evidence messages were emitted.</summary>
        public long EvidenceEmitCount { get; private set; }

        /// <summary>
        /// Maps an AD-7 topic to Logigramme1.md node 1 or 2 (interim stub labels only).
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
        /// Adds a typed receiver for one AD-7 topic. Payload is ignored (interim stub); only
        /// topic name + originating time drive Status/Evidence labels and Battery stamping.
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
            string nodeLabel = NodeLabelForTopic(topicName);
            return this.pipeline.CreateReceiver<T>(
                this,
                (_, envelope) => this.OnTopicHit(topicName, nodeLabel, envelope.OriginatingTime),
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

        private void OnTopicHit(string topicName, string nodeLabel, DateTime originatingTime)
        {
            this.TopicHitCount++;
            int batteryId = this.ResolveBatteryId(originatingTime);

            // Explicit interim-stub labeling — not full node 1–2 derivation.
            string status = $"interim-stub node-{nodeLabel}: {topicName}";
            string evidenceId = $"node-{nodeLabel}:{topicName}";

            // Multiple AD-7 topics may share an originating time; Psi emitters require
            // strictly increasing times per stream — bump ticks when needed.
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
