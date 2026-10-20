// <copyright file="FakeStreamMetadata.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

#pragma warning disable SA1600 // Elements should be documented — test stub implementing IStreamMetadata.

namespace Logigram1Analysis.Tests
{
    using System;
    using Microsoft.Psi;

    /// <summary>
    /// Minimal <see cref="IStreamMetadata"/> stub for exercising <see cref="OfflineCasperStoreConsole.HasRecordedData"/>.
    /// </summary>
    internal sealed class FakeStreamMetadata : IStreamMetadata
    {
        public string Name { get; set; } = "Stream";

        public int Id { get; set; }

        public string TypeName { get; set; } = typeof(int).AssemblyQualifiedName;

        public string SupplementalMetadataTypeName { get; set; }

        public string StoreName { get; set; } = "Store";

        public string StorePath { get; set; } = string.Empty;

        public DateTime OpenedTime { get; set; } = DateTime.MinValue;

        public DateTime ClosedTime { get; set; } = DateTime.MaxValue;

        public bool IsClosed { get; set; } = true;

        public DateTime FirstMessageCreationTime { get; set; } = DateTime.MinValue;

        public DateTime LastMessageCreationTime { get; set; } = DateTime.MinValue;

        public DateTime FirstMessageOriginatingTime { get; set; } = DateTime.MinValue;

        public DateTime LastMessageOriginatingTime { get; set; } = DateTime.MinValue;

        public long MessageCount { get; set; }

        public double AverageMessageSize { get; set; }

        public double AverageMessageLatencyMs { get; set; }

        public T GetSupplementalMetadata<T>() => default;

        public void Update(Envelope envelope, int size)
        {
        }

        public void Update(TimeInterval messagesTimeInterval, TimeInterval messagesOriginatingTimeInterval)
        {
        }
    }
}
