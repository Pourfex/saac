// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Export
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Frozen CSV micro-format for analysis exports (AD-5 / AD-7). Hosts and Plugin must not fork these values.
    /// </summary>
    public static class CsvExportFormat
    {
        /// <summary>
        /// Column delimiter (comma). Chosen once for Story 1.5 — rejects Expe2 semicolon layouts.
        /// </summary>
        public const string Delimiter = ",";

        /// <summary>
        /// Time column header spelling. Aligns with Psi envelope vocabulary (not <c>utc_timestamp_ms</c>).
        /// </summary>
        public const string TimeHeader = "OriginatingTime";

        /// <summary>
        /// Time cell format applied to <c>envelope.OriginatingTime</c> only — never <see cref="DateTime.Now"/>.
        /// Printed as clock components from the envelope value (no Kind suffix / offset); do not reinterpret as local wall clock.
        /// </summary>
        public const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>
        /// Stub marker payload column header (Story 1.5).
        /// </summary>
        public const string MessageCountHeader = "MessageCount";

        /// <summary>
        /// Coincidence C payload column header (Story 1.7). Appended after <see cref="TimeHeader"/>; micro-format unchanged.
        /// </summary>
        public const string WindowMsHeader = "WindowMs";

        /// <summary>
        /// Classification label column header (Story 2.2). Values are Alpha / Beta / Gamma only
        /// (Option C — no Apprentissage/N/A product cells; see <c>Classification/OptionC.md</c>).
        /// </summary>
        public const string LabelHeader = "Label";

        /// <summary>
        /// Classification participant column header (Story 2.2). Values are M1 / M2.
        /// </summary>
        public const string ParticipantHeader = "Participant";

        /// <summary>
        /// Classification graph-id column header (Story 2.2). Keeps rows self-describing (NFR4).
        /// </summary>
        public const string GraphIdHeader = "GraphId";

        private static readonly Encoding Utf8NoBomEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Gets UTF-8 without BOM — preferred CSV file encoding for Core export contracts.
        /// </summary>
        public static Encoding Utf8WithoutBom => Utf8NoBomEncoding;

        /// <summary>
        /// Formats an originating time for a CSV time cell using <see cref="TimeFormat"/>.
        /// </summary>
        /// <param name="originatingTime">Value from <c>envelope.OriginatingTime</c> (not wall-clock now).</param>
        /// <returns>Formatted time string.</returns>
        public static string FormatOriginatingTime(DateTime originatingTime)
        {
            return originatingTime.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }
    }
}
