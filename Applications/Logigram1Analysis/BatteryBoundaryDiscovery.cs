// <copyright file="BatteryBoundaryDiscovery.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using SAAC.PsiFormats;

    /// <summary>
    /// Discovers Battery analysis scopes via the locked AD-6 BOUNDARY_RULE (Spawn→Finish, LIFO stack).
    /// See <c>Components/Logigram1/README.md</c> and this type's remarks — never invents Battery ids.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOUNDARY_RULE: start at each <c>BatterySpawn</c> (<see cref="PsiBatteryEvent"/>) originating time;
    /// end at the paired <c>BatteryFinish</c> (<see cref="PsiBatteryFinish"/>) originating time.
    /// </para>
    /// <para>
    /// JOIN_KEY LIFO: push Spawn <c>BatteryId</c> on open stack; Finish pops the most recent still-open id
    /// (Finish has no id in PreTest.081 / Unity). Fail on empty-stack Finish, unclosed Spawns at end,
    /// missing streams, or zero closed segments.
    /// </para>
    /// </remarks>
    public static class BatteryBoundaryDiscovery
    {
        /// <summary>Exact Casper stream name for Battery spawn events.</summary>
        public const string SpawnStreamName = "BatterySpawn";

        /// <summary>Exact Casper stream name for Battery finish events.</summary>
        public const string FinishStreamName = "BatteryFinish";

        /// <summary>
        /// Segments a store Dataset using exact stream-name lookup and the locked Spawn→Finish LIFO rule.
        /// </summary>
        /// <param name="dataset">Loaded Psi Dataset (connectors already validated by the console).</param>
        /// <param name="findStreamByExactName">
        /// Stream lookup (defaults to <see cref="OfflineCasperStoreConsole.FindStreamByExactName"/>).
        /// </param>
        /// <param name="segments">Closed segments in discovery order, or empty on failure.</param>
        /// <param name="error">Null on success; otherwise why the rule cannot apply.</param>
        /// <returns>True when one or more segments were discovered; false on explicit failure.</returns>
        public static bool TryDiscoverFromDataset(
            Dataset dataset,
            Func<Dataset, string, IStreamMetadata> findStreamByExactName,
            out IReadOnlyList<BatterySegment> segments,
            out string error)
        {
            segments = Array.Empty<BatterySegment>();
            error = null;

            if (dataset == null)
            {
                error = "Dataset is null; cannot discover Battery boundaries.";
                return false;
            }

            Func<Dataset, string, IStreamMetadata> lookup = findStreamByExactName
                ?? OfflineCasperStoreConsole.FindStreamByExactName;

            IStreamMetadata spawnMeta = lookup(dataset, SpawnStreamName);
            if (spawnMeta == null)
            {
                error = $"Required Battery stream missing for Spawn→Finish rule: {SpawnStreamName}.";
                return false;
            }

            IStreamMetadata finishMeta = lookup(dataset, FinishStreamName);
            if (finishMeta == null)
            {
                error = $"Required Battery stream missing for Spawn→Finish rule: {FinishStreamName}.";
                return false;
            }

            List<(int BatteryId, DateTime OriginatingTime)> spawns;
            List<DateTime> finishes;
            try
            {
                spawns = ReadSpawnEvents(spawnMeta);
                finishes = ReadFinishEvents(finishMeta);
            }
            catch (Exception exception)
            {
                error = $"Failed to read Battery boundary streams: {exception.Message}";
                return false;
            }

            return TrySegment(spawns, finishes, out segments, out error);
        }

        /// <summary>
        /// Applies the LIFO Spawn→Finish rule to in-memory timed events (unit-testable core).
        /// </summary>
        /// <param name="spawns">Spawn events as (BatteryId, originating time).</param>
        /// <param name="finishes">Finish originating times (no id on Finish payload).</param>
        /// <param name="segments">Closed segments in close order, or empty on failure.</param>
        /// <param name="error">Null on success; otherwise why segmentation failed.</param>
        /// <returns>True when one or more segments closed; false on explicit failure.</returns>
        public static bool TrySegment(
            IEnumerable<(int BatteryId, DateTime OriginatingTime)> spawns,
            IEnumerable<DateTime> finishes,
            out IReadOnlyList<BatterySegment> segments,
            out string error)
        {
            segments = Array.Empty<BatterySegment>();
            error = null;

            var spawnList = (spawns ?? Enumerable.Empty<(int, DateTime)>()).ToList();
            var finishList = (finishes ?? Enumerable.Empty<DateTime>()).ToList();

            // Unified timeline: Spawn before Finish on equal timestamps so a same-time Finish can close.
            // Ordinal preserves input order among equal-Time same-kind events (List.Sort is unstable).
            var timeline = new List<(bool IsSpawn, int BatteryId, DateTime Time, int Ordinal)>(
                spawnList.Count + finishList.Count);
            int ordinal = 0;
            foreach ((int batteryId, DateTime time) in spawnList)
            {
                timeline.Add((true, batteryId, time, ordinal++));
            }

            foreach (DateTime time in finishList)
            {
                timeline.Add((false, 0, time, ordinal++));
            }

            timeline.Sort((left, right) =>
            {
                int cmp = left.Time.CompareTo(right.Time);
                if (cmp != 0)
                {
                    return cmp;
                }

                // Spawns before Finishes at the same originating time.
                if (left.IsSpawn != right.IsSpawn)
                {
                    return left.IsSpawn ? -1 : 1;
                }

                // Same kind + equal time: keep earlier inputs earlier before LIFO push/pop.
                return left.Ordinal.CompareTo(right.Ordinal);
            });

            var openStack = new Stack<(int BatteryId, DateTime Start)>();
            var closed = new List<BatterySegment>();

            foreach ((bool isSpawn, int batteryId, DateTime time, int _) in timeline)
            {
                if (isSpawn)
                {
                    openStack.Push((batteryId, time));
                    continue;
                }

                if (openStack.Count == 0)
                {
                    error = "BatteryFinish arrived with an empty open-Spawn stack (Spawn→Finish LIFO); no invented Battery id.";
                    return false;
                }

                (int openId, DateTime start) = openStack.Pop();
                closed.Add(new BatterySegment(openId, start, time));
            }

            if (openStack.Count > 0)
            {
                string openIds = string.Join(", ", openStack.Select(entry => entry.BatteryId).Reverse());
                error = $"Unclosed BatterySpawn at session end (open BatteryId(s): {openIds}); Spawn→Finish rule requires a Finish for each Spawn.";
                return false;
            }

            if (closed.Count == 0)
            {
                error = "Zero Battery segments: BatterySpawn/BatteryFinish streams present but no events matched the Spawn→Finish start rule; refusing to invent a session Battery.";
                return false;
            }

            segments = closed;
            return true;
        }

        private static List<(int BatteryId, DateTime OriginatingTime)> ReadSpawnEvents(IStreamMetadata metadata)
        {
            var results = new List<(int, DateTime)>();
            using (Pipeline pipeline = Pipeline.Create(nameof(ReadSpawnEvents)))
            {
                PsiImporter store = PsiStore.Open(pipeline, metadata.StoreName, metadata.StorePath);
                store.OpenStream<PsiBatteryEvent>(metadata.Name)
                    .Do((evt, envelope) => results.Add((evt.BatteryId, envelope.OriginatingTime)));
                pipeline.Run(ReplayDescriptor.ReplayAll);
            }

            return results;
        }

        private static List<DateTime> ReadFinishEvents(IStreamMetadata metadata)
        {
            var results = new List<DateTime>();
            using (Pipeline pipeline = Pipeline.Create(nameof(ReadFinishEvents)))
            {
                PsiImporter store = PsiStore.Open(pipeline, metadata.StoreName, metadata.StorePath);
                store.OpenStream<PsiBatteryFinish>(metadata.Name)
                    .Do((evt, envelope) => results.Add(envelope.OriginatingTime));
                pipeline.Run(ReplayDescriptor.ReplayAll);
            }

            return results;
        }
    }
}
