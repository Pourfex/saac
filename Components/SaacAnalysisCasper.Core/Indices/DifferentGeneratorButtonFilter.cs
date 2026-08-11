// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using Microsoft.Psi;
    using Microsoft.Psi.Components;

    /// <summary>
    /// Beta-path derived filter: different generator / module button selection (SelectModule changes).
    /// </summary>
    /// <remarks>
    /// First non-empty SelectModule after start (or after blank clear) is <strong>not</strong> a "change" —
    /// it only seeds <c>previous</c>. Empty SelectModule clears <c>previous</c> so a later non-empty
    /// re-select can pulse Beta when it differs from a prior non-empty (after blank, first non-empty
    /// still seeds without pulsing; second distinct non-empty pulses).
    /// </remarks>
    public static class DifferentGeneratorButtonFilter
    {
        /// <summary>
        /// Emits true when SelectModule value changes to a different non-empty string.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="selectModule">Catalog SelectModule stream.</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>True pulses on different-button selections.</returns>
        public static IProducer<bool> Apply(
            Pipeline pipeline,
            IProducer<string> selectModule,
            DeliveryPolicy<string>? deliveryPolicy = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (selectModule == null)
            {
                throw new ArgumentNullException(nameof(selectModule));
            }

            string operatorName = string.IsNullOrWhiteSpace(name)
                ? nameof(DifferentGeneratorButtonFilter)
                : name;

            ChangeDetector detector = new ChangeDetector(pipeline, operatorName);
            selectModule.PipeTo(detector.In, deliveryPolicy ?? DeliveryPolicy.Unlimited);
            return detector.Out;
        }

        private sealed class ChangeDetector : ConsumerProducer<string, bool>
        {
            private string? previous;

            public ChangeDetector(Pipeline pipeline, string name)
                : base(pipeline, name)
            {
            }

            /// <inheritdoc/>
            protected override void Receive(string data, Envelope envelope)
            {
                if (string.IsNullOrEmpty(data))
                {
                    // Clear so a later non-empty can seed again; first seed after blank still does not pulse.
                    this.previous = null;
                    this.Out.Post(false, envelope.OriginatingTime);
                    return;
                }

                bool changed = this.previous != null
                    && !string.Equals(this.previous, data, StringComparison.Ordinal);

                this.previous = data;
                this.Out.Post(changed, envelope.OriginatingTime);
            }
        }
    }
}
