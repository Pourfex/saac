// <copyright file="Program.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// </copyright>

namespace OpenLogigramPsiStudio
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Xml.Linq;
    using Microsoft.Psi.Data;

    /// <summary>
    /// Builds a combined Dataset (Casper + Logigram1Analysis partitions) and launches PsiStudio
    /// with the Logigram layout selected.
    /// </summary>
    public static class Program
    {
        private const string AnalysisStoreName = "Logigram1Analysis";
        private const string DefaultLayoutName = "Logigram";

        private static readonly string DefaultCasperPds =
            @"C:\Users\alexi\Documents\Dev\Ressources\Casper81\Casper5\Casper5.pds";

        private static readonly string DefaultPsiStudioExe =
            @"C:\Users\alexi\Documents\Dev\psi\Sources\Tools\PsiStudio\Microsoft.Psi.PsiStudio\bin\Release\net472\Microsoft.Psi.PsiStudio.exe";

        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">
        /// Optional: --casper &lt;Casper5.pds&gt; --analysis &lt;Casper5.Logigram1Analysis.pds|storeDir&gt;
        /// --psi-studio &lt;PsiStudio.exe&gt; --layout &lt;name&gt; --no-launch.
        /// </param>
        /// <returns>Process exit code.</returns>
        public static int Main(string[] args)
        {
            try
            {
                string casperPds = GetArg(args, "--casper") ?? DefaultCasperPds;
                string analysisArg = GetArg(args, "--analysis")
                    ?? Path.Combine(Path.GetDirectoryName(casperPds) ?? ".", "Casper5.Logigram1Analysis.pds");
                string psiStudioExe = GetArg(args, "--psi-studio") ?? DefaultPsiStudioExe;
                string layoutName = GetArg(args, "--layout") ?? DefaultLayoutName;
                bool noLaunch = args.Any(a => string.Equals(a, "--no-launch", StringComparison.OrdinalIgnoreCase));

                casperPds = Path.GetFullPath(casperPds);
                if (!File.Exists(casperPds))
                {
                    Console.Error.WriteLine("Casper dataset not found: " + casperPds);
                    return 1;
                }

                string analysisStoreRoot = ResolveAnalysisStoreRoot(analysisArg);
                if (analysisStoreRoot == null)
                {
                    Console.Error.WriteLine(
                        "Analysis store not found. Pass --analysis <Casper5.Logigram1Analysis.pds> or the store folder containing Logigram1Analysis.0000.");
                    return 1;
                }

                string combinedPds = Path.Combine(
                    Path.GetDirectoryName(casperPds) ?? ".",
                    Path.GetFileNameWithoutExtension(casperPds) + ".WithLogigram1.pds");

                Console.WriteLine("Casper dataset:   " + casperPds);
                Console.WriteLine("Analysis store:   " + analysisStoreRoot);
                Console.WriteLine("Combined dataset: " + combinedPds);

                BuildCombinedDataset(casperPds, analysisStoreRoot, combinedPds);
                EnsureLayoutSelected(layoutName);

                if (noLaunch)
                {
                    Console.WriteLine("Wrote combined dataset (launch skipped).");
                    return 0;
                }

                if (!File.Exists(psiStudioExe))
                {
                    Console.Error.WriteLine("PsiStudio not found: " + psiStudioExe);
                    Console.Error.WriteLine("Pass --psi-studio <path-to-Microsoft.Psi.PsiStudio.exe>");
                    return 1;
                }

                Console.WriteLine("Launching PsiStudio with layout '" + layoutName + "' …");
                Process.Start(new ProcessStartInfo
                {
                    FileName = psiStudioExe,
                    Arguments = "\"" + combinedPds + "\"",
                    UseShellExecute = true,
                });
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        private static void BuildCombinedDataset(string casperPds, string analysisStoreRoot, string combinedPds)
        {
            Dataset casper = Dataset.Load(casperPds);
            if (casper.Sessions == null || casper.Sessions.Count == 0)
            {
                throw new InvalidOperationException("Casper dataset has no sessions.");
            }

            Session session = casper.Sessions[0];
            bool alreadyPresent = session.Partitions != null
                && session.Partitions.Any(p =>
                    string.Equals(p.Name, AnalysisStoreName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.StoreName, AnalysisStoreName, StringComparison.OrdinalIgnoreCase));

            if (!alreadyPresent)
            {
                session.AddPartitionFromPsiStoreAsync(AnalysisStoreName, analysisStoreRoot, AnalysisStoreName)
                    .GetAwaiter()
                    .GetResult();
            }

            casper.Name = Path.GetFileNameWithoutExtension(combinedPds);
            casper.SaveAs(combinedPds);
            Console.WriteLine(
                alreadyPresent
                    ? "Analysis partition already present; refreshed combined .pds."
                    : "Added Logigram1Analysis partition to combined .pds.");
        }

        private static string ResolveAnalysisStoreRoot(string analysisArg)
        {
            string path = Path.GetFullPath(analysisArg);
            if (Directory.Exists(path))
            {
                if (Directory.Exists(Path.Combine(path, AnalysisStoreName + ".0000"))
                    || Directory.GetDirectories(path, AnalysisStoreName + ".*").Length > 0)
                {
                    return path;
                }

                // Sibling folder named Casper5.Logigram1Analysis next to a .pds argument that was a dir miss.
            }

            if (File.Exists(path) && path.EndsWith(".pds", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer sibling store folder: Casper5.Logigram1Analysis next to Casper5.Logigram1Analysis.pds
                string siblingDir = Path.Combine(
                    Path.GetDirectoryName(path) ?? ".",
                    Path.GetFileNameWithoutExtension(path));
                if (Directory.Exists(siblingDir)
                    && Directory.GetDirectories(siblingDir, AnalysisStoreName + ".*").Length > 0)
                {
                    return siblingDir;
                }

                // Or read StorePath from the analysis .pds
                Dataset analysis = Dataset.Load(path);
                IPartition partition = analysis.Sessions?
                    .SelectMany(s => s.Partitions)
                    .FirstOrDefault(p =>
                        string.Equals(p.StoreName, AnalysisStoreName, StringComparison.OrdinalIgnoreCase));
                if (partition != null && !string.IsNullOrEmpty(partition.StorePath))
                {
                    string storePath = partition.StorePath;
                    if (!Path.IsPathRooted(storePath))
                    {
                        storePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? ".", storePath));
                    }

                    // StorePath may point at …\Logigram1Analysis.0000 — parent is the store root for AddPartition.
                    if (Directory.Exists(storePath)
                        && Path.GetFileName(storePath).StartsWith(AnalysisStoreName + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.GetDirectoryName(storePath);
                    }

                    if (Directory.Exists(storePath))
                    {
                        return storePath;
                    }
                }
            }

            return null;
        }

        private static void EnsureLayoutSelected(string layoutName)
        {
            string layoutsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PsiStudio",
                "Layouts");
            string layoutFile = Path.Combine(layoutsDir, layoutName + ".plo");
            if (!File.Exists(layoutFile))
            {
                Console.WriteLine(
                    "Warning: layout '" + layoutName + "' not found at " + layoutFile
                    + ". PsiStudio will open with its last layout; create/save a layout named '"
                    + layoutName + "' in PsiStudio once.");
                return;
            }

            string settingsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PsiStudio",
                "Settings",
                Environment.MachineName);
            string settingsPath = Path.Combine(settingsDir, "PsiStudioSettings.xml");
            if (!File.Exists(settingsPath))
            {
                Console.WriteLine("Warning: PsiStudioSettings.xml not found; cannot pre-select layout.");
                return;
            }

            XDocument doc = XDocument.Load(settingsPath);
            XElement root = doc.Root;
            if (root == null)
            {
                return;
            }

            XElement layoutNode = root.Element("MostRecentlyUsedLayoutName");
            if (layoutNode == null)
            {
                layoutNode = new XElement("MostRecentlyUsedLayoutName", layoutName);
                root.Add(layoutNode);
            }
            else
            {
                layoutNode.Value = layoutName;
            }

            doc.Save(settingsPath);
            Console.WriteLine("PsiStudio layout set to '" + layoutName + "'.");
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
