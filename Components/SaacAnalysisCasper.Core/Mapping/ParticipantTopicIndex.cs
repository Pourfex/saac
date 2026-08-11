// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Mapping
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    /// <summary>
    /// Core-owned index of catalog topic strings that resolve to a participant.
    /// Seeded only with real <c>experiment.json</c> topic names (<c>M1-*</c>/<c>M2-*</c>, <c>1-*</c>/<c>2-*</c>, and catalog <c>Grab1</c>/<c>Grab2</c>).
    /// Shared session topics (e.g. <c>GazeEvent</c>) are not indexed here.
    /// </summary>
    public sealed class ParticipantTopicIndex
    {
        private static readonly string[] CatalogTopicsM1 =
        {
            "M1-SelectModule",
            "M1-Validation",
            "M1-ModuleOut",
            "M1-ModuleOutZone",
            "Grab1",
            "1-Head",
            "1-LeftWrist",
            "1-RightWrist",
            "1-GazeHeadOrientation",
            "1-EyeLeft",
            "1-EyeRight",
        };

        private static readonly string[] CatalogTopicsM2 =
        {
            "M2-SelectModule",
            "M2-Validation",
            "M2-ModuleOut",
            "M2-ModuleOutZone",
            "Grab2",
            "2-Head",
            "2-LeftWrist",

            // Catalog honesty: no 2-RightWrist in experiment.json.
            "2-GazeHeadOrientation",
            "2-EyeLeft",
            "2-EyeRight",
        };

        private readonly Dictionary<ParticipantId, IReadOnlyList<string>> topicsByParticipant;
        private readonly HashSet<string> allTopics;

        private ParticipantTopicIndex(
            Dictionary<ParticipantId, IReadOnlyList<string>> topicsByParticipant,
            HashSet<string> allTopics)
        {
            this.topicsByParticipant = topicsByParticipant;
            this.allTopics = allTopics;
        }

        /// <summary>
        /// Creates the default index seeded from catalog participant-scoped topics.
        /// </summary>
        /// <returns>A Core-owned participant→topic index.</returns>
        public static ParticipantTopicIndex CreateDefault()
        {
            Dictionary<ParticipantId, IReadOnlyList<string>> map = new Dictionary<ParticipantId, IReadOnlyList<string>>();
            HashSet<string> all = new HashSet<string>(StringComparer.Ordinal);

            IReadOnlyList<string> m1 = new ReadOnlyCollection<string>(CatalogTopicsM1);
            IReadOnlyList<string> m2 = new ReadOnlyCollection<string>(CatalogTopicsM2);
            map[ParticipantId.M1] = m1;
            map[ParticipantId.M2] = m2;

            for (int i = 0; i < CatalogTopicsM1.Length; i++)
            {
                all.Add(CatalogTopicsM1[i]);
            }

            for (int i = 0; i < CatalogTopicsM2.Length; i++)
            {
                all.Add(CatalogTopicsM2[i]);
            }

            return new ParticipantTopicIndex(map, all);
        }

        /// <summary>
        /// Gets the catalog topics associated with the given participant.
        /// </summary>
        /// <param name="participant">Participant to query.</param>
        /// <returns>Read-only list of catalog topic names.</returns>
        public IReadOnlyList<string> GetTopics(ParticipantId participant)
        {
            if (!this.topicsByParticipant.TryGetValue(participant, out IReadOnlyList<string> topics))
            {
                throw new ArgumentException("Unknown participant id: " + participant + ".", nameof(participant));
            }

            return topics;
        }

        /// <summary>
        /// Returns whether the index contains the given catalog topic.
        /// </summary>
        /// <param name="topic">Topic name to test.</param>
        /// <returns><c>true</c> when the topic is in the index.</returns>
        public bool Contains(string topic)
        {
            if (string.IsNullOrEmpty(topic))
            {
                return false;
            }

            return this.allTopics.Contains(topic);
        }

        /// <summary>
        /// Tries to get the participant for a topic that is present in this index.
        /// </summary>
        /// <param name="topic">Catalog topic name.</param>
        /// <param name="participant">Receives the participant when found.</param>
        /// <returns><c>true</c> when the topic is indexed.</returns>
        public bool TryGetParticipant(string topic, out ParticipantId participant)
        {
            if (!this.Contains(topic))
            {
                participant = default(ParticipantId);
                return false;
            }

            return ParticipantTopicResolver.TryResolve(topic, out participant);
        }
    }
}
