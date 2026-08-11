// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using System.Numerics;
    using Microsoft.Psi;

    /// <summary>
    /// Node 9: door closure edge (open→closed), distinct from sustained <see cref="DoorClosedFilter"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="IndexFilterAssumptions.DoorClosureEdge"/>.
    /// </remarks>
    public static class DoorClosureFilter
    {
        /// <summary>
        /// Emits true on open→closed transitions of the paired door stream.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="doorStream">Catalog door stream for the paired generator.</param>
        /// <param name="generatorIndex">1-based generator index (documentation / naming only).</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>True pulses on door closure edges.</returns>
        public static IProducer<bool> Apply(
            Pipeline pipeline,
            IProducer<ValueTuple<bool, Vector3>> doorStream,
            int generatorIndex,
            DeliveryPolicy<ValueTuple<bool, Vector3>>? deliveryPolicy = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (doorStream == null)
            {
                throw new ArgumentNullException(nameof(doorStream));
            }

            if (generatorIndex < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(generatorIndex), "Generator index must be ≥ 1.");
            }

            string operatorName = string.IsNullOrWhiteSpace(name)
                ? nameof(DoorClosureFilter) + "-G" + generatorIndex
                : name;

            IProducer<bool> doorClosed = DoorClosedFilter.Apply(
                doorStream, generatorIndex, deliveryPolicy, operatorName + "-ClosedLevel");
            return BoolStreamOps.RisingEdge(pipeline, doorClosed, operatorName + "-ClosureEdge");
        }
    }
}
