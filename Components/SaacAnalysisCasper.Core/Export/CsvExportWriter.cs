// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Export
{
    using System;
    using System.Globalization;
    using System.IO;

    /// <summary>
    /// Writes Core CSV rows to a host-opened <see cref="TextWriter"/>.
    /// Does not open files or hold a <see cref="StreamWriter"/> as a processor sink (AD-9).
    /// Story 1.5 stub columns: <c>OriginatingTime,MessageCount</c> (Alpha/Beta/Gamma land in Story 2.2).
    /// </summary>
    public sealed class CsvExportWriter
    {
        private readonly TextWriter writer;
        private bool headerWritten;

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvExportWriter"/> class.
        /// </summary>
        /// <param name="writer">Already-opened text writer owned by the host.</param>
        public CsvExportWriter(TextWriter writer)
        {
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <summary>
        /// Writes the stub header row once: <c>OriginatingTime,MessageCount</c>.
        /// </summary>
        public void WriteHeader()
        {
            if (this.headerWritten)
            {
                throw new InvalidOperationException("CSV header was already written.");
            }

            this.writer.WriteLine(
                CsvExportFormat.TimeHeader
                + CsvExportFormat.Delimiter
                + CsvExportFormat.MessageCountHeader);
            this.headerWritten = true;
        }

        /// <summary>
        /// Appends one marker/count row using <see cref="CsvExportFormat"/> time formatting.
        /// </summary>
        /// <param name="originatingTime">Envelope originating time (never wall-clock now).</param>
        /// <param name="messageCount">Stub payload count for Story 1.5.</param>
        public void WriteMarkerRow(DateTime originatingTime, int messageCount)
        {
            if (!this.headerWritten)
            {
                throw new InvalidOperationException("WriteHeader must be called before WriteMarkerRow.");
            }

            this.writer.WriteLine(
                CsvExportFormat.FormatOriginatingTime(originatingTime)
                + CsvExportFormat.Delimiter
                + messageCount.ToString(CultureInfo.InvariantCulture));
        }
    }
}
