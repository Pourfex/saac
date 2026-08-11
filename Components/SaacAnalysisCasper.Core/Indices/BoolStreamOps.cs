// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using Microsoft.Psi;
    using Microsoft.Psi.Components;

    /// <summary>
    /// Small bool-stream helpers for sticky OR and rising-edge pulses (Story 2.3 review).
    /// </summary>
    public static class BoolStreamOps
    {
        /// <summary>
        /// Sticky OR: true when either input is true inside a short lookback of the other (not last-writer-wins Merge).
        /// </summary>
        /// <param name="left">Left bool stream.</param>
        /// <param name="right">Right bool stream.</param>
        /// <param name="stickyWindowMs">Lookback used as Join tolerance for OR fusion.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>OR of nearest values within the sticky window.</returns>
        public static IProducer<bool> StickyOr(
            IProducer<bool> left,
            IProducer<bool> right,
            int stickyWindowMs,
            string? name = null)
        {
            if (left == null)
            {
                throw new ArgumentNullException(nameof(left));
            }

            if (right == null)
            {
                throw new ArgumentNullException(nameof(right));
            }

            if (stickyWindowMs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(stickyWindowMs));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(StickyOr) : name;
            RelativeTimeInterval tolerance = new RelativeTimeInterval(
                TimeSpan.FromMilliseconds(-stickyWindowMs),
                TimeSpan.FromMilliseconds(stickyWindowMs));

            return left.Join(right, tolerance, DeliveryPolicy.Unlimited, DeliveryPolicy.Unlimited, operatorName + "-Join")
                .Select(
                    (ValueTuple<bool, bool> pair) => pair.Item1 || pair.Item2,
                    DeliveryPolicy.Unlimited,
                    operatorName + "-Or");
        }

        /// <summary>
        /// Emits true only on false→true transitions (rising edge).
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="source">Bool source.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Rising-edge pulse stream (true on edge, false otherwise).</returns>
        public static IProducer<bool> RisingEdge(
            Pipeline pipeline,
            IProducer<bool> source,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(RisingEdge) : name;
            RisingEdgeDetector detector = new RisingEdgeDetector(pipeline, operatorName);
            source.PipeTo(detector.In, DeliveryPolicy.Unlimited);
            return detector.Out;
        }

        /// <summary>
        /// Emits true only on false→true of a dwell/satisfied flag (edge), suppressing sustained true floods.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="satisfied">Level signal (e.g. dwell currently true).</param>
        /// <param name="name">Optional name.</param>
        /// <returns>Edge pulses.</returns>
        public static IProducer<bool> RisingEdgeOnly(
            Pipeline pipeline,
            IProducer<bool> satisfied,
            string? name = null)
        {
            return RisingEdge(pipeline, satisfied, name);
        }

        private sealed class RisingEdgeDetector : ConsumerProducer<bool, bool>
        {
            private bool previous;

            public RisingEdgeDetector(Pipeline pipeline, string name)
                : base(pipeline, name)
            {
            }

            /// <inheritdoc/>
            protected override void Receive(bool data, Envelope envelope)
            {
                bool edge = data && !this.previous;
                this.previous = data;
                this.Out.Post(edge, envelope.OriginatingTime);
            }
        }
    }
}
