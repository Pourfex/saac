// <copyright file="ScrubTimeShift.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Applies a signed scrub delay to persisted Logigram State/Outcome originating times
    /// so PsiStudio Messages line up with Casper video (same idea as Spike42GazeHits --delay-sec).
    /// </summary>
    public static class ScrubTimeShift
    {
        /// <summary>
        /// Shifts <paramref name="originatingTime"/> by <paramref name="scrubDelaySec"/> seconds.
        /// </summary>
        /// <param name="originatingTime">Raw message OT.</param>
        /// <param name="scrubDelaySec">Signed seconds (positive = later on the timeline).</param>
        /// <returns>Shifted OT (unchanged when delay is 0).</returns>
        public static DateTime Apply(DateTime originatingTime, double scrubDelaySec)
        {
            if (scrubDelaySec == 0.0)
            {
                return originatingTime;
            }

            return originatingTime.AddSeconds(scrubDelaySec);
        }

        /// <summary>
        /// Returns a new list with OTs shifted, sorted ascending, with colliding OTs bumped by 1 tick.
        /// </summary>
        /// <typeparam name="T">Message payload type.</typeparam>
        /// <param name="messages">Raw (payload, OT) pairs.</param>
        /// <param name="scrubDelaySec">Signed scrub delay in seconds.</param>
        /// <returns>Persist-ready sequence.</returns>
        public static List<(T Message, DateTime OriginatingTime)> ApplyAndOrder<T>(
            IEnumerable<(T Message, DateTime OriginatingTime)> messages,
            double scrubDelaySec)
        {
            var list = (messages ?? Enumerable.Empty<(T, DateTime)>())
                .Select(item => (item.Message, Apply(item.OriginatingTime, scrubDelaySec)))
                .OrderBy(item => item.Item2)
                .ToList();

            for (int i = 1; i < list.Count; i++)
            {
                if (list[i].Item2 <= list[i - 1].Item2)
                {
                    list[i] = (list[i].Item1, list[i - 1].Item2.AddTicks(1));
                }
            }

            return list;
        }
    }
}
