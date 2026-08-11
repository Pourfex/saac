// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Export
{
    using System;
    using System.Globalization;
    using System.IO;
    using SaacAnalysisCasper.Core.Classification;

    /// <summary>
    /// Writes Core CSV rows to a host-opened <see cref="TextWriter"/>.
    /// Does not open files or hold a <see cref="StreamWriter"/> as a processor sink (AD-9).
    /// Marker: <c>OriginatingTime,MessageCount</c>; Coincidence: <c>OriginatingTime,WindowMs</c>;
    /// Classification: <c>OriginatingTime,Label,Participant,GraphId</c> (one header style per writer instance).
    /// </summary>
    public sealed class CsvExportWriter
    {
        private readonly TextWriter writer;
        private HeaderKind headerKind;

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvExportWriter"/> class.
        /// </summary>
        /// <param name="writer">Already-opened text writer owned by the host.</param>
        public CsvExportWriter(TextWriter writer)
        {
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        private enum HeaderKind
        {
            None = 0,
            Marker = 1,
            Coincidence = 2,
            Classification = 3,
        }

        /// <summary>
        /// Writes the stub header row once: <c>OriginatingTime,MessageCount</c>.
        /// </summary>
        public void WriteHeader()
        {
            if (this.headerKind != HeaderKind.None)
            {
                throw new InvalidOperationException("CSV header was already written.");
            }

            this.writer.WriteLine(
                CsvExportFormat.TimeHeader
                + CsvExportFormat.Delimiter
                + CsvExportFormat.MessageCountHeader);
            this.headerKind = HeaderKind.Marker;
        }

        /// <summary>
        /// Appends one marker/count row using <see cref="CsvExportFormat"/> time formatting.
        /// </summary>
        /// <param name="originatingTime">Envelope originating time (never wall-clock now).</param>
        /// <param name="messageCount">Stub payload count for Story 1.5.</param>
        public void WriteMarkerRow(DateTime originatingTime, int messageCount)
        {
            if (this.headerKind != HeaderKind.Marker)
            {
                throw new InvalidOperationException("WriteHeader must be called before WriteMarkerRow.");
            }

            this.writer.WriteLine(
                CsvExportFormat.FormatOriginatingTime(originatingTime)
                + CsvExportFormat.Delimiter
                + messageCount.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes the coincidence C header once: <c>OriginatingTime,WindowMs</c>.
        /// Delimiter and time format remain frozen (AD-5).
        /// </summary>
        public void WriteCoincidenceHeader()
        {
            if (this.headerKind != HeaderKind.None)
            {
                throw new InvalidOperationException("CSV header was already written.");
            }

            this.writer.WriteLine(
                CsvExportFormat.TimeHeader
                + CsvExportFormat.Delimiter
                + CsvExportFormat.WindowMsHeader);
            this.headerKind = HeaderKind.Coincidence;
        }

        /// <summary>
        /// Appends one coincidence C row using <see cref="CsvExportFormat"/> time formatting.
        /// </summary>
        /// <param name="originatingTime">Envelope originating time from C (B's time; never wall-clock now).</param>
        /// <param name="windowMs">Window length W that produced this row.</param>
        public void WriteCoincidenceRow(DateTime originatingTime, int windowMs)
        {
            if (this.headerKind != HeaderKind.Coincidence)
            {
                throw new InvalidOperationException("WriteCoincidenceHeader must be called before WriteCoincidenceRow.");
            }

            this.writer.WriteLine(
                CsvExportFormat.FormatOriginatingTime(originatingTime)
                + CsvExportFormat.Delimiter
                + windowMs.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes the classification header once: <c>OriginatingTime,Label,Participant,GraphId</c>.
        /// Delimiter and time format remain frozen (AD-5).
        /// </summary>
        public void WriteClassificationHeader()
        {
            if (this.headerKind != HeaderKind.None)
            {
                throw new InvalidOperationException("CSV header was already written.");
            }

            this.writer.WriteLine(
                CsvExportFormat.TimeHeader
                + CsvExportFormat.Delimiter
                + CsvExportFormat.LabelHeader
                + CsvExportFormat.Delimiter
                + CsvExportFormat.ParticipantHeader
                + CsvExportFormat.Delimiter
                + CsvExportFormat.GraphIdHeader);
            this.headerKind = HeaderKind.Classification;
        }

        /// <summary>
        /// Appends one classification row using <see cref="CsvExportFormat"/> time formatting.
        /// </summary>
        /// <param name="originatingTime">Envelope originating time (never wall-clock now).</param>
        /// <param name="evt">Classification product payload (label, participant, graph id).</param>
        public void WriteClassificationRow(DateTime originatingTime, ClassificationEvent evt)
        {
            if (evt == null)
            {
                throw new ArgumentNullException(nameof(evt));
            }

            if (this.headerKind != HeaderKind.Classification)
            {
                throw new InvalidOperationException("WriteClassificationHeader must be called before WriteClassificationRow.");
            }

            this.writer.WriteLine(
                CsvExportFormat.FormatOriginatingTime(originatingTime)
                + CsvExportFormat.Delimiter
                + Convert.ToString(evt.Label, CultureInfo.InvariantCulture)
                + CsvExportFormat.Delimiter
                + Convert.ToString(evt.Participant, CultureInfo.InvariantCulture)
                + CsvExportFormat.Delimiter
                + Convert.ToString(evt.GraphId, CultureInfo.InvariantCulture));
        }
    }
}
