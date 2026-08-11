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
    using SaacAnalysisCasper.Core.Indices;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Host-owned catalog-typed known-trace inject for Logigramme 1 Option C nodes 1–9 (Story 2.4 / AD-10).
    /// Mirrors <see cref="PocInjectSources"/> Sequence + fixed OT pattern; science stays in Core.
    /// </summary>
    /// <remarks>
    /// Catalogue: <c>Logigramme1KnownTraceScenarios.md</c>. Injects
    /// <see cref="Logigramme1PortRequirements.Logigramme1RequiredRoles"/> only.
    /// </remarks>
    public static class Logigramme1InjectSources
    {
        /// <summary>
        /// Host schedule fit span: deepest path is ~1-miss(5s)+4-miss(3s)+7-miss(5s)+8-miss(5s)+N9 close (~18.5s);
        /// pad to 25 s so session nesting covers node-9 scenarios.
        /// </summary>
        public const int ScheduleSpanMs = 25000;

        /// <summary>
        /// Neutral ModuleStatus id seeded at OT 0. Pre-register via
        /// <see cref="RegisterSeedModuleIds"/> after CompositionFactory session reset so neutrals do not arm node 1.
        /// </summary>
        public const int SeedModuleStatusId = 0;

        private static readonly Vector3 Door1Pose = new Vector3(10f, 0f, 0f);

        private static readonly Vector3 Door2Pose = new Vector3(20f, 0f, 0f);

        private static readonly Vector3 WristFar = new Vector3(0f, 0f, 0f);

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
        /// <returns><c>true</c> for emit-hit scenarios (nodes 3/6/7/8).</returns>
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
        /// Pre-registers the seed ModuleStatus id so OT-0 neutrals do not fire node-1 hits.
        /// Call once after Logigramme1 CompositionFactory session reset (known-trace bind).
        /// </summary>
        public static void RegisterSeedModuleIds()
        {
            ModuleGenerationSuccessFilter.TryRegisterFirstUnseen(SeedModuleStatusId);
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
                    // Doors CLOSED so node 2 can arm (open door would immediate-miss 2→3).
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 50, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 50, open: false, Door2Pose);
                    timeline.AddModuleSuccess(100);
                    timeline.AddDoorHeartbeat(100, 6000, 500, open: false);
                    break;

                case "N1-miss-timeout":
                    timeline.AddDoorHeartbeat(0, 5500, 500, open: true);
                    timeline.AddModuleStatusHeartbeat(0, 5500, 500);
                    break;

                case "N2-hit-post-door":
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 0, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 0, open: false, Door2Pose);
                    timeline.AddModuleSuccess(100);
                    timeline.AddSelectModule(300, "ModuleB");
                    timeline.AddDoorHeartbeat(400, 2000, 500, open: false);
                    break;

                case "N2-miss-door-open":
                    timeline.AddModuleSuccess(100);
                    timeline.AddDoorHeartbeat(100, 3000, 500, open: true);
                    break;

                case "N3-hit-c-gamma":
                    // 1-hit → open-door 2-miss → 3; then DoorClosed ∧ GeneratorArea exit → Gamma.
                    timeline.AddModuleSuccess(100);
                    timeline.AddGeneratorAreaExit(250);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 300, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 300, open: false, Door2Pose);
                    timeline.AddDoorHeartbeat(400, 1500, 500, open: false);
                    break;

                case "N3-miss-c":
                    timeline.AddModuleSuccess(100);
                    timeline.AddDoorHeartbeat(100, 6000, 500, open: true);
                    break;

                case "N4-hit-gaze":
                    timeline.PathToNode4();
                    timeline.AddGazeDwell(5200, "porte", sampleCount: 6, stepMs: 40);
                    timeline.AddDoorHeartbeat(5500, 9000, 500, open: true);
                    break;

                case "N4-miss-gaze":
                    timeline.PathToNode4();
                    timeline.AddDoorHeartbeat(5000, 9000, 500, open: true);
                    timeline.AddModuleStatusHeartbeat(5000, 9000, 500);
                    break;

                case "N5-hit-d-silence":
                    timeline.PathToNode4();
                    // Close while still on 4 so entry to 5 samples DoorClosed=true → D silence.
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 5200, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 5200, open: false, Door2Pose);
                    timeline.AddDoorHeartbeat(5300, 9000, 500, open: false);
                    timeline.AddModuleStatusHeartbeat(5000, 9000, 500);
                    break;

                case "N5-miss-d":
                    // Differ from N4-miss: enter node 5 window then keep door OPEN so 5→7 miss.
                    timeline.PathToNode4();
                    timeline.AddDoorHeartbeat(5000, 12000, 500, open: true);
                    timeline.AddModuleStatusHeartbeat(5000, 12000, 500);
                    break;

                case "N6-hit-e-gamma":
                    timeline.PathToNode4();
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 5100, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 5100, open: false, Door2Pose);
                    timeline.AddGazeDwell(5200, "porte", sampleCount: 6, stepMs: 40);
                    timeline.AddDoorHeartbeat(5500, 8000, 500, open: false);
                    break;

                case "N6-miss-e":
                    timeline.PathToNode4();
                    timeline.AddGazeDwell(5200, "porte", sampleCount: 6, stepMs: 40);
                    timeline.AddDoorHeartbeat(5500, 9000, 500, open: true);
                    break;

                case "N7-hit-alpha":
                    timeline.PathToNode7OpenDoor();
                    // Enter ~8000; Reset sets previousValidation=true — seed false first, then ×3 rising.
                    timeline.AddValidation(8050, false);
                    timeline.AddValidation(8100, true);
                    timeline.AddValidation(8200, false);
                    timeline.AddValidation(8300, true);
                    timeline.AddValidation(8400, false);
                    timeline.AddValidation(8500, true);
                    timeline.AddValidation(8600, false);
                    timeline.AddDoorHeartbeat(8000, 10000, 500, open: true);
                    break;

                case "N7-miss-alpha":
                    timeline.PathToNode7OpenDoor();
                    timeline.AddValidation(8100, true);
                    timeline.AddValidation(8200, false);
                    timeline.AddDoorHeartbeat(8000, 14000, 500, open: true);
                    timeline.AddModuleStatusHeartbeat(8000, 14000, 500);
                    break;

                case "N8-hit-beta":
                    timeline.PathToNode8();
                    // Enter ~13000; Reset sets previousValidation=true — false then rising after Select A→B.
                    timeline.AddSelectModule(13100, "ModuleA");
                    timeline.AddSelectModule(13200, "ModuleB");
                    timeline.AddValidation(13250, false);
                    timeline.AddValidation(13300, true);
                    timeline.AddValidation(13400, false);
                    timeline.AddDoorHeartbeat(13000, 15000, 500, open: true);
                    break;

                case "N8-miss-beta":
                    timeline.PathToNode8();
                    timeline.AddSelectModule(13100, "ModuleB");
                    timeline.AddDoorHeartbeat(13000, 19000, 500, open: true);
                    timeline.AddModuleStatusHeartbeat(13000, 19000, 500);
                    break;

                case "N9-hit-d-silence":
                    timeline.PathToNode9();
                    // Single open→closed DoorClosure; override any PathToNode9 open heartbeats ≥18100.
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 18100, open: true, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 18100, open: true, Door2Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 18200, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 18200, open: false, Door2Pose);
                    timeline.AddDoorHeartbeat(18300, 20000, 500, open: false);
                    // Explicit Upsert at 18500 (PathToNode9 previously left this OT open).
                    timeline.AddDoor(PortRoleIds.GeneratorDoor1, 18500, open: false, Door1Pose);
                    timeline.AddDoor(PortRoleIds.GeneratorDoor2, 18500, open: false, Door2Pose);
                    break;

                case "N9-miss-d":
                    timeline.PathToNode9();
                    timeline.AddDoorHeartbeat(18000, 24000, 500, open: true);
                    timeline.AddModuleStatusHeartbeat(18000, 24000, 500);
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

        private static readonly ClassificationLabel[] ForbidAlphaBeta =
            new[] { ClassificationLabel.Alpha, ClassificationLabel.Beta };

        private static readonly ClassificationLabel[] ForbidBetaGamma =
            new[] { ClassificationLabel.Beta, ClassificationLabel.Gamma };

        private static readonly ClassificationLabel[] ForbidAlphaGamma =
            new[] { ClassificationLabel.Alpha, ClassificationLabel.Gamma };

        private static readonly ClassificationLabel[] ForbidAllLabels =
            new[] { ClassificationLabel.Alpha, ClassificationLabel.Beta, ClassificationLabel.Gamma };

        private static Dictionary<string, ScenarioMeta> BuildScenarioIndex()
        {
            Dictionary<string, ScenarioMeta> map = new Dictionary<string, ScenarioMeta>(StringComparer.Ordinal);
            // Structural hits/misses: no required emit; ForbidAll catches accidental labels.
            Add(map, "N1-hit-success", "1", true, false, false, NoExpected, ForbidAllLabels);
            Add(map, "N1-miss-timeout", "1", false, false, false, NoExpected, ForbidAllLabels);
            Add(map, "N2-hit-post-door", "2", true, false, false, NoExpected, ForbidAllLabels);
            Add(map, "N2-miss-door-open", "2", false, false, false, NoExpected, ForbidAllLabels);
            // Emit hits 3/6/7/8 — forbid competitor labels (BH-01).
            Add(map, "N3-hit-c-gamma", "3", true, true, false, ExpectGamma, ForbidAlphaBeta);
            Add(map, "N3-miss-c", "3", false, false, false, NoExpected, ForbidAllLabels);
            Add(map, "N4-hit-gaze", "4", true, false, false, NoExpected, ForbidAllLabels);
            Add(map, "N4-miss-gaze", "4", false, false, false, NoExpected, ForbidAllLabels);
            // D silence: hard zero-row gate via AssertNoClassificationRows.
            Add(map, "N5-hit-d-silence", "5", true, false, true, NoExpected, ForbidAllLabels);
            Add(map, "N5-miss-d", "5", false, false, false, NoExpected, ForbidAllLabels);
            Add(map, "N6-hit-e-gamma", "6", true, true, false, ExpectGamma, ForbidAlphaBeta);
            Add(map, "N6-miss-e", "6", false, false, false, NoExpected, ForbidAllLabels);
            Add(map, "N7-hit-alpha", "7", true, true, false, ExpectAlpha, ForbidBetaGamma);
            Add(map, "N7-miss-alpha", "7", false, false, false, NoExpected, ForbidAllLabels);
            Add(map, "N8-hit-beta", "8", true, true, false, ExpectBeta, ForbidAlphaGamma);
            Add(map, "N8-miss-beta", "8", false, false, false, NoExpected, ForbidAllLabels);
            Add(map, "N9-hit-d-silence", "9", true, false, true, NoExpected, ForbidAllLabels);
            Add(map, "N9-miss-d", "9", false, false, false, NoExpected, ForbidAllLabels);
            return map;
        }

        private static void Add(
            Dictionary<string, ScenarioMeta> map,
            string id,
            string node,
            bool isHit,
            bool expectRows,
            bool assertNoClassificationRows,
            ClassificationLabel[]? expected = null,
            ClassificationLabel[]? forbidden = null)
        {
            map.Add(
                id,
                new ScenarioMeta(
                    id,
                    node,
                    isHit,
                    expectRows,
                    assertNoClassificationRows,
                    expected ?? NoExpected,
                    forbidden ?? NoForbidden));
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
            /// <param name="node">Option C node key ("1"–"9").</param>
            /// <param name="isHit">Hit vs miss polarity.</param>
            /// <param name="expectClassificationRows">When true, export gate requires ≥1 Classification row.</param>
            /// <param name="assertNoClassificationRows">
            /// When true (D-hit silence only), export gate fails if any Classification row appears.
            /// </param>
            /// <param name="expectedLabels">
            /// Labels that must all appear in Classification CSV for emit hits; empty otherwise.
            /// </param>
            /// <param name="forbiddenLabels">Labels that must not appear (empty for none).</param>
            public ScenarioMeta(
                string scenarioId,
                string node,
                bool isHit,
                bool expectClassificationRows,
                bool assertNoClassificationRows,
                ClassificationLabel[] expectedLabels,
                ClassificationLabel[] forbiddenLabels)
            {
                this.ScenarioId = scenarioId;
                this.Node = node;
                this.IsHit = isHit;
                this.ExpectClassificationRows = expectClassificationRows;
                this.AssertNoClassificationRows = assertNoClassificationRows;
                this.ExpectedLabels = expectedLabels ?? NoExpected;
                this.ForbiddenLabels = forbiddenLabels ?? NoForbidden;
            }

            /// <summary>Gets the scenario id.</summary>
            public string ScenarioId { get; }

            /// <summary>Gets the Option C node id ("1"–"9").</summary>
            public string Node { get; }

            /// <summary>Gets a value indicating whether this is a hit scenario.</summary>
            public bool IsHit { get; }

            /// <summary>Gets a value indicating whether Classification CSV must have ≥1 data row.</summary>
            public bool ExpectClassificationRows { get; }

            /// <summary>
            /// Gets a value indicating whether Classification CSV must stay header-only (D-hit silence).
            /// </summary>
            public bool AssertNoClassificationRows { get; }

            /// <summary>Gets expected labels that must all appear for emit-hit scenarios (empty otherwise).</summary>
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
                this.AddModuleStatus(0, SeedModuleStatusId, "idle");
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

            /// <summary>Node-1 miss timeout (5 s) → node 4; heartbeats keep miss clocks advancing.</summary>
            public void PathToNode4()
            {
                this.AddDoorHeartbeat(0, 5200, 500, open: true);
                this.AddModuleStatusHeartbeat(0, 5200, 500);
            }

            /// <summary>1-miss → 4-miss → 5 with door open → 7 (~8000 ms).</summary>
            public void PathToNode7OpenDoor()
            {
                this.PathToNode4();
                this.AddDoorHeartbeat(5000, 8500, 500, open: true);
                this.AddModuleStatusHeartbeat(5000, 8500, 500);
            }

            /// <summary>Path to 7 then 7-miss (5 s) → 8 (~13000 ms).</summary>
            public void PathToNode8()
            {
                this.PathToNode7OpenDoor();
                this.AddValidation(8100, true);
                this.AddValidation(8200, false);
                this.AddDoorHeartbeat(8000, 13500, 500, open: true);
                this.AddModuleStatusHeartbeat(8000, 13500, 500);
            }

            /// <summary>Path to 8 then 8-miss (5 s) → 9 (~18000 ms).</summary>
            public void PathToNode9()
            {
                this.PathToNode8();
                this.AddSelectModule(13100, "ModuleB");
                // End open heartbeats before N9 closure window (18100+) to avoid OT clobber.
                this.AddDoorHeartbeat(13000, 18000, 500, open: true);
                this.AddModuleStatusHeartbeat(13000, 18000, 500);
            }

            public void AddModuleSuccess(int offsetMs)
            {
                // Per-participant first-unseen id (session-global seen-set shared by M1/M2).
                int moduleId = ((int)this.participant * 1000) + 1;
                this.AddModuleStatus(offsetMs, moduleId, "success");
            }

            public void AddModuleStatusHeartbeat(int startMs, int endMs, int stepMs)
            {
                for (int t = startMs; t <= endMs; t += stepMs)
                {
                    this.AddModuleStatus(t, SeedModuleStatusId, "idle");
                }
            }

            public void AddDoorHeartbeat(int startMs, int endMs, int stepMs, bool open)
            {
                for (int t = startMs; t <= endMs; t += stepMs)
                {
                    this.AddDoor(PortRoleIds.GeneratorDoor1, t, open, Door1Pose);
                    this.AddDoor(PortRoleIds.GeneratorDoor2, t, open, Door2Pose);
                }
            }

            public void AddGeneratorAreaExit(int offsetMs)
            {
                // Player exit level: id==-1, state==false, info==GeneratorArea (both zones for M1/M2 pairing).
                DateTime ot = this.At(offsetMs);
                Upsert(this.zone1, ((-1, false, "GeneratorArea"), ot));
                Upsert(this.zone2, ((-1, false, "GeneratorArea"), ot));
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
