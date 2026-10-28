// <copyright file="Logigram1UnitTopicRegistry.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Maps enabled Logigram 1 Logic unit ids to AD-7 PSI topic names required as Unity→Psi inputs.
    /// Sole enableable id is <see cref="GammaPathUnitId"/> (nodes 1→2→3→C). Legacy
    /// <see cref="LegacyFirstUnitId"/> / <see cref="LegacyNode3UnitId"/> are warn+ignore only.
    /// </summary>
    public static class Logigram1UnitTopicRegistry
    {
        /// <summary>Gamma-path cycle FSM unit id (nodes 1→2→3→C).</summary>
        public const string GammaPathUnitId = "Logigram1.GammaPath";

        /// <summary>Legacy id (story 3.1); warn and ignore — not enableable.</summary>
        public const string LegacyFirstUnitId = "Logigram1.FirstUnit";

        /// <summary>Legacy id (story 3.3); warn and ignore — not enableable.</summary>
        public const string LegacyNode3UnitId = "Logigram1.Node3";

        /// <summary>
        /// Required AD-7 source topics for <see cref="GammaPathUnitId"/> (fail loud if absent).
        /// Casper stream spelling is <c>ObjectBound</c> (no space). RightWrist streams are optional.
        /// <c>M1-ModuleOut</c> / <c>M2-ModuleOut</c> are side-mapped in docs but intentionally absent here
        /// (ROUTE_DOC_ONLY — not consumed by the Gamma FSM).
        /// </summary>
        public static readonly IReadOnlyList<string> GammaPathRequiredTopics = Array.AsReadOnly(new[]
        {
            "Module status",
            "Porte1 ouverture",
            "Porte2 ouverture",
            "M1-SelectModule",
            "M2-SelectModule",
            "M1-Validation",
            "M2-Validation",
            "Area1",
            "Area2",
            "ObjectBound",
            "1-LeftWrist",
            "2-LeftWrist",
        });

        /// <summary>
        /// Optional wrist topics - attach when present; Casper stores often omit RightWrist.
        /// </summary>
        public static readonly IReadOnlyList<string> GammaPathOptionalTopics = Array.AsReadOnly(new[]
        {
            "1-RightWrist",
            "2-RightWrist",
        });

        /// <summary>
        /// Full Gamma-path catalogue (required union optional) for fixtures and docs.
        /// </summary>
        public static readonly IReadOnlyList<string> GammaPathTopics = Array.AsReadOnly(
            GammaPathRequiredTopics.Concat(GammaPathOptionalTopics).ToArray());

        /// <summary>
        /// Whether <paramref name="unitId"/> is a deprecated EnabledUnits entry (warn+ignore).
        /// </summary>
        /// <param name="unitId">Raw config unit id.</param>
        /// <returns>True for FirstUnit or Node3 legacy ids.</returns>
        public static bool IsLegacyIgnoredUnitId(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
            {
                return false;
            }

            return string.Equals(unitId, LegacyFirstUnitId, StringComparison.Ordinal)
                || string.Equals(unitId, LegacyNode3UnitId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Looks up required topic names for a single unit id.
        /// Legacy FirstUnit/Node3 ids return false (not independently enableable).
        /// </summary>
        /// <param name="unitId">Enabled unit id from config.</param>
        /// <param name="topics">AD-7 topic strings when known; otherwise null.</param>
        /// <returns>True when the unit id is the registered Gamma-path unit.</returns>
        public static bool TryGetRequiredTopics(string unitId, out IReadOnlyList<string> topics)
        {
            if (string.Equals(unitId, GammaPathUnitId, StringComparison.Ordinal))
            {
                topics = GammaPathRequiredTopics;
                return true;
            }

            topics = null;
            return false;
        }

        /// <summary>
        /// Whether <paramref name="topicName"/> is an optional Gamma-path topic (RightWrist).
        /// </summary>
        /// <param name="topicName">AD-7 topic string.</param>
        /// <returns>True when the topic may be absent without failing the run.</returns>
        public static bool IsOptionalTopic(string topicName)
        {
            if (string.IsNullOrEmpty(topicName))
            {
                return false;
            }

            foreach (string optional in GammaPathOptionalTopics)
            {
                if (string.Equals(optional, topicName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Optional topics to attach when GammaPath is among <paramref name="enabledUnits"/>.
        /// </summary>
        /// <param name="enabledUnits">Runnable unit ids (legacy already filtered).</param>
        /// <returns>Optional topic names, or empty when GammaPath is not enabled.</returns>
        public static IReadOnlyList<string> ResolveOptionalTopics(IEnumerable<string> enabledUnits)
        {
            if (enabledUnits == null)
            {
                return Array.Empty<string>();
            }

            foreach (string rawId in enabledUnits)
            {
                if (string.Equals(rawId?.Trim(), GammaPathUnitId, StringComparison.Ordinal))
                {
                    return GammaPathOptionalTopics;
                }
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// Resolves the distinct union of topics required by <paramref name="enabledUnits"/>.
        /// Legacy FirstUnit/Node3 ids are skipped (caller should warn). Unknown non-legacy ids fail.
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

                if (IsLegacyIgnoredUnitId(unitId))
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

        /// <summary>
        /// Filters <paramref name="enabledUnits"/> to the Gamma-path id only, collecting legacy ids to warn about.
        /// </summary>
        /// <param name="enabledUnits">Raw config EnabledUnits.</param>
        /// <param name="legacyIgnored">Legacy FirstUnit/Node3 ids that were listed (for warn).</param>
        /// <returns>Units that should actually run (0 or 1× GammaPath).</returns>
        public static IReadOnlyList<string> FilterRunnableUnits(
            IEnumerable<string> enabledUnits,
            out IReadOnlyList<string> legacyIgnored)
        {
            var runnable = new List<string>();
            var ignored = new List<string>();
            var seenRunnable = new HashSet<string>(StringComparer.Ordinal);
            var seenIgnored = new HashSet<string>(StringComparer.Ordinal);

            if (enabledUnits == null)
            {
                legacyIgnored = Array.Empty<string>();
                return runnable;
            }

            foreach (string rawId in enabledUnits)
            {
                string unitId = rawId?.Trim();
                if (string.IsNullOrEmpty(unitId))
                {
                    continue;
                }

                if (IsLegacyIgnoredUnitId(unitId))
                {
                    if (seenIgnored.Add(unitId))
                    {
                        ignored.Add(unitId);
                    }

                    continue;
                }

                if (string.Equals(unitId, GammaPathUnitId, StringComparison.Ordinal)
                    && seenRunnable.Add(unitId))
                {
                    runnable.Add(unitId);
                }
            }

            legacyIgnored = ignored;
            return runnable;
        }
    }
}
