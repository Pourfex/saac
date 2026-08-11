// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Replay.Services
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using Microsoft.Psi;
    using SAAC.PsiFormats;
    using SaacAnalysisCasper.Core.Classification;
    using SaacAnalysisCasper.Core.Graphs;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Host-owned catalog-typed known-trace inject for Logigramme 1 (Story 2.4 / AD-10).
    /// Mirrors <see cref="PocInjectSources"/> Sequence + fixed OT pattern; science stays in Core.
    /// </summary>
    /// <remarks>
    /// Catalogue: <c>Logigramme1KnownTraceScenarios.md</c>. Injects
    /// <see cref="Logigramme1PortRequirements.Logigramme1RequiredRoles"/> only.
    /// </remarks>
    public static class Logigramme1InjectSources
    {
        /// <summary>
        /// Host schedule fit span: max inject OT offset across scenarios is ~3800 ms (full-tree-coverage);
        /// pad to 5 s so session nesting covers the sequenced tree without overstating Alpha's Core 5 s lookback.
        /// </summary>
        public const int ScheduleSpanMs = 5000;

        private static readonly Vector3 Door1Pose = new Vector3(10f, 0f, 0f);

        private static readonly Vector3 Door2Pose = new Vector3(20f, 0f, 0f);

        private static readonly Vector3 WristFar = new Vector3(0f, 0f, 0f);

        private static readonly Vector3 WristNearDoor1 = new Vector3(10.05f, 0f, 0f);

        private static readonly Vector3 PoseForward = new Vector3(0f, 0f, 1f);

        private static readonly Dictionary<string, ScenarioMeta> Scenarios =
            BuildScenarioIndex();

        /// <summary>
        /// Returns whether <paramref name="scenarioId"/> is a documented known-trace id.
        /// </summary>
        /// <param name="scenarioId">Scenario id from run-config.</param>
        /// <returns><c>true</c> when the id is known.</returns>
        public static bool IsKnownScenario(string scenarioId)
        {
            return !string.IsNullOrWhiteSpace(scenarioId)
                && Scenarios.ContainsKey(scenarioId);
        }

        /// <summary>
        /// Returns whether a documented hit scenario expects ≥1 Classification CSV row.
        /// </summary>
        /// <param name="scenarioId">Known scenario id.</param>
        /// <returns><c>true</c> for hit polarity scenarios that emit ExpectedLabels.</returns>
        public static bool ExpectsClassificationRows(string scenarioId)
        {
            ScenarioMeta meta;
            if (!TryGetMeta(scenarioId, out meta))
            {
                throw new InvalidOperationException(
                    "Unknown knownTraceScenario '" + scenarioId + "'; cannot resolve ExpectClassificationRows.");
            }

            return meta.ExpectClassificationRows;
        }

        /// <summary>
        /// Looks up catalogue metadata for a scenario id.
        /// </summary>
        /// <param name="scenarioId">Scenario id.</param>
        /// <param name="meta">Metadata when found.</param>
        /// <returns><c>true</c> when known.</returns>
        public static bool TryGetMeta(string scenarioId, out ScenarioMeta meta)
        {
            if (string.IsNullOrWhiteSpace(scenarioId))
            {
                meta = default;
                return false;
            }

            return Scenarios.TryGetValue(scenarioId, out meta);
        }

        /// <summary>
        /// Builds independent inject producers for one participant and connects all required catalog roles.
        /// </summary>
        /// <param name="pipeline">Analysis pipeline.</param>
        /// <param name="participant">Participant branch (M1/M2 inject are independent).</param>
        /// <param name="scenarioId">Documented scenario id.</param>
        /// <param name="baseOriginatingTime">
        /// Nest inside the Replay session interval (same discipline as <see cref="PocInjectSources"/>).
        /// </param>
        /// <returns>Bundle that can <see cref="InjectBundle.ConnectTo"/> each W composition instance.</returns>
        public static InjectBundle Create(
            Pipeline pipeline,
            ParticipantId participant,
            string scenarioId,
            DateTime baseOriginatingTime)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            ScenarioMeta meta;
            if (!TryGetMeta(scenarioId, out meta))
            {
                throw new InvalidOperationException(
                    "Unknown knownTraceScenario '" + scenarioId
                    + "'. See Logigramme1KnownTraceScenarios.md for documented ids.");
            }

            ScenarioTimeline timeline = new ScenarioTimeline(baseOriginatingTime, participant);
            timeline.SeedNeutrals();
            ApplyScenarioOverrides(timeline, scenarioId);

            string namePrefix = "L1Inject-" + participant + "-" + scenarioId;
            Dictionary<string, object> producers = timeline.BuildProducers(pipeline, namePrefix);

            IReadOnlyList<string> required = Logigramme1PortRequirements.Logigramme1RequiredRoles;
            for (int i = 0; i < required.Count; i++)
            {
                string roleId = required[i];
                if (!producers.ContainsKey(roleId))
                {
                    throw new InvalidOperationException(
                        "Known-trace scenario '" + scenarioId
                        + "' missing inject producer for required role '" + roleId + "'.");
                }
            }

            return new InjectBundle(scenarioId, meta, producers);
        }

        private static void ApplyScenarioOverrides(ScenarioTimeline timeline, string scenarioId)
        {
            switch (scenarioId)
            {
                case "N1-hit-success":
                case "N2-hit-handnear":
                    timeline.AddPose(PortRoleIds.LeftWrist, 0, WristNearDoor1, PoseForward);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 0, open: true, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 0, open: true, Door2Pose);
                    timeline.AddModuleStatus(200, 1, "success");
                    break;

                case "N1-miss-success":
                    timeline.AddPose(PortRoleIds.LeftWrist, 0, WristNearDoor1, PoseForward);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 0, open: true, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 0, open: true, Door2Pose);
                    timeline.AddModuleStatus(200, 0, "failure");
                    break;

                case "N2-miss-handnear":
                    timeline.AddPose(PortRoleIds.LeftWrist, 0, WristFar, PoseForward);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 0, open: true, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 0, open: true, Door2Pose);
                    timeline.AddModuleStatus(200, 1, "success");
                    break;

                case "N3-hit-indicator":
                    timeline.AddGazeDwell(50, "indicateur", sampleCount: 6, stepMs: 40);
                    timeline.AddGazeDwell(400, "porte", sampleCount: 6, stepMs: 40);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 580, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 580, open: true, Door2Pose);
                    break;

                case "N3-miss-indicator":
                    timeline.AddGaze(100, gazing: true, " unrelated-object ");
                    timeline.AddGaze(140, gazing: false, " unrelated-object ");
                    break;

                case "N4-hit-gaze-door":
                case "N5-hit-door-closed-gaze":
                    timeline.AddGazeDwell(100, "porte", sampleCount: 6, stepMs: 40);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 280, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 280, open: true, Door2Pose);
                    break;

                case "N4-miss-gaze-door":
                case "N5-miss-door-closed-gaze":
                    timeline.AddGazeDwell(100, "porte", sampleCount: 6, stepMs: 40);
                    break;

                case "N6-hit-alpha":
                    timeline.AddSelectModule(0, "ModuleA");
                    timeline.AddValidation(100, true);
                    timeline.AddValidation(150, false);
                    timeline.AddValidation(200, true);
                    timeline.AddValidation(250, false);
                    timeline.AddValidation(300, true);
                    timeline.AddValidation(350, false);
                    break;

                case "N6-miss-alpha":
                    timeline.AddSelectModule(0, "ModuleA");
                    timeline.AddValidation(100, true);
                    timeline.AddValidation(150, false);
                    break;

                case "N7-hit-beta":
                    timeline.AddSelectModule(0, "ModuleA");
                    timeline.AddSelectModule(200, "ModuleB");
                    break;

                case "N7-miss-beta":
                    timeline.AddSelectModule(0, "ModuleA");
                    break;

                case "N8-hit-door-else-gamma":
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 500, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 500, open: true, Door2Pose);
                    break;

                case "N8-miss-door-else":
                    // Neutrals only (doors stay open).
                    break;

                case "full-tree-coverage":
                    // Sequenced hits + mid-timeline misses; gaps keep mux competitors off each other's ticks.
                    // --- Anticipation hit (success ∧ hand-near) ---
                    timeline.AddPose(PortRoleIds.LeftWrist, 100, WristNearDoor1, PoseForward);
                    timeline.AddModuleStatus(200, 1, "success");
                    // --- Silence then Anticipation miss (success, wrist far → no AnticipationΓ) ---
                    timeline.AddPose(PortRoleIds.LeftWrist, 400, WristFar, PoseForward);
                    timeline.AddModuleStatus(400, 0, "idle");
                    timeline.AddModuleStatus(600, 1, "success");
                    timeline.AddModuleStatus(800, 0, "idle");
                    // --- Indicator dwell keep-alive then GazeΓ (gaze door ∧ door close) ---
                    timeline.AddGazeDwell(900, "indicateur", sampleCount: 6, stepMs: 40);
                    timeline.AddGazeDwell(1200, "porte", sampleCount: 6, stepMs: 40);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 1450, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 1450, open: true, Door2Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 1700, open: true, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 1700, open: true, Door2Pose);
                    timeline.AddGaze(1700, gazing: false, objectType: string.Empty);
                    // --- Gaze miss (door dwell, doors stay OPEN → no GazeΓ) ---
                    timeline.AddGazeDwell(1900, "porte", sampleCount: 6, stepMs: 40);
                    timeline.AddGaze(2200, gazing: false, objectType: string.Empty);
                    // --- Alpha hit (Validation×3) ---
                    timeline.AddSelectModule(2400, "ModuleA");
                    timeline.AddValidation(2450, true);
                    timeline.AddValidation(2500, false);
                    timeline.AddValidation(2550, true);
                    timeline.AddValidation(2600, false);
                    timeline.AddValidation(2650, true);
                    timeline.AddValidation(2700, false);
                    timeline.AddValidation(2900, false);
                    // --- Beta hit (SelectModule A→B) ---
                    timeline.AddSelectModule(3100, "ModuleB");
                    // --- DoorElseΓ (door close; no gaze/success/hand) ---
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 3600, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 3600, open: true, Door2Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 3800, open: true, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 3800, open: true, Door2Pose);
                    break;

                default:
                    throw new InvalidOperationException(
                        "Scenario '" + scenarioId + "' is indexed but has no timeline overrides.");
            }
        }

        private static readonly ClassificationLabel[] NoExpected = Array.Empty<ClassificationLabel>();

        private static readonly ClassificationLabel[] NoForbidden = Array.Empty<ClassificationLabel>();

        private static readonly ClassificationLabel[] ExpectGamma =
            new[] { ClassificationLabel.Gamma };

        private static readonly ClassificationLabel[] ExpectAlpha =
            new[] { ClassificationLabel.Alpha };

        private static readonly ClassificationLabel[] ExpectBeta =
            new[] { ClassificationLabel.Beta };

        private static readonly ClassificationLabel[] ExpectAlphaBetaGamma =
            new[] { ClassificationLabel.Alpha, ClassificationLabel.Beta, ClassificationLabel.Gamma };

        private static readonly ClassificationLabel[] ForbidGamma =
            new[] { ClassificationLabel.Gamma };

        private static readonly ClassificationLabel[] ForbidAlpha =
            new[] { ClassificationLabel.Alpha };

        private static readonly ClassificationLabel[] ForbidBeta =
            new[] { ClassificationLabel.Beta };

        private static readonly ClassificationLabel[] ForbidGammaBeta =
            new[] { ClassificationLabel.Gamma, ClassificationLabel.Beta };

        private static readonly ClassificationLabel[] ForbidAlphaGamma =
            new[] { ClassificationLabel.Alpha, ClassificationLabel.Gamma };

        private static readonly ClassificationLabel[] ForbidAlphaBeta =
            new[] { ClassificationLabel.Alpha, ClassificationLabel.Beta };

        private static Dictionary<string, ScenarioMeta> BuildScenarioIndex()
        {
            Dictionary<string, ScenarioMeta> map = new Dictionary<string, ScenarioMeta>(StringComparer.Ordinal);
            Add(map, "N1-hit-success", "N1", true, true, ExpectGamma, NoForbidden);
            Add(map, "N1-miss-success", "N1", false, false, NoExpected, ForbidGamma);
            Add(map, "N2-hit-handnear", "N2", true, true, ExpectGamma, NoForbidden);
            Add(map, "N2-miss-handnear", "N2", false, false, NoExpected, ForbidGamma);
            Add(map, "N3-hit-indicator", "N3", true, true, ExpectGamma, NoForbidden);
            Add(map, "N3-miss-indicator", "N3", false, false, NoExpected, ForbidGamma);
            Add(map, "N4-hit-gaze-door", "N4", true, true, ExpectGamma, NoForbidden);
            Add(map, "N4-miss-gaze-door", "N4", false, false, NoExpected, ForbidGamma);
            Add(map, "N5-hit-door-closed-gaze", "N5", true, true, ExpectGamma, NoForbidden);
            Add(map, "N5-miss-door-closed-gaze", "N5", false, false, NoExpected, ForbidGamma);
            Add(map, "N6-hit-alpha", "N6", true, true, ExpectAlpha, ForbidGammaBeta);
            Add(map, "N6-miss-alpha", "N6", false, false, NoExpected, ForbidAlpha);
            Add(map, "N7-hit-beta", "N7", true, true, ExpectBeta, ForbidAlphaGamma);
            Add(map, "N7-miss-beta", "N7", false, false, NoExpected, ForbidBeta);
            Add(map, "N8-hit-door-else-gamma", "N8", true, true, ExpectGamma, ForbidAlphaBeta);
            Add(map, "N8-miss-door-else", "N8", false, false, NoExpected, ForbidGamma);
            Add(map, "full-tree-coverage", "ALL", true, true, ExpectAlphaBetaGamma, NoForbidden);
            return map;
        }

        private static void Add(
            Dictionary<string, ScenarioMeta> map,
            string id,
            string miroNode,
            bool isHit,
            bool expectRows,
            ClassificationLabel[]? expected = null,
            ClassificationLabel[]? forbidden = null)
        {
            map.Add(id, new ScenarioMeta(id, miroNode, isHit, expectRows, expected ?? NoExpected, forbidden ?? NoForbidden));
        }

        /// <summary>
        /// Catalogue metadata for one known-trace scenario.
        /// </summary>
        public readonly struct ScenarioMeta
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ScenarioMeta"/> struct.
            /// </summary>
            /// <param name="scenarioId">Scenario id.</param>
            /// <param name="miroNode">Miro node key (N1–N8 or ALL for full-tree).</param>
            /// <param name="isHit">Hit vs miss polarity.</param>
            /// <param name="expectClassificationRows">When true, export gate requires ≥1 Classification row.</param>
            /// <param name="expectedLabels">
            /// Labels that must all appear in Classification CSV for hits; empty for miss-only.
            /// </param>
            /// <param name="forbiddenLabels">Labels that must not appear (empty for none).</param>
            public ScenarioMeta(
                string scenarioId,
                string miroNode,
                bool isHit,
                bool expectClassificationRows,
                ClassificationLabel[] expectedLabels,
                ClassificationLabel[] forbiddenLabels)
            {
                this.ScenarioId = scenarioId;
                this.MiroNode = miroNode;
                this.IsHit = isHit;
                this.ExpectClassificationRows = expectClassificationRows;
                this.ExpectedLabels = expectedLabels ?? NoExpected;
                this.ForbiddenLabels = forbiddenLabels ?? NoForbidden;
            }

            /// <summary>Gets the scenario id.</summary>
            public string ScenarioId { get; }

            /// <summary>Gets the Miro node id.</summary>
            public string MiroNode { get; }

            /// <summary>Gets a value indicating whether this is a hit scenario.</summary>
            public bool IsHit { get; }

            /// <summary>Gets a value indicating whether Classification CSV must have ≥1 data row.</summary>
            public bool ExpectClassificationRows { get; }

            /// <summary>Gets expected labels that must all appear for hit scenarios (empty for miss).</summary>
            public ClassificationLabel[] ExpectedLabels { get; }

            /// <summary>Gets labels that must not appear in Classification CSV (empty for none).</summary>
            public ClassificationLabel[] ForbiddenLabels { get; }
        }

        /// <summary>
        /// Per-participant inject producer set (fan-out to every W composition).
        /// </summary>
        public sealed class InjectBundle
        {
            private readonly Dictionary<string, object> producersByRole;

            /// <summary>
            /// Initializes a new instance of the <see cref="InjectBundle"/> class.
            /// </summary>
            /// <param name="scenarioId">Scenario id.</param>
            /// <param name="meta">Scenario metadata.</param>
            /// <param name="producersByRole">Catalog role → producer map.</param>
            internal InjectBundle(
                string scenarioId,
                ScenarioMeta meta,
                Dictionary<string, object> producersByRole)
            {
                this.ScenarioId = scenarioId;
                this.Meta = meta;
                this.producersByRole = producersByRole;
            }

            /// <summary>Gets the scenario id.</summary>
            public string ScenarioId { get; }

            /// <summary>Gets scenario metadata.</summary>
            public ScenarioMeta Meta { get; }

            /// <summary>
            /// Connects all required catalog roles onto a Logigramme1 composition.
            /// </summary>
            /// <param name="composition">Bindable composition (must declare L1 required roles).</param>
            public void ConnectTo(IBindableComposition composition)
            {
                if (composition == null)
                {
                    throw new ArgumentNullException(nameof(composition));
                }

                IReadOnlyList<string> required = composition.RequiredPortRoles;
                if (required == null)
                {
                    throw new InvalidOperationException(
                        "Composition RequiredPortRoles must be non-null for known-trace Connect.");
                }

                for (int i = 0; i < required.Count; i++)
                {
                    string roleId = required[i];
                    object producer;
                    if (!this.producersByRole.TryGetValue(roleId, out producer) || producer == null)
                    {
                        throw new InvalidOperationException(
                            "Known-trace inject missing required role '" + roleId
                            + "' for scenario '" + this.ScenarioId + "'.");
                    }

                    composition.Connect(roleId, producer);
                }
            }
        }

        private sealed class ScenarioTimeline
        {
            private readonly DateTime baseOt;
            private readonly ParticipantId participant;
            private readonly List<(string, DateTime)> selectModule = new List<(string, DateTime)>();
            private readonly List<(bool, DateTime)> validation = new List<(bool, DateTime)>();
            private readonly List<(bool, DateTime)> moduleOut = new List<(bool, DateTime)>();
            private readonly List<(bool, DateTime)> moduleOutZone = new List<(bool, DateTime)>();
            private readonly List<(ValueTuple<int, string>, DateTime)> moduleStatus =
                new List<(ValueTuple<int, string>, DateTime)>();
            private readonly List<(ValueTuple<bool, Vector3>, DateTime)> door1 =
                new List<(ValueTuple<bool, Vector3>, DateTime)>();
            private readonly List<(ValueTuple<bool, Vector3>, DateTime)> door2 =
                new List<(ValueTuple<bool, Vector3>, DateTime)>();
            private readonly List<(ValueTuple<int, bool, string>, DateTime)> zone1 =
                new List<(ValueTuple<int, bool, string>, DateTime)>();
            private readonly List<(ValueTuple<int, bool, string>, DateTime)> zone2 =
                new List<(ValueTuple<int, bool, string>, DateTime)>();
            private readonly List<(PsiGazeObjectEvent, DateTime)> gaze =
                new List<(PsiGazeObjectEvent, DateTime)>();
            private readonly List<(Tuple<Vector3, Vector3>, DateTime)> head =
                new List<(Tuple<Vector3, Vector3>, DateTime)>();
            private readonly List<(Tuple<Vector3, Vector3>, DateTime)> leftWrist =
                new List<(Tuple<Vector3, Vector3>, DateTime)>();
            private readonly List<(Tuple<Vector3, Vector3>, DateTime)> gazeHead =
                new List<(Tuple<Vector3, Vector3>, DateTime)>();
            private readonly List<(Tuple<Vector3, Vector3>, DateTime)> eyeLeft =
                new List<(Tuple<Vector3, Vector3>, DateTime)>();
            private readonly List<(Tuple<Vector3, Vector3>, DateTime)> eyeRight =
                new List<(Tuple<Vector3, Vector3>, DateTime)>();
            private readonly List<(ValueTuple<int, bool, string>, DateTime)> grab =
                new List<(ValueTuple<int, bool, string>, DateTime)>();
            private readonly List<(PsiBatteryModuleEvent, DateTime)> addModule =
                new List<(PsiBatteryModuleEvent, DateTime)>();
            private readonly List<(PsiBatteryModuleEvent, DateTime)> removeModule =
                new List<(PsiBatteryModuleEvent, DateTime)>();

            public ScenarioTimeline(DateTime baseOriginatingTime, ParticipantId participant)
            {
                this.baseOt = baseOriginatingTime;
                this.participant = participant;
            }

            public void SeedNeutrals()
            {
                this.AddSelectModule(0, "ModuleA");
                // Stagger Validation off SelectModule OT so RepeatedValidationSequenceFilter
                // (dual receivers) is not forced to coalesce a same-OT seed pair.
                this.AddValidation(1, false);
                this.moduleOut.Add((false, this.At(0)));
                this.moduleOutZone.Add((false, this.At(0)));
                this.AddModuleStatus(0, 0, "idle");
                this.AddDoor(PortRoleIds.GeneratorDoor1, 0, open: true, Door1Pose);
                this.AddDoor(PortRoleIds.GeneratorDoor2, 0, open: true, Door2Pose);
                this.zone1.Add(((1, false, string.Empty), this.At(0)));
                this.zone2.Add(((2, false, string.Empty), this.At(0)));
                this.AddGaze(0, gazing: false, objectType: string.Empty);
                this.AddPose(PortRoleIds.Head, 0, WristFar, PoseForward);
                this.AddPose(PortRoleIds.LeftWrist, 0, WristFar, PoseForward);
                this.AddPose(PortRoleIds.GazeHeadOrientation, 0, WristFar, PoseForward);
                this.AddPose(PortRoleIds.EyeLeft, 0, WristFar, PoseForward);
                this.AddPose(PortRoleIds.EyeRight, 0, WristFar, PoseForward);
                this.grab.Add(((0, false, string.Empty), this.At(0)));
                this.addModule.Add((NeutralBattery(), this.At(0)));
                this.removeModule.Add((NeutralBattery(), this.At(0)));
            }

            public void AddSelectModule(int offsetMs, string value)
            {
                Upsert(this.selectModule, (value, this.At(offsetMs)));
            }

            public void AddValidation(int offsetMs, bool value)
            {
                Upsert(this.validation, (value, this.At(offsetMs)));
            }

            public void AddModuleStatus(int offsetMs, int code, string text)
            {
                Upsert(this.moduleStatus, ((code, text), this.At(offsetMs)));
            }

            public void AddDoor(string roleId, int offsetMs, bool open, Vector3 pose)
            {
                ValueTuple<bool, Vector3> payload = (open, pose);
                DateTime ot = this.At(offsetMs);
                if (string.Equals(roleId, PortRoleIds.GeneratorDoor1, StringComparison.Ordinal))
                {
                    Upsert(this.door1, (payload, ot));
                }
                else if (string.Equals(roleId, PortRoleIds.GeneratorDoor2, StringComparison.Ordinal))
                {
                    Upsert(this.door2, (payload, ot));
                }
                else
                {
                    throw new ArgumentException("Unexpected door role '" + roleId + "'.", nameof(roleId));
                }
            }

            public void AddPose(string roleId, int offsetMs, Vector3 position, Vector3 forward)
            {
                Tuple<Vector3, Vector3> pose = Tuple.Create(position, forward);
                DateTime ot = this.At(offsetMs);
                if (string.Equals(roleId, PortRoleIds.Head, StringComparison.Ordinal))
                {
                    Upsert(this.head, (pose, ot));
                }
                else if (string.Equals(roleId, PortRoleIds.LeftWrist, StringComparison.Ordinal))
                {
                    Upsert(this.leftWrist, (pose, ot));
                }
                else if (string.Equals(roleId, PortRoleIds.GazeHeadOrientation, StringComparison.Ordinal))
                {
                    Upsert(this.gazeHead, (pose, ot));
                }
                else if (string.Equals(roleId, PortRoleIds.EyeLeft, StringComparison.Ordinal))
                {
                    Upsert(this.eyeLeft, (pose, ot));
                }
                else if (string.Equals(roleId, PortRoleIds.EyeRight, StringComparison.Ordinal))
                {
                    Upsert(this.eyeRight, (pose, ot));
                }
                else
                {
                    throw new ArgumentException("Unexpected pose role '" + roleId + "'.", nameof(roleId));
                }
            }

            public void AddGaze(int offsetMs, bool gazing, string objectType)
            {
                PsiGazeObjectEvent evt = new PsiGazeObjectEvent(
                    (int)this.participant,
                    objectId: 1,
                    gazing,
                    objectType ?? string.Empty);
                Upsert(this.gaze, (evt, this.At(offsetMs)));
            }

            private static void Upsert<T>(List<(T, DateTime)> schedule, (T, DateTime) sample)
            {
                for (int i = 0; i < schedule.Count; i++)
                {
                    if (schedule[i].Item2 == sample.Item2)
                    {
                        schedule[i] = sample;
                        return;
                    }
                }

                schedule.Add(sample);
            }

            public void AddGazeDwell(int startOffsetMs, string objectType, int sampleCount, int stepMs)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    this.AddGaze(startOffsetMs + (i * stepMs), gazing: true, objectType);
                }
            }

            public Dictionary<string, object> BuildProducers(Pipeline pipeline, string namePrefix)
            {
                Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [PortRoleIds.SelectModule] = Sequence(pipeline, this.selectModule, namePrefix + "-SelectModule"),
                    [PortRoleIds.Validation] = Sequence(pipeline, this.validation, namePrefix + "-Validation"),
                    [PortRoleIds.ModuleOut] = Sequence(pipeline, this.moduleOut, namePrefix + "-ModuleOut"),
                    [PortRoleIds.ModuleOutZone] = Sequence(pipeline, this.moduleOutZone, namePrefix + "-ModuleOutZone"),
                    [PortRoleIds.ModuleStatus] = Sequence(pipeline, this.moduleStatus, namePrefix + "-ModuleStatus"),
                    [PortRoleIds.GeneratorDoor1] = Sequence(pipeline, this.door1, namePrefix + "-Door1"),
                    [PortRoleIds.GeneratorDoor2] = Sequence(pipeline, this.door2, namePrefix + "-Door2"),
                    [PortRoleIds.GeneratorZone1] = Sequence(pipeline, this.zone1, namePrefix + "-Zone1"),
                    [PortRoleIds.GeneratorZone2] = Sequence(pipeline, this.zone2, namePrefix + "-Zone2"),
                    [PortRoleIds.GazeEvent] = Sequence(pipeline, this.gaze, namePrefix + "-Gaze"),
                    [PortRoleIds.Head] = Sequence(pipeline, this.head, namePrefix + "-Head"),
                    [PortRoleIds.LeftWrist] = Sequence(pipeline, this.leftWrist, namePrefix + "-LeftWrist"),
                    [PortRoleIds.GazeHeadOrientation] = Sequence(pipeline, this.gazeHead, namePrefix + "-GazeHead"),
                    [PortRoleIds.EyeLeft] = Sequence(pipeline, this.eyeLeft, namePrefix + "-EyeLeft"),
                    [PortRoleIds.EyeRight] = Sequence(pipeline, this.eyeRight, namePrefix + "-EyeRight"),
                    [PortRoleIds.Grab] = Sequence(pipeline, this.grab, namePrefix + "-Grab"),
                    [PortRoleIds.AddModule] = Sequence(pipeline, this.addModule, namePrefix + "-AddModule"),
                    [PortRoleIds.RemoveModule] = Sequence(pipeline, this.removeModule, namePrefix + "-RemoveModule"),
                };

                return map;
            }

            private DateTime At(int offsetMs)
            {
                return this.baseOt.AddMilliseconds(offsetMs);
            }

            private static PsiBatteryModuleEvent NeutralBattery()
            {
                return new PsiBatteryModuleEvent(
                    batteryId: 0,
                    power: 0,
                    places: 0,
                    regulated: false,
                    moduleId: 0,
                    moduleType: string.Empty,
                    modulePower: 0,
                    placeIndex: 0,
                    moduleStatus: string.Empty);
            }

            private static IProducer<T> Sequence<T>(
                Pipeline pipeline,
                List<(T, DateTime)> schedule,
                string name)
            {
                if (schedule == null || schedule.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Inject schedule '" + name + "' must contain at least one sample.");
                }

                // startTime: null — stay inside session-proposed ReplayDescriptor (do not LeftBound to +∞).
                return Generators.Sequence(
                    pipeline,
                    schedule,
                    startTime: null,
                    keepOpen: false,
                    name: name);
            }
        }
    }
}
