// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Graphs
{
    using System;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Core composition that declares ports by role id and accepts host-bridged producers (AD-12).
    /// Hosts resolve catalog topics; compositions never hardcode <c>M1-</c>/<c>M2-</c> topic strings.
    /// </summary>
    public interface IBindableComposition : ICompositionPortRequirements
    {
        /// <summary>
        /// Gets the graph id this composition implements (e.g. <c>Poc</c>).
        /// </summary>
        string GraphId { get; }

        /// <summary>
        /// Gets the participant this instance is bound for (AD-3 dual-user branch).
        /// </summary>
        ParticipantId Participant { get; }

        /// <summary>
        /// Connects a host-bridged producer to the port identified by <paramref name="roleId"/>.
        /// </summary>
        /// <param name="roleId">Stable role id from <see cref="PortRoleIds"/>.</param>
        /// <param name="producer">Typed <c>IProducer&lt;T&gt;</c> for the catalog stream type.</param>
        /// <exception cref="ArgumentException">Unknown role, or empty role id.</exception>
        /// <exception cref="InvalidOperationException">Producer type mismatch, or role already connected.</exception>
        void Connect(string roleId, object producer);
    }
}
