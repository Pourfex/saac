// <copyright file="Logigram1UnitTopicRegistry.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Maps enabled Logigram 1 Logic unit ids to AD-7 PSI topic names required as Unity→Psi inputs.
    /// Story 2.2 stub: <see cref="FirstUnitId"/> declares nodes 1–2 source topics; story 2.4 fills the unit.
    /// </summary>
    public static class Logigram1UnitTopicRegistry
    {
        /// <summary>Stub-enabled first Logic unit id (default config; unit body arrives in 2.4).</summary>
        public const string FirstUnitId = "Logigram1.FirstUnit";

        /// <summary>
        /// AD-7 source topics for <see cref="FirstUnitId"/> (exact spelling/case/spaces).
        /// </summary>
        public static readonly IReadOnlyList<string> FirstUnitTopics = Array.AsReadOnly(new[]
        {
            "Module status",
            "Porte1 ouverture",
            "Porte2 ouverture",
            "M1-SelectModule",
            "M2-SelectModule",
            "M1-Validation",
            "M2-Validation",
        });

        /// <summary>
        /// Looks up required topic names for a single unit id.
        /// </summary>
        /// <param name="unitId">Enabled unit id from config.</param>
        /// <param name="topics">AD-7 topic strings when known; otherwise null.</param>
        /// <returns>True when the unit id is registered.</returns>
        public static bool TryGetRequiredTopics(string unitId, out IReadOnlyList<string> topics)
        {
            if (string.Equals(unitId, FirstUnitId, StringComparison.Ordinal))
            {
                topics = FirstUnitTopics;
                return true;
            }

            topics = null;
            return false;
        }

        /// <summary>
        /// Resolves the distinct union of topics required by <paramref name="enabledUnits"/>.
        /// </summary>
        /// <param name="enabledUnits">Unit ids from config (null/empty → empty union).</param>
        /// <param name="unknownUnitId">Set to the first unknown id when resolution fails; otherwise null.</param>
        /// <returns>Distinct AD-7 topic names, or null when an unknown id is encountered.</returns>
        public static IReadOnlyList<string> TryResolveTopicUnion(IEnumerable<string> enabledUnits, out string unknownUnitId)
        {
            unknownUnitId = null;
            if (enabledUnits == null)
            {
                return Array.Empty<string>();
            }

            var union = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string rawId in enabledUnits)
            {
                string unitId = rawId?.Trim();
                if (string.IsNullOrEmpty(unitId))
                {
                    continue;
                }

                if (!TryGetRequiredTopics(unitId, out IReadOnlyList<string> topics))
                {
                    unknownUnitId = unitId;
                    return null;
                }

                foreach (string topic in topics)
                {
                    if (seen.Add(topic))
                    {
                        union.Add(topic);
                    }
                }
            }

            return union;
        }
    }
}
