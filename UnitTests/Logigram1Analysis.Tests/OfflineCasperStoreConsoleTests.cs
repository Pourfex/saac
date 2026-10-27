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
    using System.Numerics;
    using Logigram1Analysis;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SAAC.Logigram1;
    using SAAC.PsiFormats;

    /// <summary>
    /// Covers the I/O matrix for <see cref="OfflineCasperStoreConsole"/> including config / store resolution,
    /// session identity, connector-load failure, <see cref="OfflineCasperStoreConsole.HasRecordedData"/> alternates,
    /// and StoreName partition fallback.
    /// </summary>
    /// <remarks>
    /// Psi FullSpeed pipelines are not safe under MSTest method parallelization — keep this class sequential.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class OfflineCasperStoreConsoleTests
    {
        /// <summary>Zero args prints usage mentioning <c>--config</c> / <c>--store</c> and exits non-zero (no auto-load).</summary>
        [TestMethod]
        public void Run_WithoutStoreArgument_ReturnsNonZeroAndPrintsUsage()
        {
            var stderr = new StringWriter();
            int exit = OfflineCasperStoreConsole.Run(Array.Empty<string>(), new StringWriter(), stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), "--store");
            StringAssert.Contains(stderr.ToString(), "--config");
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

        /// <summary>
        /// Empty <c>--store</c> beside a valid <c>--config</c> fails as missing store value and does not open the config store.
        /// </summary>
        [TestMethod]
        public void Run_WithEmptyStoreBesideValidConfig_ReturnsNonZeroWithoutOpeningConfigStore()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-emptystore-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "FromConfig.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(folder, pds, fullScreenWithData: true, area1WithData: false);
                File.WriteAllText(configPath, "{ \"StorePath\": " + JsonString(pds) + " }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(
                    new[] { "--store", "--config", configPath },
                    stdout,
                    stderr);

                Assert.AreNotEqual(0, exit, stderr.ToString());
                StringAssert.Contains(stderr.ToString(), "Missing value for --store");
                StringAssert.Contains(stderr.ToString(), OfflineCasperStoreConsole.Usage);
                Assert.IsFalse(
                    stdout.ToString().IndexOf("connectors loaded", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Must not open the config store when --store has no value.");
                Assert.IsFalse(
                    stdout.ToString().IndexOf("FromConfig.pds", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Must not log identity for the config StorePath when --store is empty.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Missing <c>.pds</c> path names the file, logs session identity, and does not create an empty Dataset.</summary>
        [TestMethod]
        public void Run_WithMissingFile_ReturnsNonZeroAndNamesPath()
        {
            string missing = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-missing-" + Guid.NewGuid().ToString("N") + ".pds");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            int exit = OfflineCasperStoreConsole.Run(new[] { "--store", missing }, stdout, stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), missing);
            AssertSessionIdentity(stdout.ToString(), missing);
            Assert.IsFalse(File.Exists(missing), "Must not create an empty .pds on missing path.");
        }

        /// <summary><c>--config</c> with StorePath opens that store and logs session identity.</summary>
        [TestMethod]
        public void Run_WithConfigStorePath_ReturnsZeroAndLogsIdentity()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "FromConfig.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(folder, pds, fullScreenWithData: true, area1WithData: false);
                File.WriteAllText(configPath, "{ \"StorePath\": " + JsonString(pds) + " }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                StringAssert.Contains(stdout.ToString(), "connectors loaded");
                StringAssert.Contains(stdout.ToString(), "Battery segments");
                StringAssert.Contains(stdout.ToString(), "BatteryId=1");
                AssertSessionIdentity(stdout.ToString(), pds);
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary><c>--store</c> overrides <c>StorePath</c> from <c>--config</c>.</summary>
        [TestMethod]
        public void Run_WithStoreOverride_OpensStoreNotConfigPath()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-override-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string configOnlyPds = Path.Combine(folder, "ConfigOnly.pds");
            string storeOverridePds = Path.Combine(folder, "StoreOverride.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(folder, storeOverridePds, fullScreenWithData: true, area1WithData: false);

                // Config points at a missing path; store flag must win without requiring that file.
                File.WriteAllText(configPath, "{ \"StorePath\": " + JsonString(configOnlyPds) + " }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(
                    new[] { "--config", configPath, "--store", storeOverridePds },
                    stdout,
                    stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                AssertSessionIdentity(stdout.ToString(), storeOverridePds);
                Assert.IsFalse(
                    stdout.ToString().IndexOf("ConfigOnly.pds", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Identity must name the --store path, not the config StorePath.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// <c>--store</c> wins for path, but <c>EnabledUnits</c> from <c>--config</c> still drive Logic-topic checks.
        /// </summary>
        [TestMethod]
        public void Run_WithStoreOverride_StillAppliesConfigEnabledUnitsLogicTopicCheck()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-override-units-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string configOnlyPds = Path.Combine(folder, "ConfigOnly.pds");
            string storeOverridePds = Path.Combine(folder, "StoreOverride.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                // Substrate-only store via --store; config EnabledUnits must still require FirstUnit topics.
                CreatePreTestShapedDataset(folder, storeOverridePds, fullScreenWithData: true, area1WithData: false);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(configOnlyPds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(
                    new[] { "--config", configPath, "--store", storeOverridePds },
                    stdout,
                    stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                string err = stderr.ToString();
                // Area1/Area2 exist as substrate streams on PreTest stores — not reported as Logic gaps.
                foreach (string topic in Logigram1UnitTopicRegistry.GammaPathTopics)
                {
                    if (string.Equals(topic, "Area1", StringComparison.Ordinal)
                        || string.Equals(topic, "Area2", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    StringAssert.Contains(err, "Required Logic topic missing: " + topic);
                }

                Assert.IsFalse(
                    stdout.ToString().IndexOf("Required Logic topics OK", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Must not claim Logic topics OK when gaps remain.");
                Assert.IsFalse(
                    stdout.ToString().IndexOf("connectors loaded", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Must not print overall open success when Logic topic gaps remain.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Missing config file exits non-zero without opening a pipeline.</summary>
        [TestMethod]
        public void Run_WithMissingConfig_ReturnsNonZero()
        {
            string missing = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-missing-config-" + Guid.NewGuid().ToString("N") + ".json");
            var stderr = new StringWriter();
            int exit = OfflineCasperStoreConsole.Run(new[] { "--config", missing }, new StringWriter(), stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), "Configuration file not found");
        }

        /// <summary>Invalid config JSON exits non-zero without opening a pipeline.</summary>
        [TestMethod]
        public void Run_WithInvalidConfigJson_ReturnsNonZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-badjson-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string configPath = Path.Combine(folder, "bad.json");
            try
            {
                File.WriteAllText(configPath, "{ not valid json");

                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, new StringWriter(), stderr);

                Assert.AreNotEqual(0, exit);
                StringAssert.Contains(stderr.ToString(), "Invalid configuration JSON");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Config with empty StorePath exits non-zero.</summary>
        [TestMethod]
        public void Run_WithConfigMissingStorePath_ReturnsNonZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-emptystore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string configPath = Path.Combine(folder, "empty-store.json");
            try
            {
                File.WriteAllText(configPath, "{ \"StorePath\": \"\" }");

                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, new StringWriter(), stderr);

                Assert.AreNotEqual(0, exit);
                StringAssert.Contains(stderr.ToString(), "StorePath");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Empty <c>--config</c> value prints usage and exits non-zero.</summary>
        [TestMethod]
        public void Run_WithEmptyConfigValue_ReturnsNonZeroAndPrintsUsage()
        {
            var stderr = new StringWriter();
            int exit = OfflineCasperStoreConsole.Run(new[] { "--config" }, new StringWriter(), stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), "--config");
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
                AssertSessionIdentity(stdout.ToString(), pds);
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
        /// All FirstUnit AD-7 topics present: stdout lists each as present; substrate OK; exit 0.
        /// </summary>
        [TestMethod]
        public void Run_WithFirstUnitTopicsPresent_ListsPresentAndReturnsZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-topics-ok-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "TopicsOk.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                StringAssert.Contains(stdout.ToString(), "Required Logic topics OK:");
                foreach (string topic in Logigram1UnitTopicRegistry.GammaPathTopics)
                {
                    StringAssert.Contains(stdout.ToString(), topic + ": present");
                }

                Assert.IsFalse(
                    stderr.ToString().IndexOf("Required Logic topic missing", StringComparison.Ordinal) >= 0,
                    stderr.ToString());

                string expectedSibling = Path.Combine(
                    folder,
                    Path.GetFileNameWithoutExtension(pds) + AnalysisOutputPath.AutoSiblingSuffix);
                StringAssert.Contains(stdout.ToString(), "Analysis output Dataset:");
                StringAssert.Contains(stdout.ToString(), expectedSibling);
                Assert.IsTrue(File.Exists(expectedSibling), "Default sibling analysis Dataset must be written.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// FirstUnit + OutputPath override writes to the override path and prints it.
        /// </summary>
        [TestMethod]
        public void Run_WithFirstUnitAndOutputPathOverride_WritesOverrideAndPrintsPath()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-out-override-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "Input.pds");
            string overridePds = Path.Combine(folder, "CustomAnalysis.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ]"
                    + ", \"OutputPath\": " + JsonString(overridePds) + " }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                StringAssert.Contains(stdout.ToString(), "Analysis output Dataset:");
                StringAssert.Contains(stdout.ToString(), overridePds);
                Assert.IsTrue(File.Exists(overridePds));
                Assert.IsFalse(
                    File.Exists(Path.Combine(folder, "Input.Logigram1Analysis.pds")),
                    "Override must not also write the auto sibling.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// OutputPath equal to input Dataset is rejected; input untouched; exit non-zero.
        /// </summary>
        [TestMethod]
        public void Run_WithOutputPathEqualToInput_RejectsAndLeavesInputUntouched()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-out-overwrite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "Same.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics);
                DateTime before = File.GetLastWriteTimeUtc(pds);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ]"
                    + ", \"OutputPath\": " + JsonString(pds) + " }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                StringAssert.Contains(stderr.ToString(), "must not overwrite the input Dataset");
                Assert.IsTrue(File.Exists(pds));
                Assert.AreEqual(before, File.GetLastWriteTimeUtc(pds), "Input Dataset must remain untouched.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// Unwritable analysis output parent → stderr explain; exit non-zero; input untouched.
        /// </summary>
        [TestMethod]
        public void Run_WithUnwritableOutputPath_ReturnsNonZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-out-unwritable-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "Input.pds");
            string blocker = Path.Combine(folder, "NotADirectory");
            File.WriteAllText(blocker, "file-not-dir");
            string badOutput = Path.Combine(blocker, "out.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ]"
                    + ", \"OutputPath\": " + JsonString(badOutput) + " }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                string err = stderr.ToString();
                Assert.IsTrue(
                    err.IndexOf("not writable", StringComparison.OrdinalIgnoreCase) >= 0
                    || err.IndexOf("not a directory", StringComparison.OrdinalIgnoreCase) >= 0
                    || err.IndexOf("parent path is a file", StringComparison.OrdinalIgnoreCase) >= 0,
                    err);
                Assert.IsFalse(
                    stdout.ToString().IndexOf("Analysis output Dataset:", StringComparison.Ordinal) >= 0);
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// Persisted analysis State carries BatteryId from Spawn→Finish scope.
        /// </summary>
        [TestMethod]
        public void Run_WithFirstUnit_PersistsBatteryKeyedState()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-battery-state-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "BatteryState.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            try
            {
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics,
                    batterySegments: new List<(int, DateTime, DateTime)>
                    {
                        (42, start, start.AddSeconds(5)),
                    });

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                string outputPds = Path.Combine(folder, "BatteryState.Logigram1Analysis.pds");
                Assert.IsTrue(File.Exists(outputPds), stdout.ToString());

                var states = ReadAnalysisStates(outputPds);
                Assert.IsTrue(states.Count > 0, "Expected at least one Logigram1.State message.");
                Assert.IsTrue(
                    states.Any(state => state.BatteryId == 42),
                    "Expected BatteryId=42 on persisted State.");
                Assert.IsTrue(
                    states.Any(state =>
                        state.Status != null
                        && state.Status.IndexOf("node-1", StringComparison.Ordinal) >= 0),
                    "Expected node-1 Status from Module status derivation.");
                Assert.IsTrue(
                    states.Any(state =>
                        state.Status != null
                        && state.Status.IndexOf("node-3:", StringComparison.Ordinal) >= 0),
                    "Expected node-3 Status after unused 2s window + door-closed leave.");

                var outcomes = ReadAnalysisOutcomes(outputPds);
                Assert.IsTrue(
                    outcomes.Any(outcome =>
                        outcome.Regulation == LogigramRegulation.Gamma && outcome.BatteryId == 42),
                    "Expected at least one persisted Gamma Outcome with BatteryId=42.");

                Dataset analysisDataset = Dataset.Load(outputPds);
                Assert.IsNotNull(
                    OfflineCasperStoreConsole.FindStreamByExactName(
                        analysisDataset,
                        LogigramOutcome.StreamName),
                    "Expected Logigram1.Outcome stream in analysis Dataset.");
                AssertAnalysisSessionName(analysisDataset, pds);
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// Path-only retarget (FR4 / AD-9): same EnabledUnits on two distinct PreTest-shaped stores yields
        /// distinct stdout + Dataset session attribution, sibling analysis paths, and Battery-scoped State.
        /// </summary>
        [TestMethod]
        public void Run_WithTwoStores_SameEnabledUnits_DistinctAttributionAndBatteryState()
        {
            string root = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-dual-store-" + Guid.NewGuid().ToString("N"));
            string folderA = Path.Combine(root, "StoreA");
            string folderB = Path.Combine(root, "StoreB");
            Directory.CreateDirectory(folderA);
            Directory.CreateDirectory(folderB);
            string pdsA = Path.Combine(folderA, "FirstStore.pds");
            string pdsB = Path.Combine(folderB, "SecondStore.pds");
            string configPath = Path.Combine(root, "logigram1.json");
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            try
            {
                CreatePreTestShapedDataset(
                    folderA,
                    pdsA,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics,
                    batterySegments: new List<(int, DateTime, DateTime)>
                    {
                        (11, start, start.AddSeconds(3)),
                    });
                CreatePreTestShapedDataset(
                    folderB,
                    pdsB,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics,
                    batterySegments: new List<(int, DateTime, DateTime)>
                    {
                        (22, start, start.AddSeconds(4)),
                    });

                // Same unit config; only StorePath changes between runs (path-only retarget).
                string enabledUnitsJson = "\"EnabledUnits\": [ \"Logigram1.GammaPath\" ]";

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pdsA) + ", " + enabledUnitsJson + " }");
                var stdoutA = new StringWriter();
                var stderrA = new StringWriter();
                int exitA = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdoutA, stderrA);
                Assert.AreEqual(0, exitA, stderrA.ToString());

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pdsB) + ", " + enabledUnitsJson + " }");
                var stdoutB = new StringWriter();
                var stderrB = new StringWriter();
                int exitB = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdoutB, stderrB);
                Assert.AreEqual(0, exitB, stderrB.ToString());

                AssertSessionIdentity(stdoutA.ToString(), pdsA);
                AssertSessionIdentity(stdoutB.ToString(), pdsB);
                Assert.AreNotEqual(
                    Path.GetFullPath(pdsA),
                    Path.GetFullPath(pdsB),
                    "Fixture paths must differ for attribution proof.");

                string siblingA = Path.Combine(folderA, "FirstStore.Logigram1Analysis.pds");
                string siblingB = Path.Combine(folderB, "SecondStore.Logigram1Analysis.pds");
                Assert.IsTrue(File.Exists(siblingA), stdoutA.ToString());
                Assert.IsTrue(File.Exists(siblingB), stdoutB.ToString());
                Assert.AreNotEqual(Path.GetFullPath(siblingA), Path.GetFullPath(siblingB));

                Dataset analysisA = Dataset.Load(siblingA);
                Dataset analysisB = Dataset.Load(siblingB);
                AssertAnalysisSessionName(analysisA, pdsA);
                AssertAnalysisSessionName(analysisB, pdsB);
                Assert.AreNotEqual(
                    FirstUnitPipelineRunner.FormatAnalysisSessionName(pdsA),
                    FirstUnitPipelineRunner.FormatAnalysisSessionName(pdsB));

                var statesA = ReadAnalysisStates(siblingA);
                var statesB = ReadAnalysisStates(siblingB);
                Assert.IsTrue(statesA.Any(state => state.BatteryId == 11), "Store A BatteryId=11");
                Assert.IsTrue(statesB.Any(state => state.BatteryId == 22), "Store B BatteryId=22");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        /// <summary>
        /// Shared OutputPath: second store run replaces prior analysis; Dataset session names the current input.
        /// </summary>
        [TestMethod]
        public void Run_WithTwoStores_SharedOutputPath_ReplacesAndNamesCurrentInput()
        {
            string root = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-shared-out-" + Guid.NewGuid().ToString("N"));
            string folderA = Path.Combine(root, "A");
            string folderB = Path.Combine(root, "B");
            Directory.CreateDirectory(folderA);
            Directory.CreateDirectory(folderB);
            string pdsA = Path.Combine(folderA, "StoreA.pds");
            string pdsB = Path.Combine(folderB, "StoreB.pds");
            string sharedOut = Path.Combine(root, "SharedAnalysis.pds");
            string configPath = Path.Combine(root, "logigram1.json");
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            try
            {
                CreatePreTestShapedDataset(
                    folderA,
                    pdsA,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics,
                    batterySegments: new List<(int, DateTime, DateTime)>
                    {
                        (7, start, start.AddSeconds(2)),
                    });
                CreatePreTestShapedDataset(
                    folderB,
                    pdsB,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics,
                    batterySegments: new List<(int, DateTime, DateTime)>
                    {
                        (8, start, start.AddSeconds(2)),
                    });

                string unitsAndOut =
                    "\"EnabledUnits\": [ \"Logigram1.GammaPath\" ], \"OutputPath\": " + JsonString(sharedOut);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pdsA) + ", " + unitsAndOut + " }");
                Assert.AreEqual(
                    0,
                    OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, new StringWriter(), new StringWriter()));
                Assert.IsTrue(File.Exists(sharedOut));
                AssertAnalysisSessionName(Dataset.Load(sharedOut), pdsA);
                Assert.IsTrue(ReadAnalysisStates(sharedOut).Any(state => state.BatteryId == 7));

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pdsB) + ", " + unitsAndOut + " }");
                var stdoutB = new StringWriter();
                var stderrB = new StringWriter();
                int exitB = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdoutB, stderrB);
                Assert.AreEqual(0, exitB, stderrB.ToString());
                AssertSessionIdentity(stdoutB.ToString(), pdsB);
                Assert.IsTrue(File.Exists(sharedOut));

                Dataset afterSecond = Dataset.Load(sharedOut);
                AssertAnalysisSessionName(afterSecond, pdsB);
                string expectedSessionA = ExpectedAnalysisSessionName(pdsA);
                Assert.AreNotEqual(
                    expectedSessionA,
                    afterSecond.Sessions[0].Name,
                    "Shared OutputPath re-run must replace prior session attribution.");
                Assert.IsTrue(ReadAnalysisStates(sharedOut).Any(state => state.BatteryId == 8));
                Assert.IsFalse(
                    ReadAnalysisStates(sharedOut).Any(state => state.BatteryId == 7),
                    "Clear-before-write must not leave BatteryId from the first store.");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        /// <summary>
        /// Second store lacking Spawn→Finish fails explicitly; no fabricated Battery / analysis success.
        /// </summary>
        [TestMethod]
        public void Run_WithSecondStoreMissingBatteryBoundaries_FailsExplicitly()
        {
            string root = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-dual-battery-fail-" + Guid.NewGuid().ToString("N"));
            string folderOk = Path.Combine(root, "Ok");
            string folderBad = Path.Combine(root, "Bad");
            Directory.CreateDirectory(folderOk);
            Directory.CreateDirectory(folderBad);
            string pdsOk = Path.Combine(folderOk, "OkStore.pds");
            string pdsBad = Path.Combine(folderBad, "BadStore.pds");
            string configPath = Path.Combine(root, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(
                    folderOk,
                    pdsOk,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics);
                CreatePreTestShapedDataset(
                    folderBad,
                    pdsBad,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics,
                    includeBatteryBoundaries: false);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pdsOk)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");
                Assert.AreEqual(
                    0,
                    OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, new StringWriter(), new StringWriter()));

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pdsBad)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");
                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                AssertSessionIdentity(stdout.ToString(), pdsBad);
                Assert.IsTrue(
                    stderr.ToString().IndexOf("Battery", StringComparison.OrdinalIgnoreCase) >= 0,
                    stderr.ToString());
                Assert.IsFalse(
                    File.Exists(Path.Combine(folderBad, "BadStore.Logigram1Analysis.pds")),
                    "Battery fail must not write analysis Dataset.");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        /// <summary>
        /// Second store missing FirstUnit topics fails with explicit gaps (same EnabledUnits as a prior success).
        /// </summary>
        [TestMethod]
        public void Run_WithSecondStoreMissingTopics_FailsWithGaps()
        {
            string root = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-dual-topic-gap-" + Guid.NewGuid().ToString("N"));
            string folderOk = Path.Combine(root, "Ok");
            string folderGap = Path.Combine(root, "Gap");
            Directory.CreateDirectory(folderOk);
            Directory.CreateDirectory(folderGap);
            string pdsOk = Path.Combine(folderOk, "OkStore.pds");
            string pdsGap = Path.Combine(folderGap, "GapStore.pds");
            string configPath = Path.Combine(root, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(
                    folderOk,
                    pdsOk,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics);

                // Substrate + Battery only — no FirstUnit topics.
                CreatePreTestShapedDataset(
                    folderGap,
                    pdsGap,
                    fullScreenWithData: true,
                    area1WithData: false);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pdsOk)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");
                Assert.AreEqual(
                    0,
                    OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, new StringWriter(), new StringWriter()));

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pdsGap)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");
                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                AssertSessionIdentity(stdout.ToString(), pdsGap);
                StringAssert.Contains(stderr.ToString(), "Required Logic topic missing:");
                Assert.IsFalse(File.Exists(Path.Combine(folderGap, "GapStore.Logigram1Analysis.pds")));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        /// <summary>
        /// Multiple missing Logic topics: stderr lists every gap; exit non-zero; no success implying topics OK.
        /// </summary>
        [TestMethod]
        public void Run_WithMultipleLogicTopicGaps_ListsAllGapsAndReturnsNonZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-topics-gap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "TopicsGap.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                // Substrate only — no Logic topic streams.
                CreatePreTestShapedDataset(folder, pds, fullScreenWithData: true, area1WithData: false);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                string err = stderr.ToString();
                // Area1/Area2 exist as substrate streams on PreTest stores — not reported as Logic gaps.
                foreach (string topic in Logigram1UnitTopicRegistry.GammaPathTopics)
                {
                    if (string.Equals(topic, "Area1", StringComparison.Ordinal)
                        || string.Equals(topic, "Area2", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    StringAssert.Contains(err, "Required Logic topic missing: " + topic);
                }

                Assert.IsFalse(
                    stdout.ToString().IndexOf("Required Logic topics OK", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Must not claim Logic topics OK when gaps remain.");
                Assert.IsFalse(
                    stdout.ToString().IndexOf("connectors loaded", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Must not print overall open success when Logic topic gaps remain.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// AD-7 name mismatch: wrong-named streams only → counted missing (no aliases); exit non-zero.
        /// </summary>
        [TestMethod]
        public void Run_WithWrongNamedLogicTopicsOnly_CountsAsGaps()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-topics-alias-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "TopicsAlias.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                // Near-miss names that must not satisfy exact AD-7 Name lookup.
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: new[]
                    {
                        "ModuleStatus",
                        "Porte1_ouverture",
                        "M1SelectModule",
                    });

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                StringAssert.Contains(stderr.ToString(), "Required Logic topic missing: Module status");
                StringAssert.Contains(stderr.ToString(), "Required Logic topic missing: Porte1 ouverture");
                Assert.IsFalse(
                    stdout.ToString().IndexOf("Required Logic topics OK", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Unknown enabled unit id → explicit registry error; exit non-zero.</summary>
        [TestMethod]
        public void Run_WithUnknownEnabledUnit_ReturnsNonZeroWithRegistryError()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-unknown-unit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "UnknownUnit.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(folder, pds, fullScreenWithData: true, area1WithData: false);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.DoesNotExist\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                StringAssert.Contains(stderr.ToString(), "Unknown enabled Logic unit id");
                StringAssert.Contains(stderr.ToString(), "Logigram1.DoesNotExist");
                Assert.IsFalse(
                    stdout.ToString().IndexOf("connectors loaded", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Must not print overall success for unknown unit.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Empty EnabledUnits → substrate smoke only; no Logic-topic section; exit 0.</summary>
        [TestMethod]
        public void Run_WithEmptyEnabledUnits_SkipsLogicTopicCheck()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-empty-units-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "EmptyUnits.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(folder, pds, fullScreenWithData: true, area1WithData: false);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds) + ", \"EnabledUnits\": [] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                StringAssert.Contains(stdout.ToString(), "required streams OK");
                Assert.IsFalse(
                    stdout.ToString().IndexOf("Required Logic topics", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Empty EnabledUnits must not emit a Logic-topic section.");
                Assert.IsFalse(
                    stdout.ToString().IndexOf("Analysis output Dataset:", StringComparison.Ordinal) >= 0,
                    "Empty EnabledUnits must not write an analysis output Dataset.");
                Assert.IsFalse(
                    File.Exists(Path.Combine(folder, "EmptyUnits.Logigram1Analysis.pds")),
                    "Empty EnabledUnits must not create sibling analysis .pds.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// Substrate fail remains enforced when Logic units are enabled (fail-fast before topic success).
        /// </summary>
        [TestMethod]
        public void Run_WithEnabledUnitButSubstrateFail_ReturnsNonZeroWithoutTopicSuccess()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-substrate-fail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "SubstrateFail.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(folder, pds, fullScreenWithData: false, area1WithData: true);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                StringAssert.Contains(stderr.ToString(), "VideoRemoteApp/FullScreen");
                Assert.IsFalse(
                    stdout.ToString().IndexOf("Required Logic topics OK", StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.IsFalse(
                    stderr.ToString().IndexOf("Required Logic topic missing", StringComparison.Ordinal) >= 0,
                    "Prefer fail-fast on substrate; do not also list Logic gaps after substrate hard fail.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Registry exposes the GammaPath topic union (module/door/select + leave/AABB/wrists).</summary>
        [TestMethod]
        public void UnitTopicRegistry_GammaPath_HasTopicUnion()
        {
            Assert.IsTrue(
                Logigram1UnitTopicRegistry.TryGetRequiredTopics(
                    Logigram1UnitTopicRegistry.GammaPathUnitId,
                    out IReadOnlyList<string> topics));
            Assert.AreEqual(14, topics.Count);
            CollectionAssert.Contains(topics.ToList(), "Module status");
            CollectionAssert.Contains(topics.ToList(), "Porte1 ouverture");
            CollectionAssert.Contains(topics.ToList(), "M1-SelectModule");
            CollectionAssert.Contains(topics.ToList(), "Area1");
            CollectionAssert.Contains(topics.ToList(), "Object Bound");
            CollectionAssert.Contains(topics.ToList(), "1-LeftWrist");
            CollectionAssert.Contains(topics.ToList(), "2-RightWrist");
        }

        /// <summary>Legacy FirstUnit/Node3 ids are not independently enableable in the registry.</summary>
        [TestMethod]
        public void UnitTopicRegistry_LegacyIds_NotEnableable()
        {
            Assert.IsFalse(
                Logigram1UnitTopicRegistry.TryGetRequiredTopics(
                    Logigram1UnitTopicRegistry.LegacyFirstUnitId,
                    out _));
            Assert.IsFalse(
                Logigram1UnitTopicRegistry.TryGetRequiredTopics(
                    Logigram1UnitTopicRegistry.LegacyNode3UnitId,
                    out _));
            Assert.IsTrue(Logigram1UnitTopicRegistry.IsLegacyIgnoredUnitId("Logigram1.FirstUnit"));
            Assert.IsTrue(Logigram1UnitTopicRegistry.IsLegacyIgnoredUnitId("Logigram1.Node3"));
        }

        /// <summary>Topic resolve for GammaPath is distinct and includes both former bags.</summary>
        [TestMethod]
        public void UnitTopicRegistry_GammaPath_UnionIsDistinct()
        {
            IReadOnlyList<string> union = Logigram1UnitTopicRegistry.TryResolveTopicUnion(
                new[] { Logigram1UnitTopicRegistry.GammaPathUnitId },
                out string unknown);
            Assert.IsNull(unknown);
            Assert.IsNotNull(union);
            CollectionAssert.Contains(union.ToList(), "Module status");
            CollectionAssert.Contains(union.ToList(), "Object Bound");
            Assert.AreEqual(
                union.Count,
                new HashSet<string>(union, StringComparer.Ordinal).Count,
                "Union must be distinct.");
        }

        /// <summary>GammaPath: unused 2s window then door+leave → node-3 State + Gamma.</summary>
        [TestMethod]
        public void Run_WithGammaPath_PersistsNode3StateAndGamma()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-gamma-ok-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "GammaOk.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            try
            {
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics,
                    batterySegments: new List<(int, DateTime, DateTime)>
                    {
                        (15, start, start.AddSeconds(10)),
                    });

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                string outputPds = Path.Combine(folder, "GammaOk.Logigram1Analysis.pds");
                Assert.IsTrue(File.Exists(outputPds), stdout.ToString());

                var states = ReadAnalysisStates(outputPds);
                Assert.IsTrue(
                    states.Any(state =>
                        state.BatteryId == 15
                        && state.Status != null
                        && state.Status.IndexOf("node-1", StringComparison.Ordinal) >= 0),
                    "Expected node-1 Status with BatteryId=15.");
                Assert.IsTrue(
                    states.Any(state =>
                        state.BatteryId == 15
                        && state.Status != null
                        && state.Status.IndexOf("node-3:", StringComparison.Ordinal) >= 0),
                    "Expected node-3 Status with BatteryId=15.");

                var outcomes = ReadAnalysisOutcomes(outputPds);
                Assert.IsTrue(
                    outcomes.Any(outcome =>
                        outcome.Regulation == LogigramRegulation.Gamma && outcome.BatteryId == 15),
                    "Expected Gamma Outcome with BatteryId=15.");
                Assert.IsFalse(
                    states.Any(state =>
                        state.Status != null
                        && state.Status.IndexOf("node-2", StringComparison.Ordinal) >= 0),
                    "Default fixture must not fire node-2 (Select is outside the 2s window).");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>GammaPath enabled with missing topics → explicit gaps; exit non-zero; no Dataset.</summary>
        [TestMethod]
        public void Run_WithGammaPathMissingTopics_ListsGapsAndReturnsNonZero()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-gamma-gap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "GammaGap.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(folder, pds, fullScreenWithData: true, area1WithData: false);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreNotEqual(0, exit, stdout.ToString());
                StringAssert.Contains(stderr.ToString(), "Required Logic topic missing: Object Bound");
                Assert.IsFalse(File.Exists(Path.Combine(folder, "GammaGap.Logigram1Analysis.pds")));
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>Legacy FirstUnit/Node3 in config: warn+ignore; no analysis run without GammaPath.</summary>
        [TestMethod]
        public void Run_WithLegacyFirstUnitAndNode3_WarnsAndSkipsRun()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-legacy-ignore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "LegacyIgnore.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            try
            {
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.FirstUnit\", \"Logigram1.Node3\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                StringAssert.Contains(stderr.ToString(), "Warning: ignoring deprecated EnabledUnits id \"Logigram1.FirstUnit\"");
                StringAssert.Contains(stderr.ToString(), "Warning: ignoring deprecated EnabledUnits id \"Logigram1.Node3\"");
                Assert.IsFalse(
                    File.Exists(Path.Combine(folder, "LegacyIgnore.Logigram1Analysis.pds")),
                    "Legacy-only EnabledUnits must not write an analysis Dataset.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>GammaPath + legacy ids: warn legacy, still run GammaPath cycle (node-1 + node-3 Gamma).</summary>
        [TestMethod]
        public void Run_WithGammaPathAndLegacyIds_WarnsAndRunsGammaPath()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-gamma-legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "GammaLegacy.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            try
            {
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics,
                    batterySegments: new List<(int, DateTime, DateTime)>
                    {
                        (21, start, start.AddSeconds(15)),
                    });

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\", \"Logigram1.FirstUnit\", \"Logigram1.Node3\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                StringAssert.Contains(stderr.ToString(), "Warning: ignoring deprecated EnabledUnits id \"Logigram1.FirstUnit\"");
                StringAssert.Contains(stdout.ToString(), "Required Logic topics OK:");
                StringAssert.Contains(stdout.ToString(), "Object Bound: present");
                StringAssert.Contains(stdout.ToString(), "Module status: present");

                string outputPds = Path.Combine(folder, "GammaLegacy.Logigram1Analysis.pds");
                var states = ReadAnalysisStates(outputPds);
                Assert.IsTrue(
                    states.Any(s => s.Status != null && s.Status.IndexOf("node-1", StringComparison.Ordinal) >= 0),
                    "Expected GammaPath node-1.");
                Assert.IsTrue(
                    states.Any(s =>
                        s.BatteryId == 21
                        && s.Status != null
                        && s.Status.IndexOf("node-3:", StringComparison.Ordinal) >= 0),
                    "Expected GammaPath node-3 (BatteryId=21).");

                int gammaCount = ReadAnalysisOutcomes(outputPds)
                    .Count(o => o.Regulation == LogigramRegulation.Gamma && o.BatteryId == 21);
                Assert.AreEqual(1, gammaCount, "Expected exactly one Gamma at C (not on node-2).");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// Door closed + Select within 2s of module create → node-2 State; zero Gamma Outcomes (cycle reset).
        /// </summary>
        [TestMethod]
        public void Run_WithGammaPath_Node2InWindow_PersistsNode2AndZeroGamma()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1Analysis-gamma-node2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "GammaNode2.pds");
            string configPath = Path.Combine(folder, "logigram1.json");
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            try
            {
                CreatePreTestShapedDataset(
                    folder,
                    pds,
                    fullScreenWithData: true,
                    area1WithData: false,
                    logicTopics: Logigram1UnitTopicRegistry.GammaPathTopics,
                    batterySegments: new List<(int, DateTime, DateTime)>
                    {
                        (33, start, start.AddSeconds(15)),
                    },
                    selectInNode2Window: true);

                File.WriteAllText(
                    configPath,
                    "{ \"StorePath\": " + JsonString(pds)
                    + ", \"EnabledUnits\": [ \"Logigram1.GammaPath\" ] }");

                var stdout = new StringWriter();
                var stderr = new StringWriter();
                int exit = OfflineCasperStoreConsole.Run(new[] { "--config", configPath }, stdout, stderr);

                Assert.AreEqual(0, exit, stderr.ToString());
                string outputPds = Path.Combine(folder, "GammaNode2.Logigram1Analysis.pds");
                Assert.IsTrue(File.Exists(outputPds), stdout.ToString());

                var states = ReadAnalysisStates(outputPds);
                Assert.IsTrue(
                    states.Any(s =>
                        s.BatteryId == 33
                        && s.Status != null
                        && s.Status.IndexOf("node-1", StringComparison.Ordinal) >= 0),
                    "Expected node-1 Status.");
                Assert.IsTrue(
                    states.Any(s =>
                        s.BatteryId == 33
                        && s.Status != null
                        && s.Status.IndexOf("node-2", StringComparison.Ordinal) >= 0),
                    "Expected node-2 Status from door+Select in create window.");
                Assert.IsFalse(
                    states.Any(s =>
                        s.Status != null
                        && s.Status.IndexOf("node-3:", StringComparison.Ordinal) >= 0),
                    "Leave after node-2 reset must not fire node-3.");

                Assert.AreEqual(
                    0,
                    ReadAnalysisOutcomes(outputPds).Count(o => o.Regulation == LogigramRegulation.Gamma),
                    "Node-2 reset must yield zero Gamma Outcomes.");
            }
            finally
            {
                TryDeleteDirectory(folder);
            }
        }

        /// <summary>
        /// Builds a PreTest.081-shaped Dataset.
        /// </summary>
        /// <param name="folder">Temp folder for Psi stores and the <c>.pds</c>.</param>
        /// <param name="pdsPath">Path of the Dataset file to create.</param>
        /// <param name="fullScreenWithData">Whether FullScreen has recorded messages.</param>
        /// <param name="area1WithData">Whether Area1 has recorded messages (presence-only in smoke).</param>
        /// <param name="logicTopics">Optional AD-7 Logic topic stream names written into the Server store.</param>
        /// <param name="includeBatteryBoundaries">
        /// When true (default), writes one closed Spawn→Finish segment so AD-6 discovery succeeds.
        /// When false, omits BatterySpawn/BatteryFinish (missing-stream fail cases).
        /// </param>
        /// <param name="batterySegments">Optional explicit segments; ignored when <paramref name="includeBatteryBoundaries"/> is false.</param>
        /// <param name="emptyBatteryStreams">When true with include, write present-but-empty Battery streams (zero-segment fail).</param>
        /// <param name="selectInNode2Window">
        /// When true, Select fires inside the 2s-from-create window (node-2 reset, no Gamma).
        /// </param>
        private static Dataset CreatePreTestShapedDataset(
            string folder,
            string pdsPath,
            bool fullScreenWithData,
            bool area1WithData,
            IEnumerable<string> logicTopics = null,
            bool includeBatteryBoundaries = true,
            IReadOnlyList<(int BatteryId, DateTime Spawn, DateTime Finish)> batterySegments = null,
            bool emptyBatteryStreams = false,
            bool selectInNode2Window = false)
        {
            DateTime start = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

            WriteStore(folder, "VideoRemoteApp", new[] { ("FullScreen", fullScreenWithData) }, start);
            WriteStore(folder, "WhisperStreaming", new[] { ("Audio_User_1", true), ("Audio_User_2", true) }, start);

            // Former FirstUnit bag (node 1–2 inputs) vs leave/AABB/wrist bag (node 3 inputs).
            var node12TopicSet = new HashSet<string>(StringComparer.Ordinal)
            {
                "Module status",
                "Porte1 ouverture",
                "Porte2 ouverture",
                "M1-SelectModule",
                "M2-SelectModule",
                "M1-Validation",
                "M2-Validation",
            };
            var node3TopicSet = new HashSet<string>(StringComparer.Ordinal)
            {
                "Porte1 ouverture",
                "Porte2 ouverture",
                "Area1",
                "Area2",
                "Object Bound",
                "1-LeftWrist",
                "1-RightWrist",
                "2-LeftWrist",
                "2-RightWrist",
            };
            bool node3Areas = logicTopics != null
                && logicTopics.Any(t => string.Equals(t, "Area1", StringComparison.Ordinal)
                    || string.Equals(t, "Area2", StringComparison.Ordinal));

            // When Area1/2 are Logic topics, write catalogue (int,bool,string) — not int stubs.
            var serverStreams = new List<(string Stream, bool WithData)>();
            if (!node3Areas)
            {
                serverStreams.Add(("Area1", area1WithData));
                serverStreams.Add(("Area2", true));
            }

            // GammaPath AD-7 topics need catalogue types; unknown names stay as int presence stubs.
            var firstUnitTopics = new List<string>();
            var node3Topics = new List<string>();
            if (logicTopics != null)
            {
                foreach (string topic in logicTopics)
                {
                    if (node12TopicSet.Contains(topic))
                    {
                        firstUnitTopics.Add(topic);
                    }

                    if (node3TopicSet.Contains(topic)
                        && !string.Equals(topic, "Porte1 ouverture", StringComparison.Ordinal)
                        && !string.Equals(topic, "Porte2 ouverture", StringComparison.Ordinal))
                    {
                        // Porte shared: written by node-1/2 helper; leave/AABB/wrist via node-3 helper.
                        node3Topics.Add(topic);
                    }
                    else if (!node12TopicSet.Contains(topic) && !node3TopicSet.Contains(topic))
                    {
                        serverStreams.Add((topic, true));
                    }
                }
            }

            IReadOnlyList<(int BatteryId, DateTime Spawn, DateTime Finish)> segments = null;
            if (includeBatteryBoundaries && !emptyBatteryStreams)
            {
                segments = batterySegments ?? new List<(int, DateTime, DateTime)>
                {
                    (1, start, start.AddSeconds(1)),
                };
            }

            WriteServerStore(
                folder,
                "Server",
                serverStreams,
                start,
                batterySegments: segments,
                emptyBatteryStreams: includeBatteryBoundaries && emptyBatteryStreams,
                firstUnitTopics: firstUnitTopics,
                node3Topics: node3Topics,
                selectInNode2Window: selectInNode2Window);

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
            WriteServerStore(
                folder,
                "Server",
                new[] { ("Area1", true), ("Area2", true) },
                start,
                storeRoot: folder,
                batterySegments: new List<(int, DateTime, DateTime)> { (1, start, start.AddSeconds(1)) });

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

        /// <summary>
        /// Writes the Server store including optional BatterySpawn / BatteryFinish boundary streams.
        /// </summary>
        private static void WriteServerStore(
            string folder,
            string storeName,
            IEnumerable<(string Stream, bool WithData)> streams,
            DateTime start,
            string storeRoot = null,
            IReadOnlyList<(int BatteryId, DateTime Spawn, DateTime Finish)> batterySegments = null,
            bool emptyBatteryStreams = false,
            IEnumerable<string> firstUnitTopics = null,
            IEnumerable<string> node3Topics = null,
            bool selectInNode2Window = false)
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

                if (firstUnitTopics != null)
                {
                    WriteFirstUnitTopicStreams(pipeline, store, firstUnitTopics, start, selectInNode2Window);
                }

                if (node3Topics != null)
                {
                    WriteNode3TopicStreams(pipeline, store, node3Topics, start, firstUnitTopics);
                }

                if (batterySegments != null || emptyBatteryStreams)
                {
                    var spawnEvents = new List<(PsiBatteryEvent, DateTime)>();
                    var finishEvents = new List<(PsiBatteryFinish, DateTime)>();
                    if (batterySegments != null)
                    {
                        foreach ((int batteryId, DateTime spawn, DateTime finish) in batterySegments)
                        {
                            spawnEvents.Add((new PsiBatteryEvent(batteryId, 0, 0, false), spawn));
                            finishEvents.Add((default(PsiBatteryFinish), finish));
                        }
                    }

                    Generators.Sequence(pipeline, spawnEvents).Write(BatteryBoundaryDiscovery.SpawnStreamName, store);
                    Generators.Sequence(pipeline, finishEvents).Write(BatteryBoundaryDiscovery.FinishStreamName, store);
                }

                pipeline.Run();
            }
        }

        /// <summary>
        /// Writes node 1–2 AD-7 topics. Default timings: module create, door closed early, Select
        /// <b>after</b> the 2s window so node 2 does not fire — leave (node-3 helper) can emit Gamma.
        /// When <paramref name="selectInNode2Window"/> is true, Select is at +1.0s (node-2 reset).
        /// </summary>
        private static void WriteFirstUnitTopicStreams(
            Pipeline pipeline,
            PsiExporter store,
            IEnumerable<string> topics,
            DateTime time,
            bool selectInNode2Window = false)
        {
            double selectOffsetSeconds = selectInNode2Window ? 1.0 : 3.0;
            foreach (string topic in topics)
            {
                switch (topic)
                {
                    case "Module status":
                        Generators.Sequence(
                                pipeline,
                                new[] { (new ValueTuple<int, string>(1, "OK"), time) })
                            .Write(topic, store);
                        break;

                    case "Porte1 ouverture":
                    case "Porte2 ouverture":
                        Generators.Sequence(
                                pipeline,
                                new[] { (new ValueTuple<bool, Vector3>(false, Vector3.Zero), time.AddSeconds(0.5)) })
                            .Write(topic, store);
                        break;

                    case "M1-SelectModule":
                    case "M2-SelectModule":
                        Generators.Sequence(
                                pipeline,
                                new[] { ("6V", time.AddSeconds(selectOffsetSeconds)) })
                            .Write(topic, store);
                        break;

                    case "M1-Validation":
                    case "M2-Validation":
                        Generators.Sequence(
                                pipeline,
                                new[] { (false, time.AddSeconds(selectOffsetSeconds + 0.2)) })
                            .Write(topic, store);
                        break;

                    default:
                        Generators.Sequence(pipeline, new[] { (1, time) }).Write(topic, store);
                        break;
                }
            }
        }

        /// <summary>
        /// Writes leave/AABB/wrist topics: player leave GeneratorArea after the 2s node-2 window
        /// so node-3 can fire when a prior module create opened the cycle.
        /// </summary>
        private static void WriteNode3TopicStreams(
            Pipeline pipeline,
            PsiExporter store,
            IEnumerable<string> topics,
            DateTime time,
            IEnumerable<string> firstUnitTopics)
        {
            var writtenByFirstUnit = new HashSet<string>(
                firstUnitTopics ?? Array.Empty<string>(),
                StringComparer.Ordinal);

            foreach (string topic in topics)
            {
                if (writtenByFirstUnit.Contains(topic)
                    && (topic.StartsWith("Porte", StringComparison.Ordinal)))
                {
                    continue;
                }

                switch (topic)
                {
                    case "Porte1 ouverture":
                    case "Porte2 ouverture":
                        Generators.Sequence(
                                pipeline,
                                new[] { (new ValueTuple<bool, Vector3>(false, Vector3.Zero), time.AddSeconds(0.5)) })
                            .Write(topic, store);
                        break;

                    case "Area1":
                    case "Area2":
                        // After unused 2s window from module create at `time`.
                        Generators.Sequence(
                                pipeline,
                                new[]
                                {
                                    (new ValueTuple<int, bool, string>(-1, false, "GeneratorArea"), time.AddSeconds(2.5)),
                                })
                            .Write(topic, store);
                        break;

                    case "Object Bound":
                        Generators.Sequence(
                                pipeline,
                                new[]
                                {
                                    (new ValueTuple<string, Vector3, Vector3>(
                                        "Generateur1",
                                        new Vector3(0, 0, 0),
                                        new Vector3(1, 1, 1)), time.AddSeconds(0.2)),
                                    (new ValueTuple<string, Vector3, Vector3>(
                                        "Generateur2",
                                        new Vector3(10, 0, 0),
                                        new Vector3(11, 1, 1)), time.AddSeconds(0.25)),
                                })
                            .Write(topic, store);
                        break;

                    case "1-LeftWrist":
                    case "1-RightWrist":
                    case "2-LeftWrist":
                    case "2-RightWrist":
                        Generators.Sequence(
                                pipeline,
                                new[]
                                {
                                    (Tuple.Create(new Vector3(100, 0, 0), Vector3.Zero), time.AddSeconds(0.3)),
                                })
                            .Write(topic, store);
                        break;

                    default:
                        Generators.Sequence(pipeline, new[] { (1, time) }).Write(topic, store);
                        break;
                }
            }
        }

        private static List<LogigramState> ReadAnalysisStates(string analysisPdsPath)
        {
            var results = new List<LogigramState>();
            Dataset dataset = Dataset.Load(analysisPdsPath);
            IStreamMetadata metadata = OfflineCasperStoreConsole.FindStreamByExactName(
                dataset,
                LogigramState.StreamName);
            Assert.IsNotNull(metadata, "Expected Logigram1.State in analysis Dataset.");

            using (Pipeline pipeline = Pipeline.Create(nameof(ReadAnalysisStates)))
            {
                PsiImporter store = PsiStore.Open(pipeline, metadata.StoreName, metadata.StorePath);
                store.OpenStream<LogigramState>(metadata.Name)
                    .Do(state => results.Add(new LogigramState
                    {
                        Status = state.Status,
                        BatteryId = state.BatteryId,
                    }));
                pipeline.Run(ReplayDescriptor.ReplayAll);
            }

            return results;
        }

        private static List<LogigramOutcome> ReadAnalysisOutcomes(string analysisPdsPath)
        {
            var results = new List<LogigramOutcome>();
            Dataset dataset = Dataset.Load(analysisPdsPath);
            IStreamMetadata metadata = OfflineCasperStoreConsole.FindStreamByExactName(
                dataset,
                LogigramOutcome.StreamName);
            Assert.IsNotNull(metadata, "Expected Logigram1.Outcome in analysis Dataset.");

            using (Pipeline pipeline = Pipeline.Create(nameof(ReadAnalysisOutcomes)))
            {
                PsiImporter store = PsiStore.Open(pipeline, metadata.StoreName, metadata.StorePath);
                store.OpenStream<LogigramOutcome>(metadata.Name)
                    .Do(outcome => results.Add(new LogigramOutcome
                    {
                        Regulation = outcome.Regulation,
                        BatteryId = outcome.BatteryId,
                    }));
                pipeline.Run(ReplayDescriptor.ReplayAll);
            }

            return results;
        }

        private static void AssertSessionIdentity(string output, string storePath)
        {
            string fullPath = Path.GetFullPath(storePath);
            string id = Path.GetFileName(fullPath);
            StringAssert.Contains(output, "Session identity:");
            StringAssert.Contains(output, "path=" + fullPath);
            StringAssert.Contains(output, "id=" + id);
        }

        private static void AssertAnalysisSessionName(Dataset analysisDataset, string inputStorePath)
        {
            Assert.IsNotNull(analysisDataset);
            Assert.AreEqual(1, analysisDataset.Sessions.Count, "Analysis Dataset must have exactly one session.");
            Assert.AreEqual(
                ExpectedAnalysisSessionName(inputStorePath),
                analysisDataset.Sessions[0].Name,
                "SESSION_ATTR: analysis Dataset session name must encode input store path/id.");
        }

        /// <summary>
        /// Independent oracle for SESSION_ATTR wire format (must not call production formatter).
        /// </summary>
        private static string ExpectedAnalysisSessionName(string inputStorePath)
        {
            string fullPath = Path.GetFullPath(inputStorePath);
            string id = Path.GetFileName(fullPath);
            return "path=" + fullPath + " id=" + id;
        }

        private static string JsonString(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
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
