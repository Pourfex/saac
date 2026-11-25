// <copyright file="DelayedStreamCopier.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace DelayedStreamCopy
{
    using System;
    using System.IO;
    using System.Linq;
    using Microsoft.Psi;
    using Microsoft.Psi.Components;
    using Microsoft.Psi.Data;
    using Microsoft.Psi.Imaging;

    /// <summary>
    /// Copies one <c>Shared&lt;EncodedImage&gt;</c> stream of a Casper store into a sibling
    /// <see cref="OutputStoreName"/> store with the same stream name, payloads and type, and a signed
    /// originating-time delay (positive = later on the timeline, same convention as <c>ScrubTimeShift</c>).
    /// The input store is only ever opened for reading.
    /// </summary>
    public static class DelayedStreamCopier
    {
        /// <summary>Fixed output store / partition name.</summary>
        public const string OutputStoreName = "PostProcess";

        /// <summary>Default source partition (Casper video).</summary>
        public const string DefaultPartition = "VideoRemoteApp";

        /// <summary>Default source stream (capital S).</summary>
        public const string DefaultStream = "FullScreen";

        /// <summary>
        /// Shifts <paramref name="originatingTime"/> by <paramref name="delaySec"/> seconds, exactly (tick resolution,
        /// unlike <see cref="DateTime.AddSeconds(double)"/> which rounds to milliseconds on .NET Framework).
        /// </summary>
        /// <param name="originatingTime">Raw message originating time.</param>
        /// <param name="delaySec">Signed seconds (positive = later).</param>
        /// <returns>The shifted originating time.</returns>
        public static DateTime ShiftOriginatingTime(DateTime originatingTime, double delaySec)
        {
            if (delaySec == 0.0)
            {
                return originatingTime;
            }

            long ticks = (long)Math.Round(delaySec * TimeSpan.TicksPerSecond);
            return new DateTime(originatingTime.Ticks + ticks, originatingTime.Kind);
        }

        /// <summary>
        /// Reads <paramref name="streamName"/> from the source store and writes the delayed copy under
        /// <paramref name="outRoot"/> (store <see cref="OutputStoreName"/>), replacing any previous PostProcess output via staging.
        /// </summary>
        /// <param name="sourceStoreName">Source store name (e.g. VideoRemoteApp).</param>
        /// <param name="sourceStoreRoot">Directory containing <c>sourceStoreName.0000</c>.</param>
        /// <param name="partitionLabel">Partition name used in messages.</param>
        /// <param name="streamName">Source and output stream name.</param>
        /// <param name="delaySec">Signed OT delay in seconds.</param>
        /// <param name="outRoot">Output root directory.</param>
        /// <returns>Copy summary.</returns>
        public static DelayedStreamCopyResult Copy(
            string sourceStoreName,
            string sourceStoreRoot,
            string partitionLabel,
            string streamName,
            double delaySec,
            string outRoot)
        {
            if (double.IsNaN(delaySec) || double.IsInfinity(delaySec))
            {
                throw new DelayedStreamCopyException("Delay must be a finite number of seconds.");
            }

            sourceStoreRoot = Path.GetFullPath(sourceStoreRoot);
            outRoot = Path.GetFullPath(outRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string where = $"{partitionLabel}/{streamName}";

            if (!Directory.Exists(sourceStoreRoot) || !PsiStore.Exists(sourceStoreName, sourceStoreRoot))
            {
                throw new DelayedStreamCopyException(
                    $"Source store \"{sourceStoreName}\" not found under \"{sourceStoreRoot}\" (needed for {where}).");
            }

            EnsureOutputIsSafe(sourceStoreRoot, outRoot);

            var result = new DelayedStreamCopyResult { OutputStoreRoot = outRoot };
            string staging = outRoot + ".staging-" + Guid.NewGuid().ToString("N");
            bool stagingCreated = false;
            try
            {
                using (var pipeline = Pipeline.Create("delayed-stream-copy"))
                {
                    PsiImporter importer = PsiStore.Open(pipeline, sourceStoreName, sourceStoreRoot);
                    if (!importer.Contains(streamName))
                    {
                        throw new DelayedStreamCopyException($"Source stream missing: {where}.");
                    }

                    IStreamMetadata metadata = importer.GetMetadata(streamName);
                    if (metadata == null || metadata.MessageCount <= 0)
                    {
                        throw new DelayedStreamCopyException($"Source stream is empty (no recorded messages): {where}.");
                    }

                    if (!IsSharedEncodedImage(metadata.TypeName))
                    {
                        throw new DelayedStreamCopyException(
                            $"Source stream {where} has type \"{metadata.TypeName}\"; only Shared<EncodedImage> is supported.");
                    }

                    result.FirstSourceOriginatingTime = metadata.FirstMessageOriginatingTime;
                    result.LastSourceOriginatingTime = metadata.LastMessageOriginatingTime;

                    // Fail before writing anything if the shift cannot be represented.
                    ShiftOriginatingTime(metadata.FirstMessageOriginatingTime, delaySec);
                    ShiftOriginatingTime(metadata.LastMessageOriginatingTime, delaySec);

                    Directory.CreateDirectory(staging);
                    stagingCreated = true;
                    PsiExporter exporter = PsiStore.Create(pipeline, OutputStoreName, staging);

                    long count = 0;
                    DateTime firstOut = DateTime.MinValue;
                    DateTime lastOut = DateTime.MinValue;

                    // Re-post each frame with a shifted OT (PsiStore.Copy / CopyStream preserve OT, so they cannot express a delay).
                    // The emitter must also close at the shifted time: the writer stores ClosedTime = closing OT for a
                    // replayed store, and readers (PsiStudio) drop messages whose OT is past ClosedTime, which would cut
                    // the last <delay> seconds of video off a positively delayed copy.
                    var shifter = new Processor<Shared<EncodedImage>, Shared<EncodedImage>>(
                        pipeline,
                        (frame, envelope, emitter) =>
                        {
                            DateTime shifted = ShiftOriginatingTime(envelope.OriginatingTime, delaySec);
                            emitter.Post(frame, shifted);
                            if (count == 0)
                            {
                                firstOut = shifted;
                            }

                            lastOut = shifted;
                            count++;
                        },
                        (closingTime, emitter) => emitter.Close(ShiftOriginatingTime(closingTime, delaySec)),
                        "DelayedStreamCopyShift");

                    IProducer<Shared<EncodedImage>> source = importer.OpenStream<Shared<EncodedImage>>(streamName);
                    source.PipeTo(shifter, DeliveryPolicy.SynchronousOrThrottle);
                    exporter.Write(shifter.Out, streamName, largeMessages: true, deliveryPolicy: DeliveryPolicy.SynchronousOrThrottle);

                    pipeline.Run(ReplayDescriptor.ReplayAll);

                    result.MessageCount = count;
                    result.FirstOutputOriginatingTime = firstOut;
                    result.LastOutputOriginatingTime = lastOut;
                }

                if (result.MessageCount <= 0)
                {
                    throw new DelayedStreamCopyException($"No messages were copied from {where}.");
                }

                // Backup-then-move: the previous output is only deleted once the new one is in place.
                string backup = null;
                if (Directory.Exists(outRoot))
                {
                    backup = outRoot + ".backup-" + Guid.NewGuid().ToString("N");
                    Directory.Move(outRoot, backup);
                }

                try
                {
                    Directory.Move(staging, outRoot);
                }
                catch
                {
                    if (backup != null && !Directory.Exists(outRoot))
                    {
                        Directory.Move(backup, outRoot);
                    }

                    throw;
                }

                stagingCreated = false;
                TryDeleteDirectory(backup);
            }
            finally
            {
                if (stagingCreated && Directory.Exists(staging))
                {
                    try
                    {
                        Directory.Delete(staging, recursive: true);
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Writes a sibling <c>.pds</c> that lists the PostProcess partition (optional convenience for PsiStudio).
        /// </summary>
        /// <param name="pdsPath">Output dataset path.</param>
        /// <param name="storeRoot">Directory containing <c>PostProcess.0000</c>.</param>
        public static void WriteDatasetPds(string pdsPath, string storeRoot)
        {
            string staging = pdsPath + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                var dataset = new Dataset(Path.GetFileNameWithoutExtension(pdsPath), staging, autoSave: true);
                Session session = dataset.AddEmptySession(OutputStoreName);
                session.AddPartitionFromPsiStoreAsync(OutputStoreName, storeRoot).GetAwaiter().GetResult();
                dataset.Save();
                string backup = null;
                if (File.Exists(pdsPath))
                {
                    backup = pdsPath + ".backup-" + Guid.NewGuid().ToString("N");
                    File.Move(pdsPath, backup);
                }

                try
                {
                    File.Move(staging, pdsPath);
                }
                catch
                {
                    if (backup != null && !File.Exists(pdsPath))
                    {
                        File.Move(backup, pdsPath);
                    }

                    throw;
                }

                if (backup != null)
                {
                    try
                    {
                        File.Delete(backup);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            finally
            {
                if (File.Exists(staging))
                {
                    try
                    {
                        File.Delete(staging);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        // Exactly Microsoft.Psi.Shared`1[[Microsoft.Psi.Imaging.EncodedImage, ...]] (assembly version/culture/token may vary).
        private static readonly System.Text.RegularExpressions.Regex SharedEncodedImageType =
            new System.Text.RegularExpressions.Regex(
                @"^Microsoft\.Psi\.Shared`1\[\[Microsoft\.Psi\.Imaging\.EncodedImage(,[^\[\]]*)?\]\](,[^\[\]]*)?$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static bool IsSharedEncodedImage(string typeName)
        {
            return typeName != null && SharedEncodedImageType.IsMatch(typeName.Trim());
        }

        private static void TryDeleteDirectory(string path)
        {
            if (path == null)
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Never delete or write into the input store; only an existing PostProcess-only directory may be replaced.
        /// </summary>
        private static void EnsureOutputIsSafe(string sourceStoreRoot, string outRoot)
        {
            string src = Normalize(sourceStoreRoot);
            string dst = Normalize(outRoot);
            if (src.StartsWith(dst, StringComparison.OrdinalIgnoreCase) || dst.StartsWith(src, StringComparison.OrdinalIgnoreCase))
            {
                throw new DelayedStreamCopyException(
                    $"Output root \"{outRoot}\" overlaps the input store \"{sourceStoreRoot}\"; refusing to touch the Casper store.");
            }

            if (File.Exists(outRoot))
            {
                throw new DelayedStreamCopyException($"Output root \"{outRoot}\" exists as a file; refusing to replace it.");
            }

            if (!Directory.Exists(outRoot))
            {
                return;
            }

            bool onlyPreviousOutput =
                Directory.GetFiles(outRoot).Length == 0
                && Directory.GetDirectories(outRoot).All(d =>
                    Path.GetFileName(d).StartsWith(OutputStoreName + ".", StringComparison.OrdinalIgnoreCase));
            if (!onlyPreviousOutput)
            {
                throw new DelayedStreamCopyException(
                    $"Output root \"{outRoot}\" exists and holds more than a previous {OutputStoreName} store; refusing to replace it.");
            }
        }

        private static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }
    }
}
