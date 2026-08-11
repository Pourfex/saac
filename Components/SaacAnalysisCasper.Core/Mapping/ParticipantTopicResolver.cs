// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Mapping
{
    using System;

    /// <summary>
    /// Resolves a catalog topic and/or <c>streamToStore</c> name to a <see cref="ParticipantId"/> (AD-3 + catalog prefixes).
    /// Shared topics (e.g. <c>GazeEvent</c>) remain unresolved.
    /// </summary>
    public static class ParticipantTopicResolver
    {
        /// <summary>
        /// Tries to resolve a participant from a topic name and optional stream-to-store name.
        /// </summary>
        /// <param name="topic">Catalog topic string (may be null).</param>
        /// <param name="streamToStore">Catalog <c>streamToStore</c> string (may be null).</param>
        /// <param name="participant">Receives the resolved participant when this method returns <c>true</c>.</param>
        /// <returns><c>true</c> when a participant was resolved; otherwise <c>false</c>.</returns>
        public static bool TryResolve(string topic, string streamToStore, out ParticipantId participant)
        {
            if (TryResolveFromTopic(topic, out participant))
            {
                return true;
            }

            if (TryResolveFromSuffix(topic, out participant))
            {
                return true;
            }

            if (TryResolveFromSuffix(streamToStore, out participant))
            {
                return true;
            }

            participant = default(ParticipantId);
            return false;
        }

        /// <summary>
        /// Tries to resolve a participant from a topic name alone.
        /// </summary>
        /// <param name="topic">Catalog topic string (may be null).</param>
        /// <param name="participant">Receives the resolved participant when this method returns <c>true</c>.</param>
        /// <returns><c>true</c> when a participant was resolved; otherwise <c>false</c>.</returns>
        public static bool TryResolve(string topic, out ParticipantId participant)
        {
            return TryResolve(topic, null, out participant);
        }

        private static bool TryResolveFromTopic(string topic, out ParticipantId participant)
        {
            if (string.IsNullOrEmpty(topic))
            {
                participant = default(ParticipantId);
                return false;
            }

            if (topic.StartsWith("M1-", StringComparison.Ordinal))
            {
                participant = ParticipantId.M1;
                return true;
            }

            if (topic.StartsWith("M2-", StringComparison.Ordinal))
            {
                participant = ParticipantId.M2;
                return true;
            }

            if (topic.StartsWith("1-", StringComparison.Ordinal))
            {
                participant = ParticipantId.M1;
                return true;
            }

            if (topic.StartsWith("2-", StringComparison.Ordinal))
            {
                participant = ParticipantId.M2;
                return true;
            }

            // Catalog Grab1/Grab2 (not M1-Grab / M2-Grab).
            if (string.Equals(topic, "Grab1", StringComparison.Ordinal))
            {
                participant = ParticipantId.M1;
                return true;
            }

            if (string.Equals(topic, "Grab2", StringComparison.Ordinal))
            {
                participant = ParticipantId.M2;
                return true;
            }

            participant = default(ParticipantId);
            return false;
        }

        private static bool TryResolveFromSuffix(string name, out ParticipantId participant)
        {
            if (string.IsNullOrEmpty(name))
            {
                participant = default(ParticipantId);
                return false;
            }

            // Require a non-digit before "_1"/"_2" so names like "foo_11" do not match "_1".
            if (EndsWithParticipantSuffix(name, "_1"))
            {
                participant = ParticipantId.M1;
                return true;
            }

            if (EndsWithParticipantSuffix(name, "_2"))
            {
                participant = ParticipantId.M2;
                return true;
            }

            participant = default(ParticipantId);
            return false;
        }

        private static bool EndsWithParticipantSuffix(string name, string suffix)
        {
            if (!name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            int prefixLength = name.Length - suffix.Length;
            if (prefixLength <= 0)
            {
                return false;
            }

            char before = name[prefixLength - 1];
            return !char.IsDigit(before);
        }
    }
}
