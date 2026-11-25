// <copyright file="DelayedStreamCopierTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// </copyright>

namespace DelayedStreamCopy.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using Microsoft.Psi.Imaging;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Story 4.14: delayed copy of a <c>Shared&lt;EncodedImage&gt;</c> stream into the PostProcess store.
    /// </summary>
    [TestClass]
    public class DelayedStreamCopierTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 3, 20, 15, 44, 23, DateTimeKind.Utc).AddTicks(1234567);

        private string tempRoot;

        [TestInitialize]
        public void SetUp()
        {
            this.tempRoot = Path.Combine(Path.GetTempPath(), "DelayedStreamCopyTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.tempRoot);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                Directory.Delete(this.tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [TestMethod]
        public void ShiftOriginatingTime_PositiveNegativeZeroAndSubMillisecond()
        {
            Assert.AreEqual(T0.AddSeconds(1.5), DelayedStreamCopier.ShiftOriginatingTime(T0, 1.5));
            Assert.AreEqual(T0.AddSeconds(-2), DelayedStreamCopier.ShiftOriginatingTime(T0, -2.0));
            Assert.AreEqual(T0, DelayedStreamCopier.ShiftOriginatingTime(T0, 0.0));

            // DateTime.AddSeconds would round to whole milliseconds on .NET Framework; the shift must be tick-exact.
            Assert.AreEqual(T0.AddTicks(12345), DelayedStreamCopier.ShiftOriginatingTime(T0, 0.0012345));
            Assert.AreEqual(DateTimeKind.Utc, DelayedStreamCopier.ShiftOriginatingTime(T0, 1.0).Kind);
        }

        [TestMethod]
        public void Copy_PositiveDelay_ShiftsOtsAndKeepsStreamNameTypePayloadsAndInputStore()
        {
            string casperRoot = this.MakeCasperStore("VideoRemoteApp", "FullScreen", Frames(5), out string storeRoot);
            string inputHashBefore = HashDirectory(storeRoot);
            string outRoot = Path.Combine(this.tempRoot, "Casper.PostProcess");

            var result = DelayedStreamCopier.Copy("VideoRemoteApp", storeRoot, "VideoRemoteApp", "FullScreen", 1.5, outRoot);

            Assert.AreEqual(5, result.MessageCount);
            Assert.IsTrue(Directory.Exists(Path.Combine(outRoot, "PostProcess.0000")), "store name must be PostProcess");
            Assert.IsFalse(Directory.Exists(Path.Combine(outRoot, "VideoRemoteApp.0000")));

            StreamDump source = ReadStream("VideoRemoteApp", storeRoot, "FullScreen");
            StreamDump copy = ReadStream("PostProcess", outRoot, "FullScreen");

            Assert.AreEqual(source.TypeName, copy.TypeName, "stream type must be unchanged (Shared<EncodedImage>)");
            Assert.AreEqual(source.Frames.Count, copy.Frames.Count);
            for (int i = 0; i < source.Frames.Count; i++)
            {
                Assert.AreEqual(source.Frames[i].OriginatingTime.AddSeconds(1.5), copy.Frames[i].OriginatingTime, "OT = raw + delay");
                CollectionAssert.AreEqual(source.Frames[i].Payload, copy.Frames[i].Payload, "payload bytes preserved");
                Assert.AreEqual(source.Frames[i].Width, copy.Frames[i].Width);
                Assert.AreEqual(source.Frames[i].Height, copy.Frames[i].Height);
            }

            // Like a recorded Casper store, the source is opened at its first OT. The copy must still be readable through its last shifted frame.
            Assert.IsTrue(source.OpenedTime <= source.Frames[0].OriginatingTime, "test precondition: source resembles a recorded store");
            Assert.IsTrue(
                copy.ClosedTime >= copy.Frames.Last().OriginatingTime,
                "copy ClosedTime must cover its last shifted frame, or readers drop the tail of the video");
            Assert.AreEqual(inputHashBefore, HashDirectory(storeRoot), "input Casper store must be untouched");
            Assert.IsNotNull(casperRoot);
        }

        [TestMethod]
        public void Copy_ZeroDelay_WritesIdenticalOtsAndRerunReplacesOutput()
        {
            this.MakeCasperStore("VideoRemoteApp", "FullScreen", Frames(3), out string storeRoot);
            string outRoot = Path.Combine(this.tempRoot, "Casper.PostProcess");

            DelayedStreamCopier.Copy("VideoRemoteApp", storeRoot, "VideoRemoteApp", "FullScreen", 0.0, outRoot);
            DelayedStreamCopier.Copy("VideoRemoteApp", storeRoot, "VideoRemoteApp", "FullScreen", 0.0, outRoot);

            StreamDump source = ReadStream("VideoRemoteApp", storeRoot, "FullScreen");
            StreamDump copy = ReadStream("PostProcess", outRoot, "FullScreen");
            CollectionAssert.AreEqual(
                source.Frames.Select(f => f.OriginatingTime).ToList(),
                copy.Frames.Select(f => f.OriginatingTime).ToList());
            Assert.AreEqual(1, Directory.GetDirectories(outRoot).Length, "re-run must replace, not accumulate");
            Assert.AreEqual(0, Directory.GetDirectories(this.tempRoot, "*.staging-*").Length);
        }

        [TestMethod]
        public void Copy_NegativeDelay_MovesFramesEarlier()
        {
            this.MakeCasperStore("VideoRemoteApp", "FullScreen", Frames(4), out string storeRoot);
            string outRoot = Path.Combine(this.tempRoot, "Casper.PostProcess");

            DelayedStreamCopier.Copy("VideoRemoteApp", storeRoot, "VideoRemoteApp", "FullScreen", -2.25, outRoot);

            StreamDump source = ReadStream("VideoRemoteApp", storeRoot, "FullScreen");
            StreamDump copy = ReadStream("PostProcess", outRoot, "FullScreen");
            for (int i = 0; i < source.Frames.Count; i++)
            {
                Assert.AreEqual(source.Frames[i].OriginatingTime.AddSeconds(-2.25), copy.Frames[i].OriginatingTime);
            }
        }

        [TestMethod]
        public void Run_MissingStream_FailsLoudNamingPartitionAndStream_AndWritesNothing()
        {
            this.MakeCasperStore("VideoRemoteApp", "FullScreen", Frames(2), out string storeRoot);
            string outRoot = Path.Combine(this.tempRoot, "out");
            var stderr = new StringWriter();

            int exit = DelayedStreamCopyConsole.Run(
                new[] { "--store-path", storeRoot, "--store-name", "VideoRemoteApp", "--stream", "fullscreen", "--out-root", outRoot },
                new StringWriter(),
                stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), "VideoRemoteApp/fullscreen");
            Assert.IsFalse(Directory.Exists(outRoot));
            Assert.AreEqual(0, Directory.GetDirectories(this.tempRoot, "*.staging-*").Length);
        }

        [TestMethod]
        public void Run_EmptyStream_FailsLoudAndWritesNoSuccessStore()
        {
            this.MakeCasperStore("VideoRemoteApp", "FullScreen", new List<(byte[], DateTime)>(), out string storeRoot);
            string outRoot = Path.Combine(this.tempRoot, "out");
            var stderr = new StringWriter();

            int exit = DelayedStreamCopyConsole.Run(
                new[] { "--store-path", storeRoot, "--store-name", "VideoRemoteApp", "--out-root", outRoot },
                new StringWriter(),
                stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), "VideoRemoteApp/FullScreen");
            StringAssert.Contains(stderr.ToString(), "empty");
            Assert.IsFalse(Directory.Exists(outRoot));
            Assert.IsFalse(File.Exists(outRoot + ".pds"));
        }

        [TestMethod]
        public void Run_PartitionAndStreamOverrides_TargetThatPair_OutputStaysPostProcess()
        {
            this.MakeCasperStore("OtherApp", "Cam", Frames(3), out string storeRoot);
            string outRoot = Path.Combine(this.tempRoot, "out");

            int exit = DelayedStreamCopyConsole.Run(
                new[] { "--store-path", storeRoot, "--partition", "OtherApp", "--stream", "Cam", "--delay-sec", "0.5", "--out-root", outRoot, "--no-pds" },
                new StringWriter(),
                new StringWriter());

            Assert.AreEqual(0, exit);
            StreamDump copy = ReadStream("PostProcess", outRoot, "Cam");
            Assert.AreEqual(3, copy.Frames.Count);
            StreamDump source = ReadStream("OtherApp", storeRoot, "Cam");
            Assert.AreEqual(source.Frames[0].OriginatingTime.AddSeconds(0.5), copy.Frames[0].OriginatingTime);
        }

        [TestMethod]
        public void Run_FromCasperPds_DefaultsResolveVideoRemoteAppAndWriteSiblingPostProcess()
        {
            string session = Path.Combine(this.tempRoot, "PreTest");
            Directory.CreateDirectory(session);
            WriteSourceStore(session, "VideoRemoteApp", "FullScreen", Frames(3));

            string pds = Path.Combine(this.tempRoot, "Casper.pds");
            var dataset = new Dataset("Casper", pds, autoSave: true);
            Session s = dataset.AddEmptySession("PreTest");
            s.AddPartitionFromPsiStoreAsync("VideoRemoteApp", session, "VideoRemoteApp").GetAwaiter().GetResult();
            dataset.Save();

            string inputHashBefore = HashDirectory(session);
            var stdout = new StringWriter();
            int exit = DelayedStreamCopyConsole.Run(new[] { "--casper", pds, "--delay-sec", "1.5" }, stdout, new StringWriter());

            Assert.AreEqual(0, exit, stdout.ToString());
            string outRoot = Path.Combine(this.tempRoot, "Casper.PostProcess");
            Assert.IsTrue(Directory.Exists(Path.Combine(outRoot, "PostProcess.0000")));
            Assert.AreEqual(3, ReadStream("PostProcess", outRoot, "FullScreen").Frames.Count);
            Assert.AreEqual(inputHashBefore, HashDirectory(session));

            string outPds = Path.Combine(this.tempRoot, "Casper.PostProcess.pds");
            Assert.IsTrue(File.Exists(outPds), "default run must write the sibling .pds");
            Dataset loaded = Dataset.Load(outPds);
            Assert.IsTrue(
                loaded.Sessions.SelectMany(x => x.Partitions).Any(p => p.Name == "PostProcess" && p.StoreName == "PostProcess"),
                "sibling .pds must list the PostProcess partition");
        }

        [TestMethod]
        public void Run_WrongStreamType_FailsLoudAndWritesNothing()
        {
            string casperRoot = Path.Combine(this.tempRoot, "PreTest.081");
            Directory.CreateDirectory(casperRoot);
            using (var pipeline = Pipeline.Create("test-wrong-type"))
            {
                PsiExporter exporter = PsiStore.Create(pipeline, "VideoRemoteApp", casperRoot);
                Generators.Sequence(pipeline, new[] { (1, T0), (2, T0.AddMilliseconds(100)) }, null, false, "ints")
                    .Write("FullScreen", exporter);
                pipeline.Run(ReplayDescriptor.ReplayAll);
            }

            string outRoot = Path.Combine(this.tempRoot, "out");
            var stderr = new StringWriter();
            int exit = DelayedStreamCopyConsole.Run(
                new[] { "--store-path", casperRoot, "--store-name", "VideoRemoteApp", "--out-root", outRoot },
                new StringWriter(),
                stderr);

            Assert.AreNotEqual(0, exit);
            StringAssert.Contains(stderr.ToString(), "VideoRemoteApp/FullScreen");
            StringAssert.Contains(stderr.ToString(), "Shared<EncodedImage>");
            Assert.IsFalse(Directory.Exists(outRoot));
            Assert.IsFalse(File.Exists(outRoot + ".pds"));
            Assert.AreEqual(0, Directory.GetDirectories(this.tempRoot, "*.staging-*").Length);
        }

        [TestMethod]
        public void Run_DelaySecWithoutValueOrInvalid_FailsWithExit2()
        {
            this.MakeCasperStore("VideoRemoteApp", "FullScreen", Frames(2), out string storeRoot);
            string outRoot = Path.Combine(this.tempRoot, "out");
            string[] common = { "--store-path", storeRoot, "--store-name", "VideoRemoteApp", "--out-root", outRoot };

            foreach (string[] args in new[]
            {
                common.Concat(new[] { "--delay-sec" }).ToArray(),
                new[] { "--delay-sec" }.Concat(common).ToArray(),
                common.Concat(new[] { "--delay-sec", "abc" }).ToArray(),
            })
            {
                var stderr = new StringWriter();
                Assert.AreEqual(2, DelayedStreamCopyConsole.Run(args, new StringWriter(), stderr));
                StringAssert.Contains(stderr.ToString(), "FAIL");
                Assert.IsFalse(Directory.Exists(outRoot));
            }
        }

        [TestMethod]
        public void Copy_OutputRootIsAFile_IsRefusedAndFileKept()
        {
            this.MakeCasperStore("VideoRemoteApp", "FullScreen", Frames(2), out string storeRoot);
            string outRoot = Path.Combine(this.tempRoot, "afile");
            File.WriteAllText(outRoot, "x");

            var ex = Assert.ThrowsException<DelayedStreamCopyException>(() =>
                DelayedStreamCopier.Copy("VideoRemoteApp", storeRoot, "VideoRemoteApp", "FullScreen", 1.0, outRoot));
            StringAssert.Contains(ex.Message, "file");
            Assert.AreEqual("x", File.ReadAllText(outRoot));
        }

        [TestMethod]
        public void Copy_OutputRootOverlappingInputStore_IsRefusedAndInputKept()
        {
            this.MakeCasperStore("VideoRemoteApp", "FullScreen", Frames(2), out string storeRoot);
            string inputHashBefore = HashDirectory(storeRoot);

            foreach (string outRoot in new[] { storeRoot, Path.GetDirectoryName(storeRoot), Path.Combine(storeRoot, "nested") })
            {
                var ex = Assert.ThrowsException<DelayedStreamCopyException>(() =>
                    DelayedStreamCopier.Copy("VideoRemoteApp", storeRoot, "VideoRemoteApp", "FullScreen", 1.0, outRoot));
                StringAssert.Contains(ex.Message, "overlaps");
            }

            Assert.AreEqual(inputHashBefore, HashDirectory(storeRoot));
        }

        [TestMethod]
        public void Copy_ExistingNonPostProcessOutputRoot_IsNotDeleted()
        {
            this.MakeCasperStore("VideoRemoteApp", "FullScreen", Frames(2), out string storeRoot);
            string outRoot = Path.Combine(this.tempRoot, "precious");
            Directory.CreateDirectory(outRoot);
            File.WriteAllText(Path.Combine(outRoot, "keep.txt"), "x");

            Assert.ThrowsException<DelayedStreamCopyException>(() =>
                DelayedStreamCopier.Copy("VideoRemoteApp", storeRoot, "VideoRemoteApp", "FullScreen", 1.0, outRoot));
            Assert.IsTrue(File.Exists(Path.Combine(outRoot, "keep.txt")));
        }

        private static List<(byte[] Payload, DateTime Ot)> Frames(int count)
        {
            var frames = new List<(byte[], DateTime)>();
            for (int i = 0; i < count; i++)
            {
                var payload = new byte[32 + i];
                for (int b = 0; b < payload.Length; b++)
                {
                    payload[b] = (byte)((b * 7) + i);
                }

                frames.Add((payload, T0.AddMilliseconds(100 * i)));
            }

            return frames;
        }

        private static void WriteSourceStore(string root, string storeName, string streamName, List<(byte[] Payload, DateTime Ot)> frames)
        {
            var images = new List<(Shared<EncodedImage>, DateTime)>();
            foreach (var (payload, ot) in frames)
            {
                Shared<EncodedImage> image = EncodedImagePool.GetOrCreate(8, 6, PixelFormat.BGR_24bpp);
                image.Resource.CopyFrom(payload, 0, payload.Length);
                images.Add((image, ot));
            }

            using (var pipeline = Pipeline.Create("test-source-store"))
            {
                PsiExporter exporter = PsiStore.Create(pipeline, storeName, root);
                Generators.Sequence(pipeline, images, images.Count > 0 ? images[0].Item2 : (DateTime?)null, false, "frames").Write(streamName, exporter, largeMessages: true);
                pipeline.Run(ReplayDescriptor.ReplayAll);
            }
        }

        private static StreamDump ReadStream(string storeName, string root, string streamName)
        {
            var dump = new StreamDump();
            using (var pipeline = Pipeline.Create("test-read-store"))
            {
                PsiImporter importer = PsiStore.Open(pipeline, storeName, root);
                IStreamMetadata meta = importer.GetMetadata(streamName);
                dump.TypeName = meta.TypeName;
                dump.OpenedTime = meta.OpenedTime;
                dump.ClosedTime = meta.ClosedTime;
                importer.OpenStream<Shared<EncodedImage>>(streamName).Do((image, envelope) =>
                {
                    byte[] payload = new byte[image.Resource.Size];
                    Array.Copy(image.Resource.GetBuffer(), payload, payload.Length);
                    dump.Frames.Add(new FrameDump
                    {
                        OriginatingTime = envelope.OriginatingTime,
                        Payload = payload,
                        Width = image.Resource.Width,
                        Height = image.Resource.Height,
                    });
                });
                pipeline.Run(ReplayDescriptor.ReplayAll);
            }

            return dump;
        }

        private static string HashDirectory(string dir)
        {
            using (var sha = SHA256.Create())
            {
                var lines = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .Select(f => f.Substring(dir.Length) + ":" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(f))));
                return string.Join("\n", lines);
            }
        }

        private string MakeCasperStore(string storeName, string streamName, List<(byte[] Payload, DateTime Ot)> frames, out string storeRoot)
        {
            string casperRoot = Path.Combine(this.tempRoot, "PreTest.081");
            Directory.CreateDirectory(casperRoot);
            WriteSourceStore(casperRoot, storeName, streamName, frames);
            storeRoot = casperRoot;
            return casperRoot;
        }

        private sealed class FrameDump
        {
            public DateTime OriginatingTime { get; set; }

            public byte[] Payload { get; set; }

            public int Width { get; set; }

            public int Height { get; set; }
        }

        private sealed class StreamDump
        {
            public string TypeName { get; set; }

            public DateTime OpenedTime { get; set; }

            public DateTime ClosedTime { get; set; }

            public List<FrameDump> Frames { get; } = new List<FrameDump>();
        }
    }
}
