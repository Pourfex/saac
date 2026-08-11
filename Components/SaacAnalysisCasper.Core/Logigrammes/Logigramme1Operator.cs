// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Logigrammes
{
    using System;
    using System.Numerics;
    using Microsoft.Psi;
    using SAAC.PsiFormats;
    using SaacAnalysisCasper.Core.Classification;
    using SaacAnalysisCasper.Core.Indices;
    using SaacAnalysisCasper.Core.Mapping;
    using SaacAnalysisCasper.Core.Windowing;

    /// <summary>
    /// Logigramme 1 decision composition: wires derived-input filters into Alpha/Beta/Gamma
    /// <see cref="ClassificationEvent"/> emissions (speech / VisualFeedback / Apprentissage omitted).
    /// </summary>
    /// <remarks>
    /// Exclusive Miro else-tree via <see cref="Logigramme1PriorityMux"/> (at most one label per decision tick).
    /// Science W is closed over for attribution; micro lookbacks use local <see cref="RelativeTimeInterval"/> gates.
    /// </remarks>
    public static class Logigramme1Operator
    {
        /// <summary>
        /// Graph id emitted on every classification product.
        /// </summary>
        public const string GraphIdValue = "Logigramme1";

        /// <summary>
        /// Applies Logigramme 1 filter wiring and emits classification products.
        /// </summary>
        /// <param name="pipeline">Owning analysis pipeline.</param>
        /// <param name="participant">Participant branch (AD-3).</param>
        /// <param name="windowMs">Science window W from run-config (validated; closed over for attribution).</param>
        /// <param name="selectModule">Catalog SelectModule.</param>
        /// <param name="validation">Catalog Validation.</param>
        /// <param name="moduleStatus">Catalog Module status.</param>
        /// <param name="door1">Catalog Porte1 ouverture.</param>
        /// <param name="door2">Catalog Porte2 ouverture.</param>
        /// <param name="zone1">Catalog Area1.</param>
        /// <param name="zone2">Catalog Area2.</param>
        /// <param name="gazeEvent">Catalog GazeEvent.</param>
        /// <param name="leftWrist">Catalog left wrist pose.</param>
        /// <param name="rightWrist">Optional right wrist (M1); null for M2 / unbound.</param>
        /// <param name="name">Optional operator name prefix.</param>
        /// <returns>Exclusive Alpha/Beta/Gamma classification stream.</returns>
        public static IProducer<ClassificationEvent> Apply(
            Pipeline pipeline,
            ParticipantId participant,
            int windowMs,
            IProducer<string> selectModule,
            IProducer<bool> validation,
            IProducer<ValueTuple<int, string>> moduleStatus,
            IProducer<ValueTuple<bool, Vector3>> door1,
            IProducer<ValueTuple<bool, Vector3>> door2,
            IProducer<ValueTuple<int, bool, string>> zone1,
            IProducer<ValueTuple<int, bool, string>> zone2,
            IProducer<PsiGazeObjectEvent> gazeEvent,
            IProducer<Tuple<Vector3, Vector3>> leftWrist,
            IProducer<Tuple<Vector3, Vector3>>? rightWrist = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            HoppingWindowPolicy.ValidateWindowMs(windowMs, nameof(windowMs));

            if (selectModule == null)
            {
                throw new ArgumentNullException(nameof(selectModule));
            }

            if (validation == null)
            {
                throw new ArgumentNullException(nameof(validation));
            }

            if (moduleStatus == null)
            {
                throw new ArgumentNullException(nameof(moduleStatus));
            }

            if (door1 == null)
            {
                throw new ArgumentNullException(nameof(door1));
            }

            if (door2 == null)
            {
                throw new ArgumentNullException(nameof(door2));
            }

            if (zone1 == null)
            {
                throw new ArgumentNullException(nameof(zone1));
            }

            if (zone2 == null)
            {
                throw new ArgumentNullException(nameof(zone2));
            }

            if (gazeEvent == null)
            {
                throw new ArgumentNullException(nameof(gazeEvent));
            }

            if (leftWrist == null)
            {
                throw new ArgumentNullException(nameof(leftWrist));
            }

            string prefix = string.IsNullOrWhiteSpace(name) ? nameof(Logigramme1Operator) : name;

            // --- Derived filters ---
            IProducer<bool> moduleSuccess = ModuleGenerationSuccessFilter.Apply(
                moduleStatus, DeliveryPolicy.Unlimited, prefix + "-ModuleSuccess");

            IProducer<bool> doorClosed1 = DoorClosedFilter.Apply(door1, 1, DeliveryPolicy.Unlimited, prefix + "-DoorClosed1");
            IProducer<bool> doorClosed2 = DoorClosedFilter.Apply(door2, 2, DeliveryPolicy.Unlimited, prefix + "-DoorClosed2");
            IProducer<bool> doorClosedAny = BoolStreamOps.StickyOr(
                doorClosed1,
                doorClosed2,
                IndexFilterAssumptions.StickyOrWindowMs,
                prefix + "-DoorClosedAny");

            IProducer<bool> handNear = HandNearDoorFilter.Apply(
                leftWrist, door1, door2, rightWrist, DeliveryPolicy.Unlimited, prefix + "-HandNear");

            // ExitGeneratorZone keep-alive (no leaf emit path).
            IProducer<bool> exitZone1 = ExitGeneratorZoneFilter.Apply(
                pipeline, zone1, 1, DeliveryPolicy.Unlimited, prefix + "-ExitZone1");
            IProducer<bool> exitZone2 = ExitGeneratorZoneFilter.Apply(
                pipeline, zone2, 2, DeliveryPolicy.Unlimited, prefix + "-ExitZone2");
            BoolStreamOps.StickyOr(exitZone1, exitZone2, IndexFilterAssumptions.StickyOrWindowMs, prefix + "-ExitZoneOr")
                .Do((_, __) => { }, DeliveryPolicy.LatestMessage, prefix + "-ExitZoneKeepAlive");

            IProducer<bool> gazeIndicator = GazeDwellFilter.ApplyDoorClosedIndicator(
                pipeline, gazeEvent, participant, DeliveryPolicy.Unlimited, prefix + "-GazeIndicator");
            IProducer<bool> gazeDoor = GazeDwellFilter.ApplyDoor(
                pipeline, gazeEvent, participant, DeliveryPolicy.Unlimited, prefix + "-GazeDoor");

            IProducer<bool> repeatedValidationLevel = RepeatedValidationSequenceFilter.Apply(
                pipeline, selectModule, validation, DeliveryPolicy.Unlimited, prefix + "-RepeatedVal");
            IProducer<bool> repeatedValidation = BoolStreamOps.RisingEdgeOnly(
                pipeline, repeatedValidationLevel, prefix + "-AlphaEdge");

            IProducer<bool> differentButton = DifferentGeneratorButtonFilter.Apply(
                pipeline, selectModule, DeliveryPolicy.Unlimited, prefix + "-DiffButton");

            // --- Path pulses (exclusive mux inputs) ---
            RelativeTimeInterval handLookback = RelativeTimeInterval.Past(
                TimeSpan.FromMilliseconds(IndexFilterAssumptions.HandNearLookbackMs));

            // Past-or-present door interval so gaze with already-closed door still classifies.
            RelativeTimeInterval doorClosedJoin = new RelativeTimeInterval(
                TimeSpan.FromMilliseconds(-IndexFilterAssumptions.DoorClosedJoinPastMs),
                TimeSpan.FromMilliseconds(IndexFilterAssumptions.DoorClosedJoinFutureMs));

            // 1) Anticipation Gamma: success ∧ HandNear lookback
            IProducer<bool> anticipationPulse = moduleSuccess
                .Where((bool s) => s, DeliveryPolicy.Unlimited, prefix + "-SuccessTrue")
                .Join(
                    handNear.Where((bool n) => n, DeliveryPolicy.Unlimited, prefix + "-HandTrue"),
                    handLookback,
                    DeliveryPolicy.Unlimited,
                    DeliveryPolicy.Unlimited,
                    prefix + "-SuccessHandJoin")
                .Select((_) => true, DeliveryPolicy.Unlimited, prefix + "-AnticipationPulse");

            // 2) Single gaze-path Gamma: GazeOnDoor ∧ DoorClosed (indicator and no-indicator both require GazeOnDoor;
            //    avoiding parallel indicator+door leaves prevents double-fire). Indicator dwell is keep-observed.
            gazeIndicator.Do((_, __) => { }, DeliveryPolicy.LatestMessage, prefix + "-IndicatorObserve");

            IProducer<bool> gazeDoorTrue = gazeDoor.Where((bool v) => v, DeliveryPolicy.Unlimited, prefix + "-GazeDoorTrue");
            IProducer<bool> gazePathPulse = gazeDoorTrue
                .Join(
                    doorClosedAny.Where((bool v) => v, DeliveryPolicy.Unlimited, prefix + "-DoorLevelTrue"),
                    doorClosedJoin,
                    DeliveryPolicy.Unlimited,
                    DeliveryPolicy.Unlimited,
                    prefix + "-GazeDoorClosedJoin")
                .Select((_) => true, DeliveryPolicy.Unlimited, prefix + "-GazePathPulse");

            // 3) Alpha / 4) Beta pulses (already edges where applicable)
            IProducer<bool> alphaPulse = repeatedValidation;
            IProducer<bool> betaPulse = differentButton.Where((bool v) => v, DeliveryPolicy.Unlimited, prefix + "-BetaTrue");

            // 5) Else DoorClosed — rising edge only (once-per-closure), not sustained closed samples
            IProducer<bool> doorClosedEdge = BoolStreamOps.RisingEdge(
                pipeline, doorClosedAny, prefix + "-DoorClosedEdge");

            IProducer<Logigramme1PriorityMux.Candidate>[] tagged = new[]
            {
                Logigramme1PriorityMux.FromPulse(
                    anticipationPulse,
                    Logigramme1PriorityMux.PriorityAnticipationGamma,
                    ClassificationLabel.Gamma,
                    prefix + "-TagAnticipation"),
                Logigramme1PriorityMux.FromPulse(
                    gazePathPulse,
                    Logigramme1PriorityMux.PriorityGazeGamma,
                    ClassificationLabel.Gamma,
                    prefix + "-TagGaze"),
                Logigramme1PriorityMux.FromPulse(
                    alphaPulse,
                    Logigramme1PriorityMux.PriorityAlpha,
                    ClassificationLabel.Alpha,
                    prefix + "-TagAlpha"),
                Logigramme1PriorityMux.FromPulse(
                    betaPulse,
                    Logigramme1PriorityMux.PriorityBeta,
                    ClassificationLabel.Beta,
                    prefix + "-TagBeta"),
                Logigramme1PriorityMux.FromPulse(
                    doorClosedEdge,
                    Logigramme1PriorityMux.PriorityDoorElseGamma,
                    ClassificationLabel.Gamma,
                    prefix + "-TagDoorElse"),
            };

            IProducer<Logigramme1PriorityMux.Candidate> merged = Operators.Merge(
                    tagged,
                    DeliveryPolicy.Unlimited,
                    prefix + "-CandidateMerge")
                .Select(
                    (Message<Logigramme1PriorityMux.Candidate> m) => m.Data,
                    DeliveryPolicy.Unlimited,
                    prefix + "-CandidateUnwrap");

            // windowMs validated above — closed over instance for AD-8 attribution.
            _ = windowMs;

            return Logigramme1PriorityMux.Apply(merged, participant, prefix + "-ExclusiveMux");
        }
    }
}
