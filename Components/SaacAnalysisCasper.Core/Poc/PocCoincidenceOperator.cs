// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Poc
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Windowing;

    /// <summary>
    /// Core A∧B-within-W⇒C operator (AD-10). Reuses <see cref="HoppingWindow"/> — no local lookback.
    /// Per-call operator instances only; no static mutable coincidence state (AD-3).
    /// Emits streams only; does not open CSV/stores (AD-9).
    /// </summary>
    /// <remarks>
    /// Stream shape: unified <see cref="PocTaggedEvent"/> (A|B). Algorithm: hop the tagged stream with
    /// frozen lookback (−W, 0] via the <see cref="HoppingWindow"/> Message-selector overload; when the
    /// window contains ≥1 A and the triggering message (max OriginatingTime in the window) is B,
    /// emit <see cref="PocCoincidenceC"/>. C OriginatingTime = B's time (window anchor under that gate).
    /// Left-exclusive lookback: gap Δ == W does not co-occur (A sits on the excluded left edge).
    /// </remarks>
    public static class PocCoincidenceOperator
    {
        /// <summary>
        /// Applies A∧B-within-W⇒C using the shared hopping window closed over <paramref name="windowMs"/>.
        /// </summary>
        /// <param name="taggedEvents">Unified A|B inject stream.</param>
        /// <param name="windowMs">Window length W from run-config (not hard-coded).</param>
        /// <param name="deliveryPolicy">Optional delivery policy for the tagged stream.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Stream of coincidence products C (may be empty when no A∧B co-occur inside W).</returns>
        public static IProducer<PocCoincidenceC> Apply(
            IProducer<PocTaggedEvent> taggedEvents,
            int windowMs,
            DeliveryPolicy<PocTaggedEvent>? deliveryPolicy = null,
            string? name = null)
        {
            if (taggedEvents == null)
            {
                throw new ArgumentNullException(nameof(taggedEvents));
            }

            HoppingWindowPolicy.ValidateWindowMs(windowMs, nameof(windowMs));
            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(PocCoincidenceOperator) : name;

            int capturedWindowMs = windowMs;

            // Message-selector overload: use envelopes (max OT = trigger), never assume payload array order.
            IProducer<PocCoincidenceC> candidates = HoppingWindow.Apply(
                taggedEvents,
                windowMs,
                (IEnumerable<Message<PocTaggedEvent>> messages) =>
                    ProjectCoincidenceOrNull(messages, capturedWindowMs),
                deliveryPolicy,
                operatorName + "-Window");

            return candidates.Where(
                (PocCoincidenceC coincidence) => coincidence != null,
                DeliveryPolicy.Unlimited,
                operatorName + "-Emit");
        }

        private static PocCoincidenceC ProjectCoincidenceOrNull(
            IEnumerable<Message<PocTaggedEvent>> messages,
            int windowMs)
        {
            if (messages == null)
            {
                return null;
            }

            bool hasA = false;
            Message<PocTaggedEvent>? trigger = null;

            foreach (Message<PocTaggedEvent> message in messages)
            {
                if (message.Data == null)
                {
                    continue;
                }

                if (message.Data.Kind == PocEventKind.A)
                {
                    hasA = true;
                }

                if (!trigger.HasValue || message.OriginatingTime >= trigger.Value.OriginatingTime)
                {
                    trigger = message;
                }
            }

            if (!hasA || !trigger.HasValue || trigger.Value.Data == null)
            {
                return null;
            }

            if (trigger.Value.Data.Kind != PocEventKind.B)
            {
                return null;
            }

            return new PocCoincidenceC(windowMs);
        }
    }
}
