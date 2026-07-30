// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Mapping
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Maps (<see cref="PortRoleIds"/> role id, <see cref="ParticipantId"/>) to exact catalog topic names (AD-12).
    /// Missing entries fail clearly — never silent null success.
    /// </summary>
    public sealed class PortTopicMap
    {
        private readonly Dictionary<string, string> topicByRoleAndParticipant;

        private PortTopicMap(Dictionary<string, string> topicByRoleAndParticipant)
        {
            this.topicByRoleAndParticipant = topicByRoleAndParticipant;
        }

        /// <summary>
        /// Creates the default map seeded with catalog-proven AD-12 role pairs.
        /// </summary>
        /// <returns>A Core-owned port→topic map.</returns>
        public static PortTopicMap CreateDefault()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            Add(map, PortRoleIds.SelectModule, ParticipantId.M1, "M1-SelectModule");
            Add(map, PortRoleIds.SelectModule, ParticipantId.M2, "M2-SelectModule");
            Add(map, PortRoleIds.Validation, ParticipantId.M1, "M1-Validation");
            Add(map, PortRoleIds.Validation, ParticipantId.M2, "M2-Validation");
            Add(map, PortRoleIds.ModuleOut, ParticipantId.M1, "M1-ModuleOut");
            Add(map, PortRoleIds.ModuleOut, ParticipantId.M2, "M2-ModuleOut");
            Add(map, PortRoleIds.ModuleOutZone, ParticipantId.M1, "M1-ModuleOutZone");
            Add(map, PortRoleIds.ModuleOutZone, ParticipantId.M2, "M2-ModuleOutZone");
            return new PortTopicMap(map);
        }

        /// <summary>
        /// Looks up the exact catalog topic for a role and participant.
        /// </summary>
        /// <param name="roleId">Stable port role id (see <see cref="PortRoleIds"/>).</param>
        /// <param name="participant">Participant for the dual-user branch.</param>
        /// <returns>Exact catalog topic name.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the map has no entry for the pair.</exception>
        public string GetTopic(string roleId, ParticipantId participant)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                throw new ArgumentException("Port role id must be non-empty.", nameof(roleId));
            }

            string key = MakeKey(roleId, participant);
            if (!this.topicByRoleAndParticipant.TryGetValue(key, out string topic))
            {
                throw new InvalidOperationException(
                    "No port→topic mapping for role '" + roleId + "' and participant " + participant + ".");
            }

            return topic;
        }

        /// <summary>
        /// Tries to look up the exact catalog topic for a role and participant.
        /// </summary>
        /// <param name="roleId">Stable port role id.</param>
        /// <param name="participant">Participant for the dual-user branch.</param>
        /// <param name="topic">Receives the topic when found.</param>
        /// <returns><c>true</c> when a mapping exists; otherwise <c>false</c>.</returns>
        public bool TryGetTopic(string roleId, ParticipantId participant, out string topic)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                topic = null;
                return false;
            }

            return this.topicByRoleAndParticipant.TryGetValue(MakeKey(roleId, participant), out topic);
        }

        private static void Add(Dictionary<string, string> map, string roleId, ParticipantId participant, string topic)
        {
            map.Add(MakeKey(roleId, participant), topic);
        }

        private static string MakeKey(string roleId, ParticipantId participant)
        {
            return roleId + "|" + ((int)participant).ToString();
        }
    }
}
