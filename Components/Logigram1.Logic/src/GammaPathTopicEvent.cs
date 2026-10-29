// <copyright file="GammaPathTopicEvent.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.Logigram1.Logic
{
    using System;

    /// <summary>
    /// One AD-7 topic payload destined for <see cref="Logigram1GammaPathUnit.AddOrderedTopicInput"/>.
    /// The originating time travels on the Psi envelope; the payload type is the catalogue type of the topic.
    /// </summary>
    public sealed class GammaPathTopicEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GammaPathTopicEvent"/> class.
        /// </summary>
        /// <param name="topicName">Exact AD-7 topic / stream name.</param>
        /// <param name="payload">Catalogue-typed payload for the topic.</param>
        public GammaPathTopicEvent(string topicName, object payload)
        {
            this.TopicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
            this.Payload = payload;
        }

        /// <summary>Gets the exact AD-7 topic / stream name.</summary>
        public string TopicName { get; }

        /// <summary>Gets the catalogue-typed payload.</summary>
        public object Payload { get; }
    }
}
