// <copyright file="DelayedStreamCopyConsole.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace DelayedStreamCopy
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using Microsoft.Psi.Data;

    /// <summary>
    /// CLI for <see cref="DelayedStreamCopier"/>.
    /// </summary>
    public static class DelayedStreamCopyConsole
    {
        /// <summary>
        /// Default Casper dataset (same as the other Logigram tools).
        /// </summary>
        public const string DefaultCasperPds = @"C:\Users\alexi\Documents\Dev\Ressources\Casper81\Casper5\Casper5.pds";

        /// <summary>
        /// Usage text.
        /// </summary>
        public const string Usage =
            "Usage: DelayedStreamCopy [--casper <Casper5.pds> | --store-path <dir> --store-name <name>] "
            + "[--partition VideoRemoteApp] [--stream FullScreen] [--delay-sec <signed seconds>] [--out-root <dir>] [--no-pds]";

        /// <summary>
        /// Runs the tool.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="stdout">Standard output (defaults to console).</param>
        /// <param name="stderr">Standard error (defaults to console).</param>
        /// <returns>0 on success; non-zero on failure.</returns>
        public static int Run(string[] args, TextWriter stdout = null, TextWriter stderr = null)
        {
            stdout = stdout ?? Console.Out;
            stderr = stderr ?? Console.Error;
            args = args ?? Array.Empty<string>();

            try
            {
                if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
                {
                    stdout.WriteLine(Usage);
                    return 0;
                }

                string casperPds = GetArg(args, "--casper");
                string storePathArg = GetArg(args, "--store-path");
                string storeNameArg = GetArg(args, "--store-name");
                string partition = GetArg(args, "--partition") ?? DelayedStreamCopier.DefaultPartition;
                string stream = GetArg(args, "--stream") ?? DelayedStreamCopier.DefaultStream;
                string outRoot = GetArg(args, "--out-root");
                bool writePds = !args.Any(a => string.Equals(a, "--no-pds", StringComparison.OrdinalIgnoreCase));

                double delaySec = 0.0;
                string delayArg = GetArg(args, "--delay-sec");
                bool delayFlagPresent = args.Any(a => string.Equals(a, "--delay-sec", StringComparison.OrdinalIgnoreCase));
                if (delayFlagPresent
                    && (delayArg == null
                        || !double.TryParse(delayArg, NumberStyles.Float, CultureInfo.InvariantCulture, out delaySec)))
                {
                    stderr.WriteLine("FAIL: --delay-sec must be a number (seconds, may be negative).");
                    stderr.WriteLine(Usage);
                    return 2;
                }

                string storeName;
                string storeRoot;
                if (storePathArg != null)
                {
                    storeRoot = storePathArg;
                    storeName = storeNameArg ?? partition;
                }
                else
                {
                    casperPds = Path.GetFullPath(casperPds ?? DefaultCasperPds);
                    ResolveFromCasper(casperPds, partition, stream, out storeName, out storeRoot);
                    if (outRoot == null)
                    {
                        outRoot = Path.Combine(
                            Path.GetDirectoryName(casperPds) ?? ".",
                            Path.GetFileNameWithoutExtension(casperPds) + "." + DelayedStreamCopier.OutputStoreName);
                    }
                }

                if (outRoot == null)
                {
                    stderr.WriteLine("FAIL: --out-root is required with --store-path.");
                    stderr.WriteLine(Usage);
                    return 2;
                }

                outRoot = Path.GetFullPath(outRoot);
                stdout.WriteLine("DelayedStreamCopy");
                stdout.WriteLine($"  source = {partition}/{stream} (store \"{storeName}\" @ {Path.GetFullPath(storeRoot)})");
                stdout.WriteLine($"  delaySec = {delaySec.ToString("F3", CultureInfo.InvariantCulture)} (OT = raw + delay)");
                stdout.WriteLine($"  outRoot = {outRoot} (store {DelayedStreamCopier.OutputStoreName})");

                DelayedStreamCopyResult result = DelayedStreamCopier.Copy(storeName, storeRoot, partition, stream, delaySec, outRoot);

                stdout.WriteLine($"Wrote store {DelayedStreamCopier.OutputStoreName} @ {result.OutputStoreRoot}");
                stdout.WriteLine($"  stream {stream}: {result.MessageCount} message(s), type Shared<EncodedImage>");
                stdout.WriteLine($"  source OT {result.FirstSourceOriginatingTime:o} .. {result.LastSourceOriginatingTime:o}");
                stdout.WriteLine($"  output OT {result.FirstOutputOriginatingTime:o} .. {result.LastOutputOriginatingTime:o}");

                if (writePds)
                {
                    string pdsPath = outRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".pds";
                    try
                    {
                        DelayedStreamCopier.WriteDatasetPds(pdsPath, outRoot);
                        stdout.WriteLine($"Wrote dataset {pdsPath}");
                    }
                    catch (Exception exception)
                    {
                        stderr.WriteLine($"Warning: store written but optional .pds failed: {exception.Message}");
                    }
                }

                return 0;
            }
            catch (DelayedStreamCopyException exception)
            {
                stderr.WriteLine("FAIL: " + exception.Message);
                return 1;
            }
            catch (Exception exception)
            {
                stderr.WriteLine($"FAIL: {exception.GetType().Name}: {exception.Message}");
                return 1;
            }
        }

        private static void ResolveFromCasper(string casperPds, string partitionName, string streamName, out string storeName, out string storeRoot)
        {
            if (!File.Exists(casperPds))
            {
                throw new DelayedStreamCopyException($"Casper dataset not found: {casperPds}");
            }

            Dataset dataset = Dataset.Load(casperPds);
            IPartition partition = dataset.Sessions?
                .SelectMany(s => s.Partitions)
                .FirstOrDefault(p =>
                    string.Equals(p.Name, partitionName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.StoreName, partitionName, StringComparison.OrdinalIgnoreCase));
            if (partition == null || string.IsNullOrEmpty(partition.StorePath))
            {
                throw new DelayedStreamCopyException(
                    $"Source stream missing: partition \"{partitionName}\" (stream \"{streamName}\") not found in {casperPds}.");
            }

            storeName = partition.StoreName;
            string storePath = partition.StorePath;
            if (!Path.IsPathRooted(storePath))
            {
                storePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(casperPds) ?? ".", storePath));
            }

            // StorePath may point at …\VideoRemoteApp.0000 — the store root is its parent.
            storeRoot = Path.GetFileName(storePath).StartsWith(storeName + ".", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(storePath)
                : storePath;
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
