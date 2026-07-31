// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.PsiStudioPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Mapping;
    using SaacAnalysisCasper.Core.Poc;

    /// <summary>
    /// Host-owned synthetic A/B inject schedule for Story 1.8 Plugin POC proof (sources only — no A∧B⇒C rules).
    /// Mirrors Replay <c>PocInjectSources</c> gaps so C semantics match across hosts.
    /// </summary>
    /// <remarks>
    /// Per participant: one A at <c>baseOriginatingTime</c>, then B_near at +<see cref="NearGapMs"/>,
    /// then B_far at +<see cref="FarGapMs"/>. With frozen lookback (−W, 0] (left exclusive): expect C when
    /// <c>windowMs &gt; NearGapMs</c>; B_far yields C only when <c>windowMs &gt; FarGapMs</c>.
    /// </remarks>
    public static class PocInjectSources
    {
        /// <summary>
        /// Gap from A to B_near in milliseconds (Δ ≤ large W; Δ &gt; small W for sweep proof).
        /// </summary>
        public const int NearGapMs = 200;

        /// <summary>
        /// Gap from A to B_far in milliseconds (Δ &gt; typical single-W proof values such as 1000).
        /// </summary>
        public const int FarGapMs = 5000;

        /// <summary>
        /// Fixed inject base OriginatingTime for Plugin (no capture session interval). Deterministic science time — not wall-clock.
        /// </summary>
        public static readonly DateTime DefaultInjectBaseUtc =
            new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Returns whether a POC closed over <paramref name="windowMs"/> is expected to emit ≥1 C
        /// for this inject schedule (B_near co-occurs with A inside (−W, 0]).
        /// </summary>
        /// <param name="windowMs">Window length W.</param>
        /// <returns><c>true</c> when W strictly exceeds <see cref="NearGapMs"/> (left-exclusive lookback).</returns>
        public static bool ExpectsCoincidenceRows(int windowMs)
        {
            return windowMs > NearGapMs;
        }

        /// <summary>
        /// Creates a finite Generators.Sequence of tagged A/B events with fixed OriginatingTimes.
        /// </summary>
        /// <param name="pipeline">Analysis pipeline.</param>
        /// <param name="participant">Participant branch (name only; M1/M2 inject are independent).</param>
        /// <param name="baseOriginatingTime">OriginatingTime for A (deterministic Plugin epoch or override).</param>
        /// <returns>Host-owned inject producer (science rules stay in Core).</returns>
        public static IProducer<PocTaggedEvent> Create(
            Pipeline pipeline,
            ParticipantId participant,
            DateTime baseOriginatingTime)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            List<(PocTaggedEvent, DateTime)> schedule = new List<(PocTaggedEvent, DateTime)>
            {
                (new PocTaggedEvent(PocEventKind.A), baseOriginatingTime),
                (new PocTaggedEvent(PocEventKind.B), baseOriginatingTime.AddMilliseconds(NearGapMs)),
                (new PocTaggedEvent(PocEventKind.B), baseOriginatingTime.AddMilliseconds(FarGapMs)),
            };

            return Generators.Sequence(
                pipeline,
                schedule,
                startTime: null,
                keepOpen: false,
                name: "PocInject-" + participant);
        }
    }
}
