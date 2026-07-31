// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Windowing
{
    using System;
    using Microsoft.Psi;

    /// <summary>
    /// Frozen Story 1.6 hopping/sliding window policy (AD-6). Single shared hop + interval semantics for Replay, Plugin, Poc, and all Logigrammes.
    /// Do not reimplement or diverge from these values in hosts or graphs.
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Lookback: <c>RelativeTimeInterval.Past(TimeSpan.FromMilliseconds(windowMs), inclusive: false)</c> → (−W, 0]
    /// (left exclusive, present inclusive).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Primary advance: event-anchored Psi <c>RelativeTimeWindow</c> (emits per source message; hop follows stream pace).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Clock hop when a uniform hop is required (Sample / DynamicWindow / timer-driven aggregation):
    /// <c>hopMs = max(1, windowMs / 10)</c> via <see cref="HopMs"/> only — never hard-code a different hop in a graph.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// W bounds: [<see cref="MinWindowMs"/>, <see cref="MaxWindowMs"/>] ms inclusive (100 ms–2 min).
    /// </description>
    /// </item>
    /// </list>
    /// </summary>
    public static class HoppingWindowPolicy
    {
        /// <summary>
        /// Minimum allowed window length W in milliseconds (inclusive).
        /// </summary>
        public const int MinWindowMs = 100;

        /// <summary>
        /// Maximum allowed window length W in milliseconds (inclusive) — 2 minutes.
        /// </summary>
        public const int MaxWindowMs = 120000;

        /// <summary>
        /// Creates the frozen lookback interval (−W, 0] for the given window length.
        /// </summary>
        /// <param name="windowMs">Window length W in milliseconds; must be in [<see cref="MinWindowMs"/>, <see cref="MaxWindowMs"/>].</param>
        /// <returns>Psi relative lookback interval with left exclusive and present inclusive.</returns>
        public static RelativeTimeInterval CreateLookback(int windowMs)
        {
            ValidateWindowMs(windowMs, nameof(windowMs));
            return RelativeTimeInterval.Past(TimeSpan.FromMilliseconds(windowMs), inclusive: false);
        }

        /// <summary>
        /// Derives the frozen clock hop in milliseconds: <c>max(1, windowMs / 10)</c>.
        /// Use only when a uniform hop clock is required; primary advance remains event-anchored.
        /// </summary>
        /// <param name="windowMs">Window length W in milliseconds.</param>
        /// <returns>Hop duration in milliseconds (always ≥ 1).</returns>
        public static int HopMs(int windowMs)
        {
            ValidateWindowMs(windowMs, nameof(windowMs));
            return Math.Max(1, windowMs / 10);
        }

        /// <summary>
        /// Validates that <paramref name="windowMs"/> is within the frozen W bounds.
        /// </summary>
        /// <param name="windowMs">Window length to validate.</param>
        /// <param name="fieldName">Config or parameter field name for the exception message (e.g. <c>windowMs</c>).</param>
        /// <exception cref="InvalidOperationException">Thrown when W is outside [<see cref="MinWindowMs"/>, <see cref="MaxWindowMs"/>].</exception>
        public static void ValidateWindowMs(int windowMs, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                fieldName = "windowMs";
            }

            if (windowMs < MinWindowMs || windowMs > MaxWindowMs)
            {
                throw new InvalidOperationException(
                    "'" + fieldName + "' must be in [" + MinWindowMs + ", " + MaxWindowMs + "] milliseconds (inclusive); got " + windowMs + ".");
            }
        }
    }
}
