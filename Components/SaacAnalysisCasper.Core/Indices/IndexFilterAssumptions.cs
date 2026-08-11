// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    /// <summary>
    /// Documented assumptions for Story 2.3 derived-input filters
    /// (<c>Logigramme1.md</c> + Option C). Confirm with Alexis if polarity/heuristics prove wrong on PreTest.
    /// </summary>
    public static class IndexFilterAssumptions
    {
        /// <summary>
        /// Door stream <c>ValueTuple&lt;bool, Vector3&gt;.Item1</c> polarity:
        /// topic name is <c>PorteN ouverture</c>; <strong>closed</strong> = <c>Item1 == false</c>.
        /// </summary>
        public const string DoorClosedPolarity =
            "PorteN ouverture Item1==true means OPEN; DoorClosed emits when Item1==false.";

        /// <summary>
        /// Hand-near-door distance threshold in meters (<c>Logigramme1.md</c> node 3).
        /// Door world pose/bounds are not in catalog today — hand arm fail-closes until a pose parent is confirmed.
        /// </summary>
        public const float HandNearDoorDistanceMeters = 1.0f;

        /// <summary>
        /// Bounded Join tolerance for wrist↔door pose fusion (±ms) when a door world-pose parent exists.
        /// </summary>
        public const int HandNearDoorJoinToleranceMs = 1500;

        /// <summary>
        /// Sticky-OR window when combining dual wrist bools (ms).
        /// </summary>
        public const int StickyOrWindowMs = 250;

        /// <summary>
        /// ModuleGenerationSuccess: first-unseen ModuleStatus <c>Item1</c> id (session-global seen-set).
        /// </summary>
        public const string ModuleGenerationSuccessHeuristic =
            "Success when ModuleStatus Item1 (module id) has not been seen yet this session (session-global set shared by M1/M2).";

        /// <summary>
        /// Gaze object-name substrings for door-closed indicator (ObjectType, ordinal ignore-case). Provisional.
        /// </summary>
        public const string GazeIndicatorObjectSubstrings =
            "indicateur;indicator;doorclosed;porte ferm;closedindicator";

        /// <summary>
        /// Gaze object-name substrings for door (ObjectType); indicator matches are excluded. Provisional.
        /// </summary>
        public const string GazeDoorObjectSubstrings = "porte;door";

        /// <summary>
        /// Gaze dwell window lower bound (ms): 150–250 ms per <c>Logigramme1.md</c> node 4.
        /// </summary>
        public const int GazeDwellMinMs = 150;

        /// <summary>
        /// Gaze dwell window upper bound in milliseconds.
        /// </summary>
        public const int GazeDwellMaxMs = 250;

        /// <summary>
        /// Max gap between consecutive matching-gaze samples still treated as contiguous dwell (ms).
        /// </summary>
        public const int GazeDwellMaxGapMs = 50;

        /// <summary>
        /// Node 4: indicator and/or door dwell combine window (ms) from node entry.
        /// </summary>
        public const int GazeCombineWindowMs = 3000;

        /// <summary>
        /// Node 2: DoorClosed level ∧ (SelectModule change | Validation) within this many milliseconds of node entry.
        /// </summary>
        public const int PostDoorSelectOrValidationWindowMs = 2000;

        /// <summary>
        /// Node 7 / 8 family: aggregation / miss window from node entry (milliseconds).
        /// </summary>
        public const int SequenceWindowMs = 5000;

        /// <summary>
        /// Alpha path: ≥ this many Validation rising edges, or Select→Validation pairs, inside <see cref="SequenceWindowMs"/>.
        /// </summary>
        public const int AlphaValidationCountThreshold = 3;

        /// <summary>
        /// Default miss timeout for nodes without an explicit window (nodes 1/3/9) in milliseconds.
        /// </summary>
        public const int DefaultNodeMissTimeoutMs = 5000;

        /// <summary>
        /// ExitGeneratorZone: <c>info=="GeneratorArea"</c> and player <c>id==-1</c>; exit evidence = falling edge
        /// (true→false) <strong>or</strong> level (<c>state==false</c>). Other Area infos must not false-pulse
        /// (hold last GeneratorArea evidence). Zone parent selected by composition pairing (Area1 vs Area2).
        /// </summary>
        public const string ExitGeneratorZoneEdge =
            "Exit evidence when AreaN info==GeneratorArea and Item1==-1 and (Item2 true→false edge OR Item2==false level); hold last on other infos.";

        /// <summary>
        /// DoorClosure (node 9): open→closed rising edge of DoorClosed, distinct from sustained DoorClosed (nodes 5/6).
        /// </summary>
        public const string DoorClosureEdge =
            "DoorClosure pulses on false→true of DoorClosed (open→closed edge).";
    }
}
