// <copyright file="AlphaBetaPathPipelineRunner.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using SAAC.Logigram1;
    using SAAC.Logigram1.Logic;

    /// <summary>
    /// Sibling Analysis runner for <c>Logigram1.AlphaBetaPath</c> Idle State persist.
    /// Does not rewrite <see cref="FirstUnitPipelineRunner"/> / Gamma OT-merge / Outcome writer.
    /// When Gamma already wrote the analysis store, merges Idle States onto the shared
    /// <see cref="LogigramState.StreamName"/> without dropping Gamma State/Outcome (one persist surface).
    /// </summary>
    public static class AlphaBetaPathPipelineRunner
    {
        /// <summary>Partition / store name written into the analysis Dataset (shared with Gamma runner).</summary>
        public const string AnalysisStoreName = FirstUnitPipelineRunner.AnalysisStoreName;

        /// <summary>
        /// Runs AlphaBetaPath Idle emit and persists stamped <c>ab-idle:</c> State.
        /// When <paramref name="preserveExistingAnalysisStore"/> is true, rewrites the store with
        /// existing Gamma State/Outcome plus new Idle States (no clear-clobber of Gamma).
        /// When false, clear-before-write like the Gamma runner (AlphaBeta-only enable).
        /// </summary>
        /// <param name="batterySegments">Discovered Spawn→Finish segments for BatteryId stamping.</param>
        /// <param name="resolvedOutputPds">Validated absolute output <c>.pds</c> path.</param>
        /// <param name="inputStorePath">Absolute input Casper <c>.pds</c> (SESSION_ATTR session name).</param>
        /// <param name="preserveExistingAnalysisStore">
        /// True when Gamma already persisted into the same store root; merge Idle onto shared State.
        /// </param>
        /// <param name="scrubDelaySec">
        /// Signed seconds for Idle State OTs. When preserving, Gamma State/Outcome are already delayed;
        /// only new Idle messages are shifted here.
        /// </param>
        /// <exception cref="InvalidOperationException">Persist failure.</exception>
        public static void RunAndPersist(
            IReadOnlyList<BatterySegment> batterySegments,
            string resolvedOutputPds,
            string inputStorePath,
            bool preserveExistingAnalysisStore,
            double scrubDelaySec = 0)
        {
            if (string.IsNullOrWhiteSpace(resolvedOutputPds))
            {
                throw new ArgumentException("Resolved output Dataset path is required.", nameof(resolvedOutputPds));
            }

            IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> scopes =
                (batterySegments ?? Array.Empty<BatterySegment>())
                .Select(segment => (segment.BatteryId, segment.StartOriginatingTime, segment.EndOriginatingTime))
                .ToList();

            string storeRoot = AnalysisOutputPath.ResolveStoreRoot(resolvedOutputPds);

            var existingStates = new List<(LogigramState State, DateTime OriginatingTime)>();
            var existingOutcomes = new List<(LogigramOutcome Outcome, DateTime OriginatingTime)>();

            if (preserveExistingAnalysisStore)
            {
                if (Directory.Exists(storeRoot))
                {
                    LoadExistingAnalysisMessages(storeRoot, existingStates, existingOutcomes);
                }

                if (existingStates.Count == 0 && existingOutcomes.Count == 0)
                {
                    throw new InvalidOperationException(
                        "preserveExistingAnalysisStore requires existing Logigram1.State or Logigram1.Outcome; loaded both empty.");
                }
            }

            var idleStates = new List<(LogigramState State, DateTime OriginatingTime)>();
            DateTime idlePrimeTime = ResolveIdlePrimeTime(scopes);

            using (Pipeline pipeline = Pipeline.Create(nameof(AlphaBetaPathPipelineRunner) + "-Idle"))
            {
                var alphaBeta = new Logigram1AlphaBetaPathUnit(pipeline, scopes);
                alphaBeta.StateOut.Do((state, envelope) =>
                {
                    idleStates.Add((
                        new LogigramState
                        {
                            Status = state.Status,
                            BatteryId = state.BatteryId,
                            PersonId = state.PersonId,
                        },
                        envelope.OriginatingTime));
                });

                // Evidence reserved for later stories — not persisted this story.
                alphaBeta.EvidenceOut.Do(_ => { });

                Generators.Sequence(pipeline, new[] { (true, idlePrimeTime) })
                    .PipeTo(alphaBeta.AddIdlePrimeInput());

                RunFullSpeed(pipeline);

                if (alphaBeta.IdleEmitCount == 0)
                {
                    throw new InvalidOperationException(
                        "AlphaBetaPath Idle emit produced no State messages.");
                }
            }

            // Existing Gamma messages are already scrub-shifted when preserve=true; shift only new Idle.
            var shiftedIdle = ScrubTimeShift.ApplyAndOrder(idleStates, scrubDelaySec);
            var mergedStates = new List<(LogigramState State, DateTime OriginatingTime)>();
            mergedStates.AddRange(existingStates);
            mergedStates.AddRange(shiftedIdle);
            mergedStates = ScrubTimeShift.ApplyAndOrder(mergedStates, scrubDelaySec: 0);

            var shiftedOutcomes = ScrubTimeShift.ApplyAndOrder(existingOutcomes, scrubDelaySec: 0);

            string stagingRoot = storeRoot + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(stagingRoot);

                using (Pipeline pipeline = Pipeline.Create(nameof(AlphaBetaPathPipelineRunner) + "-Persist"))
                {
                    PsiExporter exporter = PsiStore.Create(pipeline, AnalysisStoreName, stagingRoot);

                    if (mergedStates.Count > 0)
                    {
                        Generators.Sequence(pipeline, mergedStates)
                            .Write(LogigramState.StreamName, exporter);
                    }

                    if (shiftedOutcomes.Count > 0)
                    {
                        Generators.Sequence(pipeline, shiftedOutcomes)
                            .Write(LogigramOutcome.StreamName, exporter);
                    }

                    RunFullSpeed(pipeline);
                }

                // Persist succeeded — replace live storeRoot only now.
                if (Directory.Exists(storeRoot))
                {
                    Directory.Delete(storeRoot, recursive: true);
                }

                Directory.Move(stagingRoot, storeRoot);
            }
            catch
            {
                if (Directory.Exists(stagingRoot))
                {
                    try
                    {
                        Directory.Delete(stagingRoot, recursive: true);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                throw;
            }

            SaveAnalysisDataset(resolvedOutputPds, storeRoot, inputStorePath);
        }

        private static DateTime ResolveIdlePrimeTime(
            IReadOnlyList<(int BatteryId, DateTime Start, DateTime End)> scopes)
        {
            if (scopes == null || scopes.Count == 0)
            {
                return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            DateTime earliest = scopes[0].Start;
            for (int i = 1; i < scopes.Count; i++)
            {
                if (scopes[i].Start < earliest)
                {
                    earliest = scopes[i].Start;
                }
            }

            return earliest != DateTime.MinValue
                ? earliest
                : new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private static void LoadExistingAnalysisMessages(
            string storeRoot,
            List<(LogigramState State, DateTime OriginatingTime)> states,
            List<(LogigramOutcome Outcome, DateTime OriginatingTime)> outcomes)
        {
            using (Pipeline loader = Pipeline.Create(nameof(AlphaBetaPathPipelineRunner) + "-LoadExisting"))
            {
                PsiImporter importer = PsiStore.Open(loader, AnalysisStoreName, storeRoot);
                bool hasState = importer.AvailableStreams != null
                    && importer.AvailableStreams.Any(stream =>
                        string.Equals(stream.Name, LogigramState.StreamName, StringComparison.Ordinal));
                bool hasOutcome = importer.AvailableStreams != null
                    && importer.AvailableStreams.Any(stream =>
                        string.Equals(stream.Name, LogigramOutcome.StreamName, StringComparison.Ordinal));

                if (hasState)
                {
                    importer.OpenStream<LogigramState>(LogigramState.StreamName)
                        .Do((state, envelope) => states.Add((
                            new LogigramState
                            {
                                Status = state.Status,
                                BatteryId = state.BatteryId,
                                PersonId = state.PersonId,
                            },
                            envelope.OriginatingTime)));
                }

                if (hasOutcome)
                {
                    importer.OpenStream<LogigramOutcome>(LogigramOutcome.StreamName)
                        .Do((outcome, envelope) => outcomes.Add((
                            new LogigramOutcome
                            {
                                Regulation = outcome.Regulation,
                                BatteryId = outcome.BatteryId,
                                PersonId = outcome.PersonId,
                            },
                            envelope.OriginatingTime)));
                }

                RunFullSpeed(loader);
            }
        }

        private static void RunFullSpeed(Pipeline pipeline)
        {
            Exception pipelineError = null;
            pipeline.PipelineExceptionNotHandled += (_, args) =>
            {
                pipelineError = args.Exception;
            };
            try
            {
                pipeline.Run(ReplayDescriptor.ReplayAll);
            }
            catch (AggregateException aggregate) when (pipelineError != null || aggregate.InnerExceptions.Count > 0)
            {
                Exception root = pipelineError ?? aggregate.Flatten().InnerException ?? aggregate;
                throw new InvalidOperationException(
                    $"AlphaBetaPath FullSpeed pipeline failed: {root.GetType().Name}: {root.Message}",
                    root);
            }

            if (pipelineError != null)
            {
                throw new InvalidOperationException(
                    $"AlphaBetaPath FullSpeed pipeline failed: {pipelineError.GetType().Name}: {pipelineError.Message}",
                    pipelineError);
            }
        }

        private static void SaveAnalysisDataset(
            string resolvedOutputPds,
            string storeRoot,
            string inputStorePath)
        {
            string directory = Path.GetDirectoryName(resolvedOutputPds);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string stagingPds = resolvedOutputPds + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                var dataset = new Dataset(
                    Path.GetFileNameWithoutExtension(resolvedOutputPds),
                    stagingPds,
                    autoSave: true);

                Session session = dataset.AddEmptySession(
                    FirstUnitPipelineRunner.FormatAnalysisSessionName(inputStorePath));
                session.AddPartitionFromPsiStoreAsync(AnalysisStoreName, storeRoot).GetAwaiter().GetResult();
                dataset.Save();

                if (File.Exists(resolvedOutputPds))
                {
                    File.Delete(resolvedOutputPds);
                }

                File.Move(stagingPds, resolvedOutputPds);
            }
            catch
            {
                if (File.Exists(stagingPds))
                {
                    try
                    {
                        File.Delete(stagingPds);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                throw;
            }
        }
    }
}
