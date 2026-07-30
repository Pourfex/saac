// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Mapping
{
    using System.Collections.Generic;

    /// <summary>
    /// Declares required input ports by stable role id (AD-12).
    /// Compositions must not discover topics; hosts resolve via <see cref="PortTopicMap"/>.
    /// </summary>
    public interface ICompositionPortRequirements
    {
        /// <summary>
        /// Gets the required port role ids (see <see cref="PortRoleIds"/>).
        /// </summary>
        IReadOnlyList<string> RequiredPortRoles { get; }
    }
}
