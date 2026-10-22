// <copyright file="AnalysisOutputPath.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.IO;
    using System.Security;

    /// <summary>
    /// OUTPUT_HOME B: resolve and validate the analysis Dataset <c>.pds</c> path
    /// (auto-sibling default or optional <c>OutputPath</c> override).
    /// </summary>
    public static class AnalysisOutputPath
    {
        /// <summary>
        /// Full sibling filename suffix including <c>.pds</c> (e.g. appended to input base name
        /// yields <c>&lt;name&gt;.Logigram1Analysis.pds</c>).
        /// </summary>
        public const string AutoSiblingSuffix = ".Logigram1Analysis.pds";

        /// <summary>
        /// Resolves the analysis output Dataset path.
        /// Non-empty <paramref name="outputPathOverride"/> wins (relative paths resolve against the
        /// input <c>.pds</c> directory); otherwise
        /// <c>&lt;inputDir&gt;/&lt;inputName&gt;.Logigram1Analysis.pds</c>.
        /// </summary>
        /// <param name="inputPdsFullPath">Absolute path to the input Casper <c>.pds</c>.</param>
        /// <param name="outputPathOverride">Optional config <c>OutputPath</c> (null/empty → sibling).</param>
        /// <returns>Absolute resolved output <c>.pds</c> path.</returns>
        public static string Resolve(string inputPdsFullPath, string outputPathOverride)
        {
            if (string.IsNullOrWhiteSpace(inputPdsFullPath))
            {
                throw new ArgumentException("Input Dataset path is required.", nameof(inputPdsFullPath));
            }

            string inputFull = Path.GetFullPath(inputPdsFullPath);
            string inputDirectory = Path.GetDirectoryName(inputFull);
            if (string.IsNullOrEmpty(inputDirectory))
            {
                throw new ArgumentException($"Cannot derive directory from input Dataset \"{inputFull}\".");
            }

            if (!string.IsNullOrWhiteSpace(outputPathOverride))
            {
                string trimmed = outputPathOverride.Trim();
                if (Path.IsPathRooted(trimmed))
                {
                    return Path.GetFullPath(trimmed);
                }

                // Relative OutputPath is relative to the input .pds directory (not process cwd).
                return Path.GetFullPath(Path.Combine(inputDirectory, trimmed));
            }

            string nameWithoutExtension = Path.GetFileNameWithoutExtension(inputFull);
            if (string.IsNullOrEmpty(nameWithoutExtension))
            {
                throw new ArgumentException($"Cannot derive sibling output path from \"{inputFull}\".");
            }

            return Path.Combine(inputDirectory, nameWithoutExtension + AutoSiblingSuffix);
        }

        /// <summary>
        /// Validates that <paramref name="resolvedOutputPds"/> does not overwrite the input store
        /// and that its parent directory is writable (created if missing).
        /// </summary>
        /// <param name="inputPdsFullPath">Absolute input <c>.pds</c>.</param>
        /// <param name="resolvedOutputPds">Absolute resolved output <c>.pds</c>.</param>
        /// <param name="error">Null on success; otherwise why validation failed.</param>
        /// <returns>True when the output path is usable.</returns>
        public static bool TryValidate(string inputPdsFullPath, string resolvedOutputPds, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(resolvedOutputPds))
            {
                error = "Resolved analysis output path is empty.";
                return false;
            }

            string inputFull;
            string outputFull;
            try
            {
                inputFull = Path.GetFullPath(inputPdsFullPath);
                outputFull = Path.GetFullPath(resolvedOutputPds);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is PathTooLongException
                || exception is NotSupportedException
                || exception is SecurityException)
            {
                error = $"Invalid analysis output path: {exception.Message}";
                return false;
            }

            if (!string.Equals(Path.GetExtension(outputFull), ".pds", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Analysis output must be a .pds Dataset file: \"{outputFull}\".";
                return false;
            }

            if (string.Equals(inputFull, outputFull, StringComparison.OrdinalIgnoreCase))
            {
                error =
                    $"Analysis output path must not overwrite the input Dataset: \"{outputFull}\".";
                return false;
            }

            // Same store roots: reject when output path is the input path under a different casing
            // already covered; also reject when output directory equals the input .pds file path.
            string inputDirectory = Path.GetDirectoryName(inputFull);
            if (!string.IsNullOrEmpty(inputDirectory)
                && string.Equals(outputFull, inputDirectory, StringComparison.OrdinalIgnoreCase))
            {
                error =
                    $"Analysis output path must not overwrite input store roots: \"{outputFull}\".";
                return false;
            }

            // Reject OutputPath whose store root collides with an existing Casper partition folder
            // beside the input (e.g. Server.pds → Server/). Allow re-runs when the folder name
            // contains Logigram1Analysis.
            string storeRoot = ResolveStoreRoot(outputFull);
            if (Directory.Exists(storeRoot)
                && !string.IsNullOrEmpty(inputDirectory)
                && string.Equals(
                    Path.GetFullPath(Path.GetDirectoryName(storeRoot) ?? string.Empty),
                    Path.GetFullPath(inputDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                string storeRootName = Path.GetFileName(
                    storeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (storeRootName.IndexOf("Logigram1Analysis", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    error =
                        $"Analysis output store root would collide with an existing folder under the input Dataset directory: \"{storeRoot}\".";
                    return false;
                }
            }

            string outputDirectory = Path.GetDirectoryName(outputFull);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                error = $"Analysis output path has no parent directory: \"{outputFull}\".";
                return false;
            }

            try
            {
                if (File.Exists(outputDirectory))
                {
                    error =
                        $"Analysis output parent path is a file, not a directory: \"{outputDirectory}\".";
                    return false;
                }

                Directory.CreateDirectory(outputDirectory);

                // Probe writability without leaving a permanent sibling of the .pds.
                string probe = Path.Combine(outputDirectory, ".logigram1-write-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException
                || exception is SecurityException
                || exception is NotSupportedException
                || exception is ArgumentException)
            {
                error =
                    $"Analysis output path is not writable (\"{outputFull}\"): {exception.Message}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Directory that will hold the analysis PsiStore partition files for a resolved <c>.pds</c>.
        /// </summary>
        /// <param name="resolvedOutputPds">Absolute output Dataset path.</param>
        /// <returns>Sibling folder named like the Dataset without extension.</returns>
        public static string ResolveStoreRoot(string resolvedOutputPds)
        {
            string full = Path.GetFullPath(resolvedOutputPds);
            string directory = Path.GetDirectoryName(full);
            string name = Path.GetFileNameWithoutExtension(full);
            return Path.Combine(directory, name);
        }
    }
}
