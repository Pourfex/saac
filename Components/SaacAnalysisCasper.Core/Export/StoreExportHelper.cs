// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Export
{
    using System;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;

    /// <summary>
    /// Thin helper to write Core producers into a host-opened <see cref="PsiExporter"/>.
    /// Hosts open stores via <c>PsiStore.Create</c>; Core processors must not open stores (AD-9).
    /// Hosts writing <c>IProducer&lt;ClassificationEvent&gt;</c> must call
    /// <c>ClassificationSerialization.EnsureRegistered</c> on the exporter/pipeline serializer set
    /// before this helper — do not project ClassificationEvent to string/int to dodge serializers.
    /// This helper does not open stores or register serializers itself.
    /// </summary>
    public static class StoreExportHelper
    {
        /// <summary>
        /// Writes <paramref name="source"/> into <paramref name="exporter"/> under <paramref name="streamName"/>.
        /// </summary>
        /// <typeparam name="T">Stream payload type.</typeparam>
        /// <param name="exporter">Opened exporter from <c>PsiStore.Create</c> (host-owned).</param>
        /// <param name="source">Core emitter/producer to persist.</param>
        /// <param name="streamName">AD-5 stream name from <see cref="ExportPathFormatter.FormatStreamName"/>.</param>
        public static void Write<T>(PsiExporter exporter, IProducer<T> source, string streamName)
        {
            if (exporter == null)
            {
                throw new ArgumentNullException(nameof(exporter));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (string.IsNullOrWhiteSpace(streamName))
            {
                throw new ArgumentException("streamName must be non-empty.", nameof(streamName));
            }

            // Known-primitive streams (e.g. int Marker) need no extra registration.
            // ClassificationEvent streams: call ClassificationSerialization.EnsureRegistered first.
            exporter.Write(source, streamName);
        }
    }
}
