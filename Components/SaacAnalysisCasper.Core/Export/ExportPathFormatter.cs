// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Export
{
    using System;
    using System.Globalization;
    using System.IO;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// String-only AD-5 path and stream-name helpers. Hosts create directories and open files/stores.
    /// </summary>
    public static class ExportPathFormatter
    {
        /// <summary>
        /// Formats a derived-store stream name: <c>{graphId}/{participant}/{streamRole}</c>.
        /// </summary>
        /// <param name="graphId">Graph id (e.g. <c>Poc</c>).</param>
        /// <param name="participant">Participant branch (ToString yields <c>M1</c>/<c>M2</c>).</param>
        /// <param name="streamRole">Stream role segment (e.g. <see cref="ExportStreamRoles.Marker"/>).</param>
        /// <returns>AD-5 stream name.</returns>
        public static string FormatStreamName(string graphId, ParticipantId participant, string streamRole)
        {
            RequireNonEmpty(graphId, nameof(graphId));
            RequireNonEmpty(streamRole, nameof(streamRole));
            return graphId + "/" + participant.ToString() + "/" + streamRole;
        }

        /// <summary>
        /// Formats a CSV output path: <c>{outputRoot}/{graphId}_{participant}_W{Wms}.csv</c>.
        /// </summary>
        /// <param name="outputRoot">Host output root directory.</param>
        /// <param name="graphId">Graph id.</param>
        /// <param name="participant">Participant branch.</param>
        /// <param name="windowMs">Window length in milliseconds (W).</param>
        /// <returns>Full CSV file path.</returns>
        public static string FormatCsvPath(string outputRoot, string graphId, ParticipantId participant, int windowMs)
        {
            RequireNonEmpty(outputRoot, nameof(outputRoot));
            RequireNonEmpty(graphId, nameof(graphId));
            if (windowMs <= 0)
            {
                throw new ArgumentException("windowMs must be a positive duration in milliseconds.", nameof(windowMs));
            }

            string fileName = graphId + "_" + participant.ToString() + "_W" + windowMs.ToString(CultureInfo.InvariantCulture) + ".csv";
            return Path.Combine(outputRoot, fileName);
        }

        /// <summary>
        /// Formats a derived-store directory: <c>{outputRoot}/{graphId}_{sessionId}_derived/</c>.
        /// </summary>
        /// <param name="outputRoot">Host output root directory.</param>
        /// <param name="graphId">Graph id.</param>
        /// <param name="sessionId">Session/dataset identity for attribution.</param>
        /// <returns>Derived store directory path.</returns>
        public static string FormatDerivedStoreDirectory(string outputRoot, string graphId, string sessionId)
        {
            RequireNonEmpty(outputRoot, nameof(outputRoot));
            RequireNonEmpty(graphId, nameof(graphId));
            RequireNonEmpty(sessionId, nameof(sessionId));
            return Path.Combine(outputRoot, FormatDerivedStoreName(graphId, sessionId));
        }

        /// <summary>
        /// Formats a derived-store name: <c>{graphId}_{sessionId}_derived</c>.
        /// </summary>
        /// <param name="graphId">Graph id.</param>
        /// <param name="sessionId">Session/dataset identity for attribution.</param>
        /// <returns>Store name (also used as folder leaf).</returns>
        public static string FormatDerivedStoreName(string graphId, string sessionId)
        {
            RequireNonEmpty(graphId, nameof(graphId));
            RequireNonEmpty(sessionId, nameof(sessionId));
            return graphId + "_" + sessionId + "_derived";
        }

        private static void RequireNonEmpty(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(paramName + " must be non-empty.", paramName);
            }
        }
    }
}
