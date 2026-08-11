// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Mapping
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    /// <summary>
    /// Map-only required port roles for graph id <c>Logigramme1</c> (Story 2.1).
    /// Lists catalog-bound roles that both M1 and M2 can resolve via <see cref="PortTopicMap"/>.
    /// Derived input role ids are listed separately for Story 2.3 — they are not capture topics.
    /// Does not construct a composition graph.
    /// </summary>
    public sealed class Logigramme1PortRequirements : ICompositionPortRequirements
    {
        private static readonly IReadOnlyList<string> CatalogRequiredRoles = new ReadOnlyCollection<string>(
            new[]
            {
                PortRoleIds.SelectModule,
                PortRoleIds.Validation,
                PortRoleIds.ModuleOut,
                PortRoleIds.ModuleOutZone,
                PortRoleIds.ModuleStatus,
                PortRoleIds.GeneratorDoor1,
                PortRoleIds.GeneratorDoor2,
                PortRoleIds.GeneratorZone1,
                PortRoleIds.GeneratorZone2,
                PortRoleIds.GazeEvent,
                PortRoleIds.Head,
                PortRoleIds.LeftWrist,

                // RightWrist omitted: catalog has no 2-RightWrist (see Logigramme1PortMap.md flagged mismatches).
                PortRoleIds.GazeHeadOrientation,
                PortRoleIds.EyeLeft,
                PortRoleIds.EyeRight,
                PortRoleIds.Grab,
                PortRoleIds.AddModule,
                PortRoleIds.RemoveModule,
            });

        private static readonly IReadOnlyList<string> DerivedInputRoles = new ReadOnlyCollection<string>(
            new[]
            {
                PortRoleIds.ModuleGenerationSuccess,
                PortRoleIds.DoorClosed,
                PortRoleIds.HandNearDoor,
                PortRoleIds.ExitGeneratorZone,
                PortRoleIds.GazeOnDoorClosedIndicator,
                PortRoleIds.GazeOnDoor,
                PortRoleIds.RepeatedValidationSequence,
                PortRoleIds.DifferentGeneratorButton,
            });

        /// <summary>
        /// Gets the singleton map-only requirements instance for <c>Logigramme1</c>.
        /// </summary>
        public static Logigramme1PortRequirements Instance { get; } = new Logigramme1PortRequirements();

        /// <summary>
        /// Gets catalog role ids Story 2.3 should declare for host binding (both participants resolvable).
        /// </summary>
        public static IReadOnlyList<string> Logigramme1RequiredRoles => CatalogRequiredRoles;

        /// <summary>
        /// Gets agreed derived input role ids (document/encode names only in 2.1; filters in 2.3).
        /// </summary>
        public static IReadOnlyList<string> Logigramme1DerivedInputRoles => DerivedInputRoles;

        /// <inheritdoc/>
        public IReadOnlyList<string> RequiredPortRoles => CatalogRequiredRoles;
    }
}
