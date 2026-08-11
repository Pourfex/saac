// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SAAC.PsiFormats;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Filters <see cref="PsiGazeObjectEvent"/> by object-name pattern and 150–250 ms contiguous dwell.
    /// </summary>
    /// <remarks>
    /// Dwell bounds: <see cref="IndexFilterAssumptions.GazeDwellMinMs"/>–
    /// <see cref="IndexFilterAssumptions.GazeDwellMaxMs"/>.
    /// Node 4 combine: indicator <strong>and/or</strong> door within <see cref="IndexFilterAssumptions.GazeCombineWindowMs"/>
    /// via held OR (one silent arm must not starve).
    /// </remarks>
    public static class GazeDwellFilter
    {
        /// <summary>
        /// Gaze on door-closed indicator with PortMap dwell.
        /// </summary>
        /// <param name="pipeline">Owning pipeline (rising-edge detector).</param>
        /// <param name="gazeEvent">Shared catalog GazeEvent stream.</param>
        /// <param name="participant">Participant branch (filters UserId).</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>True pulses when dwell on indicator is newly satisfied.</returns>
        public static IProducer<bool> ApplyDoorClosedIndicator(
            Pipeline pipeline,
            IProducer<PsiGazeObjectEvent> gazeEvent,
            ParticipantId participant,
            DeliveryPolicy<PsiGazeObjectEvent>? deliveryPolicy = null,
            string? name = null)
        {
            string[] patterns = SplitPatterns(IndexFilterAssumptions.GazeIndicatorObjectSubstrings);
            return Apply(
                pipeline,
                gazeEvent,
                participant,
                patterns,
                excludeSubstrings: null,
                deliveryPolicy,
                string.IsNullOrWhiteSpace(name) ? nameof(GazeDwellFilter) + "-Indicator" : name);
        }

        /// <summary>
        /// Gaze on door with PortMap dwell (excludes indicator object matches).
        /// </summary>
        /// <param name="pipeline">Owning pipeline (rising-edge detector).</param>
        /// <param name="gazeEvent">Shared catalog GazeEvent stream.</param>
        /// <param name="participant">Participant branch (filters UserId).</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>True pulses when dwell on door is newly satisfied.</returns>
        public static IProducer<bool> ApplyDoor(
            Pipeline pipeline,
            IProducer<PsiGazeObjectEvent> gazeEvent,
            ParticipantId participant,
            DeliveryPolicy<PsiGazeObjectEvent>? deliveryPolicy = null,
            string? name = null)
        {
            string[] include = SplitPatterns(IndexFilterAssumptions.GazeDoorObjectSubstrings);
            string[] exclude = SplitPatterns(IndexFilterAssumptions.GazeIndicatorObjectSubstrings);
            return Apply(
                pipeline,
                gazeEvent,
                participant,
                include,
                exclude,
                deliveryPolicy,
                string.IsNullOrWhiteSpace(name) ? nameof(GazeDwellFilter) + "-Door" : name);
        }

        /// <summary>
        /// Node 4: indicator and/or door dwell levels combined with held OR (no Join-starve).
        /// Miss window (3 s from node entry) is enforced by the stepper.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="gazeEvent">Shared catalog GazeEvent stream.</param>
        /// <param name="participant">Participant branch.</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Held-OR level: true when either gaze arm satisfies dwell.</returns>
        public static IProducer<bool> ApplyCombinedWithinWindow(
            Pipeline pipeline,
            IProducer<PsiGazeObjectEvent> gazeEvent,
            ParticipantId participant,
            DeliveryPolicy<PsiGazeObjectEvent>? deliveryPolicy = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            string operatorName = string.IsNullOrWhiteSpace(name)
                ? nameof(GazeDwellFilter) + "-Combine"
                : name;

            string[] indicatorPatterns = SplitPatterns(IndexFilterAssumptions.GazeIndicatorObjectSubstrings);
            string[] doorInclude = SplitPatterns(IndexFilterAssumptions.GazeDoorObjectSubstrings);
            string[] doorExclude = SplitPatterns(IndexFilterAssumptions.GazeIndicatorObjectSubstrings);

            IProducer<bool> indicatorLevel = ApplyDwellLevel(
                gazeEvent,
                participant,
                indicatorPatterns,
                excludeSubstrings: null,
                deliveryPolicy,
                operatorName + "-IndLevel");

            IProducer<bool> doorLevel = ApplyDwellLevel(
                gazeEvent,
                participant,
                doorInclude,
                doorExclude,
                deliveryPolicy,
                operatorName + "-DoorLevel");

            return BoolStreamOps.HeldOr(pipeline, indicatorLevel, doorLevel, operatorName + "-HeldOr");
        }

        /// <summary>
        /// Generic object-name + contiguous dwell filter with rising-edge emit.
        /// </summary>
        /// <param name="pipeline">Owning pipeline.</param>
        /// <param name="gazeEvent">Gaze catalog stream.</param>
        /// <param name="participant">Participant for UserId filter.</param>
        /// <param name="includeSubstrings">ObjectType must contain one of these.</param>
        /// <param name="excludeSubstrings">ObjectType must not contain any of these (optional).</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Operator name.</param>
        /// <returns>Dwell-satisfied rising-edge bool stream.</returns>
        public static IProducer<bool> Apply(
            Pipeline pipeline,
            IProducer<PsiGazeObjectEvent> gazeEvent,
            ParticipantId participant,
            string[] includeSubstrings,
            string[]? excludeSubstrings,
            DeliveryPolicy<PsiGazeObjectEvent>? deliveryPolicy = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(GazeDwellFilter) : name;
            IProducer<bool> dwellLevel = ApplyDwellLevel(
                gazeEvent,
                participant,
                includeSubstrings,
                excludeSubstrings,
                deliveryPolicy,
                operatorName + "-Level");

            return BoolStreamOps.RisingEdgeOnly(pipeline, dwellLevel, operatorName + "-DwellEdge");
        }

        private static IProducer<bool> ApplyDwellLevel(
            IProducer<PsiGazeObjectEvent> gazeEvent,
            ParticipantId participant,
            string[] includeSubstrings,
            string[]? excludeSubstrings,
            DeliveryPolicy<PsiGazeObjectEvent>? deliveryPolicy,
            string operatorName)
        {
            if (gazeEvent == null)
            {
                throw new ArgumentNullException(nameof(gazeEvent));
            }

            if (includeSubstrings == null || includeSubstrings.Length == 0)
            {
                throw new ArgumentException("At least one include substring is required.", nameof(includeSubstrings));
            }

            int userId = (int)participant;
            DeliveryPolicy<PsiGazeObjectEvent> policy = deliveryPolicy ?? DeliveryPolicy.Unlimited;

            IProducer<bool> matchingGaze = gazeEvent.Select(
                (PsiGazeObjectEvent gaze) =>
                    gaze.UserId == userId
                    && gaze.IsGazing
                    && MatchesObject(gaze.ObjectType, includeSubstrings, excludeSubstrings),
                policy,
                operatorName + "-Match");

            return matchingGaze.Window(
                RelativeTimeInterval.Past(TimeSpan.FromMilliseconds(IndexFilterAssumptions.GazeDwellMaxMs)),
                (IEnumerable<Message<bool>> messages) => HasContiguousDwell(messages),
                DeliveryPolicy.Unlimited,
                operatorName + "-DwellLevel");
        }

        private static bool HasContiguousDwell(IEnumerable<Message<bool>> messages)
        {
            if (messages == null)
            {
                return false;
            }

            List<DateTime> trueTimes = new List<DateTime>();
            foreach (Message<bool> message in messages)
            {
                if (message.Data)
                {
                    trueTimes.Add(message.OriginatingTime);
                }
            }

            if (trueTimes.Count == 0)
            {
                return false;
            }

            trueTimes.Sort();

            int runStart = 0;
            for (int i = 1; i <= trueTimes.Count; i++)
            {
                bool endRun = i == trueTimes.Count
                    || (trueTimes[i] - trueTimes[i - 1]).TotalMilliseconds > IndexFilterAssumptions.GazeDwellMaxGapMs;

                if (!endRun)
                {
                    continue;
                }

                double spanMs = (trueTimes[i - 1] - trueTimes[runStart]).TotalMilliseconds;
                if (spanMs >= IndexFilterAssumptions.GazeDwellMinMs
                    && spanMs <= IndexFilterAssumptions.GazeDwellMaxMs)
                {
                    return true;
                }

                runStart = i;
            }

            return false;
        }

        private static bool MatchesObject(string? objectType, string[] include, string[]? exclude)
        {
            if (string.IsNullOrEmpty(objectType))
            {
                return false;
            }

            bool included = false;
            for (int i = 0; i < include.Length; i++)
            {
                if (objectType.IndexOf(include[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    included = true;
                    break;
                }
            }

            if (!included)
            {
                return false;
            }

            if (exclude != null)
            {
                for (int i = 0; i < exclude.Length; i++)
                {
                    if (objectType.IndexOf(exclude[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static string[] SplitPatterns(string semicolonSeparated)
        {
            string[] parts = semicolonSeparated.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
            }

            return parts;
        }
    }
}
