// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    /// <summary>
    /// Documented assumptions for Story 2.3 derived-input filters (confirm with Alexis if polarity/heuristics prove wrong on PreTest).
    /// </summary>
    public static class IndexFilterAssumptions
    {
        /// <summary>
        /// Door stream <c>ValueTuple&lt;bool, Vector3&gt;.Item1</c> polarity:
        /// topic name is <c>PorteN ouverture</c>; prior-art CASPERAnalysis treats <c>Item1 == true</c> as open/opening,
        /// so <strong>closed</strong> = <c>Item1 == false</c>.
        /// </summary>
        public const string DoorClosedPolarity =
            "PorteN ouverture Item1==true means OPEN; DoorClosed emits when Item1==false (prior-art MainWindow).";

        /// <summary>
        /// Hand-near-door distance threshold in meters (prior-art HandDoorProximityDetector default).
        /// Door pose parent = door stream <c>Item2</c> Vector3 position.
        /// </summary>
        public const float HandNearDoorDistanceMeters = 0.15f;

        /// <summary>
        /// Bounded Join tolerance for wrist↔door pose fusion (±ms). Not Infinite.
        /// </summary>
        public const int HandNearDoorJoinToleranceMs = 1500;

        /// <summary>
        /// Sticky-OR window when combining dual door / dual wrist bools (ms).
        /// </summary>
        public const int StickyOrWindowMs = 250;

        /// <summary>
        /// ModuleGenerationSuccess: <c>Item1 == 1</c> OR status string contains success markers (prior-art MainWindow).
        /// UTF-8 source uses "réuss"; ASCII "reuss" kept as fallback.
        /// </summary>
        public const string ModuleGenerationSuccessHeuristic =
            "Success when Item1==1 OR Item2 contains 'success'/'réuss'/'reuss' (ordinal ignore-case).";

        /// <summary>
        /// Gaze object-name substrings for door-closed indicator (ObjectType, ordinal ignore-case).
        /// </summary>
        public const string GazeIndicatorObjectSubstrings =
            "indicateur;indicator;doorclosed;porte ferm;closedindicator";

        /// <summary>
        /// Gaze object-name substrings for door (ObjectType); indicator matches are excluded.
        /// </summary>
        public const string GazeDoorObjectSubstrings = "porte;door";

        /// <summary>
        /// Gaze dwell window: PortMap 150–250 ms (not CASPERAnalysis 50–150 ms).
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
        /// Hand-near lookback after module-generation success (prior-art ~500 ms).
        /// </summary>
        public const int HandNearLookbackMs = 500;

        /// <summary>
        /// Door closed lookback/horizon for gaze→door Join: past (already closed) through near future (ms).
        /// </summary>
        public const int DoorClosedJoinPastMs = 2000;

        /// <summary>
        /// Door closed future horizon for gaze→door Join (ms).
        /// </summary>
        public const int DoorClosedJoinFutureMs = 2000;

        /// <summary>
        /// Alpha path: ≥ this many Validation rising edges within the aggregation window (speech omitted).
        /// </summary>
        public const int AlphaValidationCountThreshold = 3;

        /// <summary>
        /// Aggregation window for counting validations toward Alpha (milliseconds).
        /// </summary>
        public const int AlphaValidationWindowMs = 5000;

        /// <summary>
        /// Decision-tick window for exclusive priority mux (ms): at most one winning label per tick.
        /// </summary>
        public const int DecisionTickMs = 100;

        /// <summary>
        /// ExitGeneratorZone: fires on falling edge of zone <c>Item2</c> bool (true→false = exit);
        /// messages with <c>Item1 != zoneIndex</c> are ignored.
        /// </summary>
        public const string ExitGeneratorZoneEdge =
            "Exit when AreaN ValueTuple Item2 transitions from true to false (assumed in-zone flag); Item1 must match bound zoneIndex.";
    }
}
