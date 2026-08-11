// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Logigrammes
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using SaacAnalysisCasper.Core.Classification;
    using SaacAnalysisCasper.Core.Indices;
    using SaacAnalysisCasper.Core.Mapping;

    /// <summary>
    /// Exclusive Miro priority mux: at most one ClassificationEvent wins per decision tick.
    /// Priority order (lower number wins): Anticipation Gamma → Gaze Gamma → Alpha → Beta → Else DoorClosed Gamma.
    /// </summary>
    internal static class Logigramme1PriorityMux
    {
        /// <summary>Anticipation Gamma (ModuleGenerationSuccess + HandNearDoor).</summary>
        public const int PriorityAnticipationGamma = 1;

        /// <summary>Gaze-path Gamma (GazeOnDoor + DoorClosed).</summary>
        public const int PriorityGazeGamma = 2;

        /// <summary>Alpha (RepeatedValidationSequence).</summary>
        public const int PriorityAlpha = 3;

        /// <summary>Beta (DifferentGeneratorButton).</summary>
        public const int PriorityBeta = 4;

        /// <summary>Else DoorClosed rising edge → Gamma.</summary>
        public const int PriorityDoorElseGamma = 5;

        /// <summary>
        /// Tagged decision candidate for mux input.
        /// </summary>
        public readonly struct Candidate
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Candidate"/> struct.
            /// </summary>
            /// <param name="priority">Priority (1 = highest).</param>
            /// <param name="label">Winning label if this candidate is selected.</param>
            public Candidate(int priority, ClassificationLabel label)
            {
                this.Priority = priority;
                this.Label = label;
            }

            /// <summary>Gets priority (lower wins).</summary>
            public int Priority { get; }

            /// <summary>Gets classification label.</summary>
            public ClassificationLabel Label { get; }
        }

        /// <summary>
        /// Merges tagged candidate pulses and emits at most one ClassificationEvent per decision tick.
        /// </summary>
        /// <param name="candidates">Tagged candidate stream (pulse when path fires).</param>
        /// <param name="participant">Participant attribution.</param>
        /// <param name="name">Operator name.</param>
        /// <returns>Exclusive classification product stream.</returns>
        public static IProducer<ClassificationEvent> Apply(
            IProducer<Candidate> candidates,
            ParticipantId participant,
            string? name = null)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(Logigramme1PriorityMux) : name;
            ParticipantId captured = participant;
            int tickMs = IndexFilterAssumptions.DecisionTickMs;

            IProducer<ClassificationEvent?> windowPick = candidates.Window(
                RelativeTimeInterval.Past(TimeSpan.FromMilliseconds(tickMs)),
                (IEnumerable<Message<Candidate>> messages) => PickBestOrNull(messages, captured),
                DeliveryPolicy.Unlimited,
                operatorName + "-TickWindow");

            ClassificationEvent? previous = null;
            return windowPick.Process(
                (ClassificationEvent? best, Envelope envelope, Emitter<ClassificationEvent> emitter) =>
                {
                    if (best == null)
                    {
                        previous = null;
                        return;
                    }

                    // Rising edge of a non-null pick (suppress sustained same-tick floods).
                    if (previous != null
                        && previous.Label == best.Label
                        && previous.Participant == best.Participant)
                    {
                        return;
                    }

                    previous = best;
                    emitter.Post(best, envelope.OriginatingTime);
                },
                DeliveryPolicy.Unlimited,
                operatorName + "-Emit");
        }

        /// <summary>
        /// Builds a candidate pulse stream from a bool edge stream.
        /// </summary>
        /// <param name="pulse">True when this path fires.</param>
        /// <param name="priority">Mux priority.</param>
        /// <param name="label">Label if selected.</param>
        /// <param name="name">Operator name.</param>
        /// <returns>Candidate stream (only posts on true pulses).</returns>
        public static IProducer<Candidate> FromPulse(
            IProducer<bool> pulse,
            int priority,
            ClassificationLabel label,
            string? name = null)
        {
            if (pulse == null)
            {
                throw new ArgumentNullException(nameof(pulse));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(FromPulse) : name;
            Candidate tagged = new Candidate(priority, label);
            return pulse.Where((bool v) => v, DeliveryPolicy.Unlimited, operatorName + "-True")
                .Select((_) => tagged, DeliveryPolicy.Unlimited, operatorName + "-Tag");
        }

        private static ClassificationEvent? PickBestOrNull(
            IEnumerable<Message<Candidate>> messages,
            ParticipantId participant)
        {
            if (messages == null)
            {
                return null;
            }

            int bestPriority = int.MaxValue;
            ClassificationLabel? bestLabel = null;

            foreach (Message<Candidate> message in messages)
            {
                Candidate c = message.Data;
                if (c.Priority < bestPriority)
                {
                    bestPriority = c.Priority;
                    bestLabel = c.Label;
                }
            }

            if (!bestLabel.HasValue)
            {
                return null;
            }

            return new ClassificationEvent(bestLabel.Value, participant, Logigramme1Operator.GraphIdValue);
        }
    }
}
