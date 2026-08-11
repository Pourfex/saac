// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Mapping
{
    /// <summary>
    /// Stable composition port role identifiers (AD-12). Compositions declare these; hosts resolve topics via <see cref="PortTopicMap"/>.
    /// Direct catalog roles map through <see cref="PortTopicMap"/>; derived input roles are named here for Story 2.3 wiring and are not capture topics.
    /// </summary>
    public static class PortRoleIds
    {
        /// <summary>
        /// Select-module event role (<c>M1-SelectModule</c> / <c>M2-SelectModule</c>).
        /// </summary>
        public const string SelectModule = "SelectModule";

        /// <summary>
        /// Validation event role (<c>M1-Validation</c> / <c>M2-Validation</c>).
        /// </summary>
        public const string Validation = "Validation";

        /// <summary>
        /// Module-out event role (<c>M1-ModuleOut</c> / <c>M2-ModuleOut</c>).
        /// </summary>
        public const string ModuleOut = "ModuleOut";

        /// <summary>
        /// Module-out-zone event role (<c>M1-ModuleOutZone</c> / <c>M2-ModuleOutZone</c>).
        /// </summary>
        public const string ModuleOutZone = "ModuleOutZone";

        /// <summary>
        /// Shared module-status catalog topic (<c>Module status</c>) for both participants.
        /// </summary>
        public const string ModuleStatus = "ModuleStatus";

        /// <summary>
        /// Shared generator door 1 catalog topic (<c>Porte1 ouverture</c>).
        /// </summary>
        public const string GeneratorDoor1 = "GeneratorDoor1";

        /// <summary>
        /// Shared generator door 2 catalog topic (<c>Porte2 ouverture</c>).
        /// </summary>
        public const string GeneratorDoor2 = "GeneratorDoor2";

        /// <summary>
        /// Shared generator zone 1 catalog topic (<c>Area1</c>).
        /// </summary>
        public const string GeneratorZone1 = "GeneratorZone1";

        /// <summary>
        /// Shared generator zone 2 catalog topic (<c>Area2</c>).
        /// </summary>
        public const string GeneratorZone2 = "GeneratorZone2";

        /// <summary>
        /// Shared gaze catalog topic (<c>GazeEvent</c>) for both participants.
        /// </summary>
        public const string GazeEvent = "GazeEvent";

        /// <summary>
        /// Head pose role (<c>1-Head</c> / <c>2-Head</c>).
        /// </summary>
        public const string Head = "Head";

        /// <summary>
        /// Left-wrist pose role (<c>1-LeftWrist</c> / <c>2-LeftWrist</c>).
        /// </summary>
        public const string LeftWrist = "LeftWrist";

        /// <summary>
        /// Right-wrist pose role. Catalog has <c>1-RightWrist</c> only — M2 has no <c>2-RightWrist</c> (flagged asymmetry).
        /// </summary>
        public const string RightWrist = "RightWrist";

        /// <summary>
        /// Gaze head-orientation role (<c>1-GazeHeadOrientation</c> / <c>2-GazeHeadOrientation</c>).
        /// </summary>
        public const string GazeHeadOrientation = "GazeHeadOrientation";

        /// <summary>
        /// Left-eye role (<c>1-EyeLeft</c> / <c>2-EyeLeft</c>).
        /// </summary>
        public const string EyeLeft = "EyeLeft";

        /// <summary>
        /// Right-eye role (<c>1-EyeRight</c> / <c>2-EyeRight</c>).
        /// </summary>
        public const string EyeRight = "EyeRight";

        /// <summary>
        /// Grab role mapped to catalog <c>Grab1</c> (M1) / <c>Grab2</c> (M2).
        /// </summary>
        public const string Grab = "Grab";

        /// <summary>
        /// Shared add-module catalog topic (<c>AddModule</c>).
        /// </summary>
        public const string AddModule = "AddModule";

        /// <summary>
        /// Shared remove-module catalog topic (<c>RemoveModule</c>).
        /// </summary>
        public const string RemoveModule = "RemoveModule";

        /// <summary>
        /// Agreed derived input: module-generation success from <see cref="ModuleStatus"/> (Story 2.3).
        /// </summary>
        public const string ModuleGenerationSuccess = "ModuleGenerationSuccess";

        /// <summary>
        /// Agreed derived input: door-closed interpretation from generator door ports (Story 2.3).
        /// </summary>
        public const string DoorClosed = "DoorClosed";

        /// <summary>
        /// Agreed derived input: hand near door from wrist + door pose (Story 2.3).
        /// </summary>
        public const string HandNearDoor = "HandNearDoor";

        /// <summary>
        /// Agreed derived input: exit generator zone from zone ports (Story 2.3).
        /// </summary>
        public const string ExitGeneratorZone = "ExitGeneratorZone";

        /// <summary>
        /// Agreed derived input: gaze on door-closed indicator via <see cref="GazeEvent"/> object filter (Story 2.3).
        /// </summary>
        public const string GazeOnDoorClosedIndicator = "GazeOnDoorClosedIndicator";

        /// <summary>
        /// Agreed derived input: gaze on door via <see cref="GazeEvent"/> object filter (Story 2.3).
        /// </summary>
        public const string GazeOnDoor = "GazeOnDoor";

        /// <summary>
        /// Agreed derived input: repeated validation sequence from SelectModule + Validation (Story 2.3).
        /// </summary>
        public const string RepeatedValidationSequence = "RepeatedValidationSequence";

        /// <summary>
        /// Agreed derived input: different-generator button path from SelectModule (Story 2.3).
        /// </summary>
        public const string DifferentGeneratorButton = "DifferentGeneratorButton";
    }
}
