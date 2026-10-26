// <copyright file="BatteryBoundaryDiscoveryTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Logigram1Analysis;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SAAC.PsiFormats;

    /// <summary>
    /// I/O matrix for Spawn→Finish FIFO Battery boundary discovery (AD-6 / FR5).
    /// </summary>
    [TestClass]
    public class BatteryBoundaryDiscoveryTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Multi-Battery store: distinct segments, no N↔M merge (FIFO close order).</summary>
        [TestMethod]
        public void TrySegment_MultiBattery_ReturnsDistinctScopesInCloseOrder()
        {
            var spawns = new List<(int, DateTime)>
            {
                (10, T0),
                (20, T0.AddSeconds(5)),
            };
            var finishes = new List<DateTime>
            {
                T0.AddSeconds(10),
                T0.AddSeconds(15),
            };

            Assert.IsTrue(
                BatteryBoundaryDiscovery.TrySegment(spawns, finishes, out IReadOnlyList<BatterySegment> segments, out string error),
                error);

            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual(10, segments[0].BatteryId);
            Assert.AreEqual(T0, segments[0].StartOriginatingTime);
            Assert.AreEqual(T0.AddSeconds(10), segments[0].EndOriginatingTime);
            Assert.AreEqual(20, segments[1].BatteryId);
            Assert.AreEqual(T0.AddSeconds(5), segments[1].StartOriginatingTime);
            Assert.AreEqual(T0.AddSeconds(15), segments[1].EndOriginatingTime);
            Assert.IsNull(error);
        }

        /// <summary>FIFO overlap: Finish closes the oldest open Spawn; later Spawn stays open.</summary>
        [TestMethod]
        public void TrySegment_FifoOverlap_FinishClosesOldestOnly()
        {
            var spawns = new List<(int, DateTime)>
            {
                (1, T0),
                (2, T0.AddSeconds(1)),
            };
            var finishes = new List<DateTime>
            {
                T0.AddSeconds(2),
                T0.AddSeconds(3),
            };

            Assert.IsTrue(BatteryBoundaryDiscovery.TrySegment(spawns, finishes, out IReadOnlyList<BatterySegment> segments, out _));

            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual(1, segments[0].BatteryId);
            Assert.AreEqual(2, segments[1].BatteryId);
        }

        /// <summary>Same originating time: Spawn sorts before Finish so the segment closes.</summary>
        [TestMethod]
        public void TrySegment_SameOriginatingTime_SpawnBeforeFinish_ClosesSegment()
        {
            var spawns = new List<(int, DateTime)> { (9, T0) };
            var finishes = new List<DateTime> { T0 };

            Assert.IsTrue(
                BatteryBoundaryDiscovery.TrySegment(spawns, finishes, out IReadOnlyList<BatterySegment> segments, out string error),
                error);

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual(9, segments[0].BatteryId);
            Assert.AreEqual(T0, segments[0].StartOriginatingTime);
            Assert.AreEqual(T0, segments[0].EndOriginatingTime);
        }

        /// <summary>Equal-time Spawns preserve input order: first Spawn is closed first (FIFO).</summary>
        [TestMethod]
        public void TrySegment_EqualTimeSpawns_PreserveInputOrder_FirstClosedFirst()
        {
            var spawns = new List<(int, DateTime)>
            {
                (1, T0),
                (2, T0),
            };
            var finishes = new List<DateTime>
            {
                T0.AddSeconds(1),
                T0.AddSeconds(2),
            };

            Assert.IsTrue(
                BatteryBoundaryDiscovery.TrySegment(spawns, finishes, out IReadOnlyList<BatterySegment> segments, out string error),
                error);

            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual(1, segments[0].BatteryId);
            Assert.AreEqual(2, segments[1].BatteryId);
        }

        /// <summary>Finish with empty queue → explicit fail; no invented id.</summary>
        [TestMethod]
        public void TrySegment_FinishWithEmptyQueue_FailsExplicitly()
        {
            var finishes = new List<DateTime> { T0 };

            Assert.IsFalse(
                BatteryBoundaryDiscovery.TrySegment(
                    Array.Empty<(int, DateTime)>(),
                    finishes,
                    out IReadOnlyList<BatterySegment> segments,
                    out string error));

            Assert.AreEqual(0, segments.Count);
            StringAssert.Contains(error, "empty open-Spawn queue");
        }

        /// <summary>Unclosed Spawn at end → explicit fail listing remaining open BatteryId(s) (FIFO closed the oldest).</summary>
        [TestMethod]
        public void TrySegment_UnclosedSpawn_FailsListingOpenIds()
        {
            var spawns = new List<(int, DateTime)>
            {
                (3, T0),
                (4, T0.AddSeconds(1)),
            };
            var finishes = new List<DateTime> { T0.AddSeconds(2) };

            Assert.IsFalse(
                BatteryBoundaryDiscovery.TrySegment(spawns, finishes, out IReadOnlyList<BatterySegment> segments, out string error));

            Assert.AreEqual(0, segments.Count);
            StringAssert.Contains(error, "Unclosed BatterySpawn");
            // One Finish closes oldest Spawn (3); only Battery 4 remains open.
            StringAssert.Contains(error, "open BatteryId(s): 4");
        }

        /// <summary>Zero segments (streams conceptually present, no matching start events) → fail.</summary>
        [TestMethod]
        public void TrySegment_ZeroEvents_FailsWithoutInventingSessionBattery()
        {
            Assert.IsFalse(
                BatteryBoundaryDiscovery.TrySegment(
                    Array.Empty<(int, DateTime)>(),
                    Array.Empty<DateTime>(),
                    out IReadOnlyList<BatterySegment> segments,
                    out string error));

            Assert.AreEqual(0, segments.Count);
            StringAssert.Contains(error, "Zero Battery segments");
        }

        /// <summary>Console: missing BatterySpawn stream → non-zero exit and stderr names the topic.</summary>
        [TestMethod]
        public void Run_MissingBatterySpawn_ReturnsNonZeroAndNamesTopic()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1-battery-missing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "MissingBattery.pds");
            try
            {
                CreateDatasetWithoutBatteryStreams(folder, pds);

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--store", pds }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                StringAssert.Contains(stderr.ToString(), BatteryBoundaryDiscovery.SpawnStreamName);
                Assert.IsFalse(
                    stdout.ToString().IndexOf("Battery segments", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Must not claim Battery segments when discovery failed.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Console: BatterySpawn present but BatteryFinish absent → non-zero exit naming Finish.</summary>
        [TestMethod]
        public void Run_MissingBatteryFinish_ReturnsNonZeroAndNamesTopic()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1-battery-nofinish-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "MissingFinish.pds");
            try
            {
                CreateDatasetWithSpawnOnly(folder, pds);

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--store", pds }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                StringAssert.Contains(stderr.ToString(), BatteryBoundaryDiscovery.FinishStreamName);
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Console: empty Battery streams → zero-segment fail.</summary>
        [TestMethod]
        public void Run_EmptyBatteryStreams_ReturnsNonZeroZeroSegment()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1-battery-empty-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "EmptyBattery.pds");
            try
            {
                CreateDatasetWithEmptyBatteryStreams(folder, pds);

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--store", pds }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                StringAssert.Contains(stderr.ToString(), "Zero Battery segments");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Console: multi-Battery store lists distinct segment ids.</summary>
        [TestMethod]
        public void Run_MultiBatteryStore_ListsDistinctSegments()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1-battery-multi-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "MultiBattery.pds");
            try
            {
                DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
                CreateDatasetWithBatterySegments(
                    folder,
                    pds,
                    new List<(int, DateTime, DateTime)>
                    {
                        (11, start, start.AddSeconds(2)),
                        (22, start.AddSeconds(3), start.AddSeconds(4)),
                    });

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--store", pds }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                string outText = stdout.ToString();
                StringAssert.Contains(outText, "Battery segments (2)");
                StringAssert.Contains(outText, "BatteryId=11");
                StringAssert.Contains(outText, "BatteryId=22");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        private static void CreateDatasetWithoutBatteryStreams(string folder, string pdsPath)
        {
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            WriteIntStore(folder, "VideoRemoteApp", new[] { ("FullScreen", true) }, start);
            WriteIntStore(folder, "WhisperStreaming", new[] { ("Audio_User_1", true), ("Audio_User_2", true) }, start);
            WriteIntStore(folder, "Server", new[] { ("Area1", false), ("Area2", true) }, start);
            SaveDataset(folder, pdsPath);
        }

        private static void CreateDatasetWithSpawnOnly(string folder, string pdsPath)
        {
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            WriteIntStore(folder, "VideoRemoteApp", new[] { ("FullScreen", true) }, start);
            WriteIntStore(folder, "WhisperStreaming", new[] { ("Audio_User_1", true), ("Audio_User_2", true) }, start);

            string storePath = Path.Combine(folder, "Server");
            Directory.CreateDirectory(storePath);
            using (Pipeline pipeline = Pipeline.Create("BatteryDiscoveryWrite-SpawnOnly"))
            {
                PsiExporter store = PsiStore.Create(pipeline, "Server", storePath);
                Generators.Sequence(pipeline, new[] { (1, start) }).Write("Area1", store);
                Generators.Sequence(pipeline, new[] { (1, start) }).Write("Area2", store);
                Generators.Sequence(
                        pipeline,
                        new[] { (new PsiBatteryEvent(1, 0, 0, false), start) })
                    .Write(BatteryBoundaryDiscovery.SpawnStreamName, store);
                pipeline.Run();
            }

            SaveDataset(folder, pdsPath);
        }

        private static void CreateDatasetWithEmptyBatteryStreams(string folder, string pdsPath)
        {
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            WriteIntStore(folder, "VideoRemoteApp", new[] { ("FullScreen", true) }, start);
            WriteIntStore(folder, "WhisperStreaming", new[] { ("Audio_User_1", true), ("Audio_User_2", true) }, start);
            WriteServerWithBatteries(folder, start, segments: null);
            SaveDataset(folder, pdsPath);
        }

        private static void CreateDatasetWithBatterySegments(
            string folder,
            string pdsPath,
            IReadOnlyList<(int BatteryId, DateTime Spawn, DateTime Finish)> segments)
        {
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            WriteIntStore(folder, "VideoRemoteApp", new[] { ("FullScreen", true) }, start);
            WriteIntStore(folder, "WhisperStreaming", new[] { ("Audio_User_1", true), ("Audio_User_2", true) }, start);
            WriteServerWithBatteries(folder, start, segments);
            SaveDataset(folder, pdsPath);
        }

        private static void SaveDataset(string folder, string pdsPath)
        {
            var dataset = new Dataset(Path.GetFileNameWithoutExtension(pdsPath), pdsPath, autoSave: true);
            Session session = dataset.AddEmptySession("Session");
            session.AddPartitionFromPsiStoreAsync("VideoRemoteApp", Path.Combine(folder, "VideoRemoteApp")).GetAwaiter().GetResult();
            session.AddPartitionFromPsiStoreAsync("WhisperStreaming", Path.Combine(folder, "WhisperStreaming")).GetAwaiter().GetResult();
            session.AddPartitionFromPsiStoreAsync("Server", Path.Combine(folder, "Server")).GetAwaiter().GetResult();
            dataset.Save();
        }

        private static void WriteIntStore(
            string folder,
            string storeName,
            IEnumerable<(string Stream, bool WithData)> streams,
            DateTime start)
        {
            string storePath = Path.Combine(folder, storeName);
            Directory.CreateDirectory(storePath);

            using (Pipeline pipeline = Pipeline.Create("BatteryDiscoveryWrite-" + storeName))
            {
                PsiExporter store = PsiStore.Create(pipeline, storeName, storePath);
                foreach ((string streamName, bool withData) in streams)
                {
                    IEnumerable<(int, DateTime)> values = withData
                        ? new[] { (1, start) }
                        : Array.Empty<(int, DateTime)>();
                    Generators.Sequence(pipeline, values).Write(streamName, store);
                }

                pipeline.Run();
            }
        }

        private static void WriteServerWithBatteries(
            string folder,
            DateTime start,
            IReadOnlyList<(int BatteryId, DateTime Spawn, DateTime Finish)> segments)
        {
            string storePath = Path.Combine(folder, "Server");
            Directory.CreateDirectory(storePath);

            using (Pipeline pipeline = Pipeline.Create("BatteryDiscoveryWrite-Server"))
            {
                PsiExporter store = PsiStore.Create(pipeline, "Server", storePath);
                Generators.Sequence(pipeline, new[] { (1, start) }).Write("Area1", store);
                Generators.Sequence(pipeline, new[] { (1, start) }).Write("Area2", store);

                var spawnEvents = new List<(PsiBatteryEvent, DateTime)>();
                var finishEvents = new List<(PsiBatteryFinish, DateTime)>();
                if (segments != null)
                {
                    foreach ((int batteryId, DateTime spawn, DateTime finish) in segments)
                    {
                        spawnEvents.Add((new PsiBatteryEvent(batteryId, 0, 0, false), spawn));
                        finishEvents.Add((default(PsiBatteryFinish), finish));
                    }
                }

                Generators.Sequence(pipeline, spawnEvents).Write(BatteryBoundaryDiscovery.SpawnStreamName, store);
                Generators.Sequence(pipeline, finishEvents).Write(BatteryBoundaryDiscovery.FinishStreamName, store);

                pipeline.Run();
            }
        }

        private static void TryDeleteDirectory(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
