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
    /// <see cref="FirstUnitId"/> = nodes 1–2; <see cref="Node3UnitId"/> = node 3 (door/hand leave-zone).
    /// </summary>
    public static class Logigram1UnitTopicRegistry
    {
        /// <summary>First Logic unit id (nodes 1–2).</summary>
        public const string FirstUnitId = "Logigram1.FirstUnit";

        /// <summary>Node-3 Logic unit id (door-closed / hand-near + leave GeneratorArea → Gamma C).</summary>
        public const string Node3UnitId = "Logigram1.Node3";

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
        /// AD-7 source topics for <see cref="Node3UnitId"/> (exact spelling/case/spaces).
        /// Includes Porte, Area, <c>Object Bound</c> (Generateur1/2 AABB), and player wrist streams.
        /// </summary>
        public static readonly IReadOnlyList<string> Node3Topics = Array.AsReadOnly(new[]
        {
            "Porte1 ouverture",
            "Porte2 ouverture",
            "Area1",
            "Area2",
            "Object Bound",
            "1-LeftWrist",
            "1-RightWrist",
            "2-LeftWrist",
            "2-RightWrist",
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

            if (string.Equals(unitId, Node3UnitId, StringComparison.Ordinal))
            {
                topics = Node3Topics;
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
