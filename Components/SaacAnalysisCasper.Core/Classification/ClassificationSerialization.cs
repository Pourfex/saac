// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Classification
{
    using System;
    using Microsoft.Psi.Serialization;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Registers Core classification types with Psi <see cref="KnownSerializers"/> for store write/read (AD-7).
    /// Hosts call <see cref="EnsureRegistered"/> before <c>StoreExportHelper.Write</c> on
    /// <see cref="ClassificationEvent"/> streams; Core does not open stores (AD-9).
    /// </summary>
    public static class ClassificationSerialization
    {
        /// <summary>
        /// Ensures <see cref="ClassificationEvent"/> and nested enums are registered for auto-generated serialization.
        /// Safe to call more than once on the same <paramref name="serializers"/> instance when flags match.
        /// </summary>
        /// <param name="serializers">Host pipeline/exporter serializer set (or <see cref="KnownSerializers.Default"/>).</param>
        public static void EnsureRegistered(KnownSerializers serializers)
        {
            if (serializers == null)
            {
                throw new ArgumentNullException(nameof(serializers));
            }

            // Nested enums registered explicitly so polymorphic / store reopen paths resolve without hand-rolled ISerializer.
            serializers.Register<ClassificationLabel>();
            serializers.Register<ParticipantId>();
            serializers.Register<ClassificationEvent>();
        }
    }
}
