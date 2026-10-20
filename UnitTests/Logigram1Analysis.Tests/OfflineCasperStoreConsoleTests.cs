// <copyright file="OfflineCasperStoreConsoleTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Logigram1Analysis;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Covers the I/O matrix for <see cref="OfflineCasperStoreConsole"/> including connector-load failure,
    /// <see cref="OfflineCasperStoreConsole.HasRecordedData"/> alternates, and StoreName partition fallback.
    /// </summary>
    [TestClass]
    public class OfflineCasperStoreConsoleTests
    {
        /// <summary>Missing <c>--store</c> prints usage and exits non-zero.</summary>
        [TestMethod]
        public void Run_WithoutStoreArgument_ReturnsNonZeroAndPrintsUsage()
        {
            var stderr = new StringWriter();
            int exit = OfflineCasperStoreConsole.Run(Array.Empty<string>(), new StringWriter(), stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), "--store");
        }

        /// <summary>Empty <c>--store</c> value prints usage and exits non-zero.</summary>
        [TestMethod]
        public void Run_WithEmptyStoreValue_ReturnsNonZeroAndPrintsUsage()
        {
            var stderr = new StringWriter();
            int exit = OfflineCasperStoreConsole.Run(new[] { "--store" }, new StringWriter(), stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), "--store");
        }

        /// <summary>Missing <c>.pds</c> path names the file and does not create an empty Dataset.</summary>
        [TestMethod]
        public void Run_WithMissingFile_ReturnsNonZeroAndNamesPath()
        {
            string missing = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-missing-" + Guid.NewGuid().ToString("N") + ".pds");
            var stderr = new StringWriter();

            int exit = OfflineCasperStoreConsole.Run(new[] { "--store", missing }, new StringWriter(), stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), missing);
            Assert.IsFalse(File.Exists(missing), "Must not create an empty .pds on missing path.");
        }

        /// <summary>Expe2-style session folder is rejected; a <c>.pds</c> file is required.</summary>
        [TestMethod]
        public void Run_WithSessionFolder_ReturnsNonZeroRequiringPds()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-folder-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--store", folder }, new StringWriter(), stderr);

                Assert.AreNotEqual(0, exit);
                StringAssert.Contains(stderr.ToString(), ".pds");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        /// <summary>Empty Dataset fails naming a missing required partition/stream.</summary>
        [TestMethod]
        public void Run_WithEmptyDataset_FailsNamingMissingRequiredStream()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-empty-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "Empty.pds");
            try
            {
                var dataset = new Dataset(Path.GetFileNameWithoutExtension(pds), pds, autoSave: true);
                dataset.Save();

                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--store", pds }, new StringWriter(), stderr);

                Assert.AreNotEqual(0, exit);
                StringAssert.Contains(stderr.ToString(), "Required stream missing:");
                Assert.IsTrue(
                    OfflineCasperStoreConsole.RequiredStreams.Any(required =>
                        stderr.ToString().Contains(required.Partition + "/" + required.Stream)),
                    stderr.ToString());
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Present but empty video stream (RequireData) fails naming partition/stream.</summary>
        [TestMethod]
        public void VerifyRequiredStreams_EmptyFullScreen_FailsNamingEmptyStream()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-zeromsg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "ZeroMsg.pds");
            try
            {
                Dataset dataset = CreatePreTestShapedDataset(folder, pds, fullScreenWithData: false, area1WithData: true);
                string error = OfflineCasperStoreConsole.VerifyRequiredStreams(dataset);

                Assert.IsNotNull(error, "Expected empty-stream failure.");
                StringAssert.Contains(error, "empty");
                StringAssert.Contains(error, "VideoRemoteApp/FullScreen");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Empty Area1 is OK (presence only) when video/audio have data.</summary>
        [TestMethod]
        public void VerifyRequiredStreams_EmptyArea1_PassesWhenVideoAudioHaveData()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-empty-area-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "EmptyArea.pds");
            try
            {
                Dataset dataset = CreatePreTestShapedDataset(folder, pds, fullScreenWithData: true, area1WithData: false);
                Assert.IsNull(OfflineCasperStoreConsole.VerifyRequiredStreams(dataset));
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Happy path: PreTest.081-shaped Dataset with required streams + data exits 0.</summary>
        [TestMethod]
        public void Run_WithRequiredStreamsAndData_ReturnsZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-happy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "Happy.pds");
            try
            {
                CreatePreTestShapedDataset(folder, pds, fullScreenWithData: true, area1WithData: false);

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--store", pds }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                StringAssert.Contains(stdout.ToString(), "connectors loaded");
                StringAssert.Contains(stdout.ToString(), "required streams OK");
                foreach ((string partition, string stream, bool _) in OfflineCasperStoreConsole.RequiredStreams)
                {
                    StringAssert.Contains(stdout.ToString(), partition + "/" + stream);
                }
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Corrupt <c>.pds</c> fails load with a clear error (no silent empty success).</summary>
        [TestMethod]
        public void Run_WithCorruptPds_ReturnsNonZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-corrupt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "Corrupt.pds");
            try
            {
                File.WriteAllText(pds, "not-a-valid-psi-dataset");

                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--store", pds }, new StringWriter(), stderr);

                Assert.AreNotEqual(0, exit);
                StringAssert.Contains(stderr.ToString(), "Failed to open dataset");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// CONNECTOR_LOAD=A: when <c>LoadDatasetAndConnectors</c> returns false, exit non-zero with a connector error
        /// (metadata alone is not enough; no warn-and-continue / exit 0).
        /// </summary>
        [TestMethod]
        public void Run_WhenConnectorsFailToLoad_ReturnsNonZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-connfail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "ConnFail.pds");
            try
            {
                // Dataset opens and would pass required-stream metadata; connector load is forced false.
                CreatePreTestShapedDataset(folder, pds, fullScreenWithData: true, area1WithData: true);

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(
                    new[] { "--store", pds },
                    stdout,
                    stderr,
                    loadConnectors: _ => false);

                Assert.AreNotEqual(0, exit, stderr.ToString() + stdout.ToString());
                StringAssert.Contains(stderr.ToString(), "connectors");
                Assert.IsFalse(
                    stdout.ToString().IndexOf("connectors loaded", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Success text must not claim connectors loaded when load failed.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Non-<c>.pds</c> paths are rejected before any Dataset open.</summary>
        [TestMethod]
        public void ValidateStorePath_RejectsNonPdsExtension()
        {
            string path = Path.Combine(Path.GetTempPath(), "not-a-dataset.txt");
            File.WriteAllText(path, "x");
            try
            {
                try
                {
                    OfflineCasperStoreConsole.ValidateStorePath(path);
                    Assert.Fail("Expected ArgumentException.");
                }
                catch (ArgumentException exception)
                {
                    StringAssert.Contains(exception.Message, ".pds");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Path API errors other than ArgumentException still exit non-zero with a message.</summary>
        [TestMethod]
        public void Run_WithInvalidPathCharacters_ReturnsNonZero()
        {
            // Pipe is illegal in Windows file names; GetFullPath / Exists path APIs fail loudly.
            string bad = "C:\\bad|path\\store.pds";
            var stderr = new StringWriter();
            int exit = OfflineCasperStoreConsole.Run(new[] { "--store", bad }, new StringWriter(), stderr);

            Assert.AreNotEqual(0, exit);
            Assert.IsFalse(string.IsNullOrWhiteSpace(stderr.ToString()));
        }

        /// <summary>MessageCount &gt; 0 is the preferred non-empty signal.</summary>
        [TestMethod]
        public void HasRecordedData_MessageCountPositive_ReturnsTrue()
        {
            var metadata = new FakeStreamMetadata { MessageCount = 3 };
            Assert.IsTrue(OfflineCasperStoreConsole.HasRecordedData(metadata));
            StringAssert.Contains(OfflineCasperStoreConsole.FormatDataSignal(metadata), "3 message");
        }

        /// <summary>AverageMessageSize &gt; 0 passes when MessageCount is 0.</summary>
        [TestMethod]
        public void HasRecordedData_AverageMessageSizePositive_ReturnsTrue()
        {
            var metadata = new FakeStreamMetadata { MessageCount = 0, AverageMessageSize = 16 };
            Assert.IsTrue(OfflineCasperStoreConsole.HasRecordedData(metadata));
            string signal = OfflineCasperStoreConsole.FormatDataSignal(metadata);
            StringAssert.Contains(signal, "avg size");
            Assert.IsFalse(signal.StartsWith("0 message", StringComparison.Ordinal), "Printed signal must not contradict size fallback.");
        }

        /// <summary>Originating-time span with Last &gt; First passes when count and size are empty.</summary>
        [TestMethod]
        public void HasRecordedData_LastGreaterThanFirst_ReturnsTrue()
        {
            DateTime first = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            var metadata = new FakeStreamMetadata
            {
                MessageCount = 0,
                AverageMessageSize = 0,
                FirstMessageOriginatingTime = first,
                LastMessageOriginatingTime = first.AddSeconds(1),
            };
            Assert.IsTrue(OfflineCasperStoreConsole.HasRecordedData(metadata));
            StringAssert.Contains(OfflineCasperStoreConsole.FormatDataSignal(metadata), "originating time span");
        }

        /// <summary>First == Last originating time is not treated as non-empty (strict Last &gt; First).</summary>
        [TestMethod]
        public void HasRecordedData_FirstEqualsLast_ReturnsFalse()
        {
            DateTime stamp = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            var metadata = new FakeStreamMetadata
            {
                MessageCount = 0,
                AverageMessageSize = 0,
                FirstMessageOriginatingTime = stamp,
                LastMessageOriginatingTime = stamp,
            };
            Assert.IsFalse(OfflineCasperStoreConsole.HasRecordedData(metadata));
        }

        /// <summary>Last = DateTime.MaxValue is a sentinel, not a non-empty time span.</summary>
        [TestMethod]
        public void HasRecordedData_LastIsMaxValue_ReturnsFalse()
        {
            DateTime first = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            var metadata = new FakeStreamMetadata
            {
                MessageCount = 0,
                AverageMessageSize = 0,
                FirstMessageOriginatingTime = first,
                LastMessageOriginatingTime = DateTime.MaxValue,
            };
            Assert.IsFalse(OfflineCasperStoreConsole.HasRecordedData(metadata));
        }

        /// <summary>First = DateTime.MinValue is a sentinel, not a non-empty time span.</summary>
        [TestMethod]
        public void HasRecordedData_FirstIsMinValue_ReturnsFalse()
        {
            DateTime last = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            var metadata = new FakeStreamMetadata
            {
                MessageCount = 0,
                AverageMessageSize = 0,
                FirstMessageOriginatingTime = DateTime.MinValue,
                LastMessageOriginatingTime = last,
            };
            Assert.IsFalse(OfflineCasperStoreConsole.HasRecordedData(metadata));
        }

        /// <summary>Partition display name mismatch still matches via stream StoreName.</summary>
        [TestMethod]
        public void PartitionMatches_StoreNameFallback_FindsRequiredStreams()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-storename-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "StoreName.pds");
            try
            {
                Dataset dataset = CreatePreTestShapedDatasetWithMismatchedPartitionNames(folder, pds);

                // Partition display names differ; StoreName on streams still matches required partition keys.
                Assert.IsNotNull(
                    OfflineCasperStoreConsole.FindStreamMetadata(dataset, "VideoRemoteApp", "FullScreen"),
                    "Expected StoreName fallback to resolve VideoRemoteApp/FullScreen.");
                Assert.IsNull(OfflineCasperStoreConsole.VerifyRequiredStreams(dataset));
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// Builds a PreTest.081-shaped Dataset.
        /// </summary>
        private static Dataset CreatePreTestShapedDataset(string folder, string pdsPath, bool fullScreenWithData, bool area1WithData)
        {
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

            WriteStore(folder, "VideoRemoteApp", new[] { ("FullScreen", fullScreenWithData) }, start);
            WriteStore(folder, "WhisperStreaming", new[] { ("Audio_User_1", true), ("Audio_User_2", true) }, start);
            WriteStore(folder, "Server", new[] { ("Area1", area1WithData), ("Area2", true) }, start);

            var dataset = new Dataset(Path.GetFileNameWithoutExtension(pdsPath), pdsPath, autoSave: true);
            Session session = dataset.AddEmptySession("Session");
            session.AddPartitionFromPsiStoreAsync("VideoRemoteApp", Path.Combine(folder, "VideoRemoteApp")).GetAwaiter().GetResult();
            session.AddPartitionFromPsiStoreAsync("WhisperStreaming", Path.Combine(folder, "WhisperStreaming")).GetAwaiter().GetResult();
            session.AddPartitionFromPsiStoreAsync("Server", Path.Combine(folder, "Server")).GetAwaiter().GetResult();
            dataset.Save();
            return Dataset.Load(pdsPath);
        }

        /// <summary>
        /// Same stores as PreTest, but partition display names intentionally differ from required keys
        /// so matching must use stream <c>StoreName</c>.
        /// Signature: AddPartitionFromPsiStoreAsync(storeName, storePath, partitionName).
        /// </summary>
        private static Dataset CreatePreTestShapedDatasetWithMismatchedPartitionNames(string folder, string pdsPath)
        {
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

            // Stores live under a shared root; store names remain VideoRemoteApp / WhisperStreaming / Server.
            WriteStore(folder, "VideoRemoteApp", new[] { ("FullScreen", true) }, start, storeRoot: folder);
            WriteStore(folder, "WhisperStreaming", new[] { ("Audio_User_1", true), ("Audio_User_2", true) }, start, storeRoot: folder);
            WriteStore(folder, "Server", new[] { ("Area1", true), ("Area2", true) }, start, storeRoot: folder);

            var dataset = new Dataset(Path.GetFileNameWithoutExtension(pdsPath), pdsPath, autoSave: true);
            Session session = dataset.AddEmptySession("Session");
            session.AddPartitionFromPsiStoreAsync("VideoRemoteApp", folder, "VideoPartitionAlias").GetAwaiter().GetResult();
            session.AddPartitionFromPsiStoreAsync("WhisperStreaming", folder, "WhisperPartitionAlias").GetAwaiter().GetResult();
            session.AddPartitionFromPsiStoreAsync("Server", folder, "ServerPartitionAlias").GetAwaiter().GetResult();
            dataset.Save();
            return Dataset.Load(pdsPath);
        }

        private static void WriteStore(
            string folder,
            string storeName,
            IEnumerable<(string Stream, bool WithData)> streams,
            DateTime start,
            string storeRoot = null)
        {
            string storePath = storeRoot ?? Path.Combine(folder, storeName);
            Directory.CreateDirectory(storePath);

            using (Pipeline pipeline = Pipeline.Create("Logigram1AnalysisWrite-" + storeName))
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
                // Temp cleanup best-effort.
            }
            catch (UnauthorizedAccessException)
            {
                // Temp cleanup best-effort.
            }
        }
    }
}
