// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using Microsoft.Psi;
    using Microsoft.Psi.Components;

    /// <summary>
    /// Derives generator-zone exit pulses from <c>AreaN</c> (<c>ValueTuple&lt;int,bool,string&gt;</c>).
    /// </summary>
    /// <remarks>
    /// Edge assumption: <see cref="IndexFilterAssumptions.ExitGeneratorZoneEdge"/>.
    /// Per-zone selector — call once per zone parent. Messages with <c>Item1 != zoneIndex</c> are ignored.
    /// </remarks>
    public static class ExitGeneratorZoneFilter
    {
        /// <summary>
        /// Emits <c>true</c> on falling edge of the zone occupancy bool (exit).
        /// </summary>
        /// <param name="pipeline">Owning pipeline (for stateful edge detector).</param>
        /// <param name="zoneStream">Catalog zone stream for one area.</param>
        /// <param name="zoneIndex">1-based zone index; messages with other Item1 are ignored.</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Stream that pulses true on exit edges.</returns>
        public static IProducer<bool> Apply(
            Pipeline pipeline,
            IProducer<ValueTuple<int, bool, string>> zoneStream,
            int zoneIndex,
            DeliveryPolicy<ValueTuple<int, bool, string>>? deliveryPolicy = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (zoneStream == null)
            {
                throw new ArgumentNullException(nameof(zoneStream));
            }

            if (zoneIndex < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(zoneIndex), "Zone index must be ≥ 1.");
            }

            string operatorName = string.IsNullOrWhiteSpace(name)
                ? nameof(ExitGeneratorZoneFilter) + "-Z" + zoneIndex
                : name;

            FallingEdgeDetector detector = new FallingEdgeDetector(pipeline, operatorName, zoneIndex);
            zoneStream.PipeTo(detector.In, deliveryPolicy ?? DeliveryPolicy.Unlimited);
            return detector.Out;
        }

        private sealed class FallingEdgeDetector : ConsumerProducer<ValueTuple<int, bool, string>, bool>
        {
            private readonly int zoneIndex;
            private bool? previousInZone;

            public FallingEdgeDetector(Pipeline pipeline, string name, int zoneIndex)
                : base(pipeline, name)
            {
                this.zoneIndex = zoneIndex;
            }

            /// <inheritdoc/>
            protected override void Receive(ValueTuple<int, bool, string> data, Envelope envelope)
            {
                if (data.Item1 != this.zoneIndex)
                {
                    return;
                }

                bool inZone = data.Item2;
                bool exited = this.previousInZone.HasValue && this.previousInZone.Value && !inZone;
                this.previousInZone = inZone;
                this.Out.Post(exited, envelope.OriginatingTime);
            }
        }
    }
}
