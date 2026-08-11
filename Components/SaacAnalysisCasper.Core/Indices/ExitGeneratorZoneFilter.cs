// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using Microsoft.Psi;
    using Microsoft.Psi.Components;

    /// <summary>
    /// Derives generator-zone exit evidence from <c>AreaN</c> (<c>ValueTuple&lt;int,bool,string&gt;</c>).
    /// </summary>
    /// <remarks>
    /// <see cref="IndexFilterAssumptions.ExitGeneratorZoneEdge"/>:
    /// <c>info=="GeneratorArea"</c> + player <c>id==-1</c>; exit = falling edge (true→false) <strong>or</strong>
    /// level (<c>state==false</c>). Other Area <c>info</c> values do not post (hold last GeneratorArea evidence).
    /// Zone parent is selected by M1↔Area1 / M2↔Area2 pairing — <c>Item1</c> is entity id, not zone index.
    /// </remarks>
    public static class ExitGeneratorZoneFilter
    {
        private const string GeneratorAreaInfo = "GeneratorArea";

        private const int PlayerEntityId = -1;

        /// <summary>
        /// Emits <c>true</c> when GeneratorArea player exit evidence is present (edge or level).
        /// </summary>
        /// <param name="pipeline">Owning pipeline (for stateful edge detector).</param>
        /// <param name="zoneStream">Catalog zone stream for the paired area (Area1 or Area2).</param>
        /// <param name="zoneIndex">1-based zone index (documentation / naming only).</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Stream that is true on GeneratorArea player exit evidence.</returns>
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

            ExitEvidenceDetector detector = new ExitEvidenceDetector(pipeline, operatorName);
            zoneStream.PipeTo(detector.In, deliveryPolicy ?? DeliveryPolicy.Unlimited);
            return detector.Out;
        }

        private sealed class ExitEvidenceDetector : ConsumerProducer<ValueTuple<int, bool, string>, bool>
        {
            private bool? previousPlayerInZone;

            public ExitEvidenceDetector(Pipeline pipeline, string name)
                : base(pipeline, name)
            {
            }

            /// <inheritdoc/>
            protected override void Receive(ValueTuple<int, bool, string> data, Envelope envelope)
            {
                // Hold last GeneratorArea player evidence — do not false-pulse other Area infos or module ids.
                if (!string.Equals(data.Item3, GeneratorAreaInfo, StringComparison.Ordinal)
                    || data.Item1 != PlayerEntityId)
                {
                    return;
                }

                bool inZone = data.Item2;
                bool edgeExit = this.previousPlayerInZone.HasValue && this.previousPlayerInZone.Value && !inZone;
                bool levelExit = !inZone;
                this.previousPlayerInZone = inZone;
                this.Out.Post(edgeExit || levelExit, envelope.OriginatingTime);
            }
        }
    }
}
