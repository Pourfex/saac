// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Export
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Best-effort abort cleanup: rename partial artifacts to <c>*.incomplete</c> or delete them.
    /// </summary>
    public static class IncompleteExportCleanup
    {
        /// <summary>
        /// Marks each existing file incomplete (rename to <c>*.incomplete</c>) or deletes it.
        /// </summary>
        /// <param name="filePaths">CSV or other file paths created during the run.</param>
        /// <param name="log">Optional diagnostic logger; IO failures are swallowed after attempt.</param>
        public static void MarkIncompleteOrDelete(IEnumerable<string> filePaths, Action<string>? log = null)
        {
            if (filePaths == null)
            {
                return;
            }

            foreach (string path in filePaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    string incompletePath = path + ".incomplete";
                    if (File.Exists(incompletePath))
                    {
                        File.Delete(incompletePath);
                    }

                    try
                    {
                        File.Move(path, incompletePath);
                        log?.Invoke("Marked incomplete file: " + incompletePath);
                    }
                    catch (Exception renameEx) when (renameEx is IOException || renameEx is UnauthorizedAccessException)
                    {
                        File.Delete(path);
                        log?.Invoke("Deleted incomplete file (rename failed): " + path);
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke("Incomplete file cleanup failed for '" + path + "': " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Marks an existing directory incomplete (rename to <c>*.incomplete</c>) or deletes the tree.
        /// </summary>
        /// <param name="directoryPath">Derived store directory path.</param>
        /// <param name="log">Optional diagnostic logger; IO failures are swallowed after attempt.</param>
        public static void MarkIncompleteOrDeleteDirectory(string directoryPath, Action<string>? log = null)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    return;
                }

                string incompletePath = directoryPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + ".incomplete";

                if (Directory.Exists(incompletePath))
                {
                    Directory.Delete(incompletePath, recursive: true);
                }

                try
                {
                    Directory.Move(directoryPath, incompletePath);
                    log?.Invoke("Marked incomplete directory: " + incompletePath);
                }
                catch (Exception renameEx) when (renameEx is IOException || renameEx is UnauthorizedAccessException)
                {
                    Directory.Delete(directoryPath, recursive: true);
                    log?.Invoke("Deleted incomplete directory (rename failed): " + directoryPath);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("Incomplete directory cleanup failed for '" + directoryPath + "': " + ex.Message);
            }
        }
    }
}
