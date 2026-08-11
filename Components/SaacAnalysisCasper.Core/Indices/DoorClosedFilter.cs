// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using System.Numerics;
    using Microsoft.Psi;

    /// <summary>
    /// Derives per-generator door-closed bool from <c>PorteN ouverture</c> (<c>ValueTuple&lt;bool, Vector3&gt;</c>).
    /// </summary>
    /// <remarks>
    /// Polarity assumption: <see cref="IndexFilterAssumptions.DoorClosedPolarity"/>.
    /// Call once per generator parent — do not silently collapse both doors without a selector.
    /// </remarks>
    public static class DoorClosedFilter
    {
        /// <summary>
        /// Projects door open/pose stream to door-closed bool for one generator.
        /// </summary>
        /// <param name="doorStream">Catalog door stream for one generator.</param>
        /// <param name="generatorIndex">1-based generator index (documentation / naming only).</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>True when door is considered closed.</returns>
        public static IProducer<bool> Apply(
            IProducer<ValueTuple<bool, Vector3>> doorStream,
            int generatorIndex,
            DeliveryPolicy<ValueTuple<bool, Vector3>>? deliveryPolicy = null,
            string? name = null)
        {
            if (doorStream == null)
            {
                throw new ArgumentNullException(nameof(doorStream));
            }

            if (generatorIndex < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(generatorIndex), "Generator index must be ≥ 1.");
            }

            string operatorName = string.IsNullOrWhiteSpace(name)
                ? nameof(DoorClosedFilter) + "-G" + generatorIndex
                : name;

            // Item1 true = open (ouverture); closed when false.
            return doorStream.Select(
                (ValueTuple<bool, Vector3> door) => !door.Item1,
                deliveryPolicy ?? DeliveryPolicy.Unlimited,
                operatorName);
        }
    }
}
