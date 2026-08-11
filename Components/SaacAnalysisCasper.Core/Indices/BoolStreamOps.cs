// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using Microsoft.Psi;
    using Microsoft.Psi.Components;

    /// <summary>
    /// Small bool-stream helpers for sticky OR, held AND/OR, and rising-edge pulses (Story 2.3).
    /// </summary>
    public static class BoolStreamOps
    {
        /// <summary>
        /// Sticky OR via Join: true when either input is true inside a short lookback of the other.
        /// Prefer <see cref="HeldOr"/> when one arm may be silent (Join-starve).
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
        /// Held OR: tracks last value of each arm independently so a silent arm cannot starve the other.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="left">Left bool stream.</param>
        /// <param name="right">Right bool stream.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>OR of held last values.</returns>
        public static IProducer<bool> HeldOr(
            Pipeline pipeline,
            IProducer<bool> left,
            IProducer<bool> right,
            string? name = null)
        {
            return HeldCombine(pipeline, left, right, andNotOr: false, name ?? nameof(HeldOr));
        }

        /// <summary>
        /// Held AND: sticky levels — last left ∧ last right (no tiny Join window).
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="left">Left bool stream.</param>
        /// <param name="right">Right bool stream.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>AND of held last values.</returns>
        public static IProducer<bool> HeldAnd(
            Pipeline pipeline,
            IProducer<bool> left,
            IProducer<bool> right,
            string? name = null)
        {
            return HeldCombine(pipeline, left, right, andNotOr: true, name ?? nameof(HeldAnd));
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

        private static IProducer<bool> HeldCombine(
            Pipeline pipeline,
            IProducer<bool> left,
            IProducer<bool> right,
            bool andNotOr,
            string name)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (left == null)
            {
                throw new ArgumentNullException(nameof(left));
            }

            if (right == null)
            {
                throw new ArgumentNullException(nameof(right));
            }

            HeldBoolCombiner combiner = new HeldBoolCombiner(pipeline, name, andNotOr);
            left.PipeTo(combiner.LeftIn, DeliveryPolicy.Unlimited);
            right.PipeTo(combiner.RightIn, DeliveryPolicy.Unlimited);
            return combiner.Out;
        }

        private sealed class HeldBoolCombiner
        {
            private readonly Emitter<bool> output;
            private readonly bool andNotOr;
            private bool left;
            private bool right;
            private bool leftSeen;
            private bool rightSeen;
            private DateTime lastEmittedOt = DateTime.MinValue;

            public HeldBoolCombiner(Pipeline pipeline, string name, bool andNotOr)
            {
                this.andNotOr = andNotOr;
                this.output = pipeline.CreateEmitter<bool>(this, name + "-Out");
                this.LeftIn = pipeline.CreateReceiver<bool>(this, this.ReceiveLeft, name + "-LeftIn");
                this.RightIn = pipeline.CreateReceiver<bool>(this, this.ReceiveRight, name + "-RightIn");
            }

            public Receiver<bool> LeftIn { get; }

            public Receiver<bool> RightIn { get; }

            public Emitter<bool> Out => this.output;

            private void ReceiveLeft(bool value, Envelope envelope)
            {
                this.left = value;
                this.leftSeen = true;
                this.Emit(envelope.OriginatingTime);
            }

            private void ReceiveRight(bool value, Envelope envelope)
            {
                this.right = value;
                this.rightSeen = true;
                this.Emit(envelope.OriginatingTime);
            }

            private void Emit(DateTime originatingTime)
            {
                // AND waits until both arms have sampled; OR fires as soon as either has.
                if (this.andNotOr)
                {
                    if (!this.leftSeen || !this.rightSeen)
                    {
                        return;
                    }
                }
                else if (!this.leftSeen && !this.rightSeen)
                {
                    return;
                }

                bool combined = this.andNotOr
                    ? (this.left && this.right)
                    : ((this.leftSeen && this.left) || (this.rightSeen && this.right));

                DateTime emitOt = originatingTime;
                if (emitOt < this.lastEmittedOt)
                {
                    emitOt = this.lastEmittedOt.AddTicks(1);
                }
                else if (emitOt == this.lastEmittedOt)
                {
                    emitOt = this.lastEmittedOt.AddTicks(1);
                }

                this.output.Post(combined, emitOt);
                this.lastEmittedOt = emitOt;
            }
        }

        private sealed class RisingEdgeDetector : ConsumerProducer<bool, bool>
        {
            private bool previous;
            private bool seen;

            public RisingEdgeDetector(Pipeline pipeline, string name)
                : base(pipeline, name)
            {
            }

            /// <inheritdoc/>
            protected override void Receive(bool data, Envelope envelope)
            {
                // First sample seeds state only — do not treat initial true as open→closed.
                bool edge = this.seen && data && !this.previous;
                this.previous = data;
                this.seen = true;
                this.Out.Post(edge, envelope.OriginatingTime);
            }
        }
    }
}
