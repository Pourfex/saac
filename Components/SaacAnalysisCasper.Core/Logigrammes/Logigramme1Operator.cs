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
    /// Logigramme 1 decision composition: wires derived-input filters into a stateful 9-node stepper
    /// that emits Alpha/Beta/Gamma per Option C (E→Gamma, D→no-emit).
    /// </summary>
    /// <remarks>
    /// Generator pairing: M1 → Door1+Zone1, M2 → Door2+Zone2 (do not OR both gens in one participant).
    /// HandNear fail-closes until a door world-pose parent exists (Ask First — no invented topics).
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
        /// <returns>Alpha/Beta/Gamma classification stream (D paths silent).</returns>
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

            // M1 → Door1+Zone1; M2 → Door2+Zone2. Keep the unused pair observed (catalog Connect).
            int generatorIndex = (int)participant;
            IProducer<ValueTuple<bool, Vector3>> pairedDoor;
            IProducer<ValueTuple<int, bool, string>> pairedZone;
            IProducer<ValueTuple<bool, Vector3>> unusedDoor;
            IProducer<ValueTuple<int, bool, string>> unusedZone;
            if (generatorIndex == 1)
            {
                pairedDoor = door1;
                pairedZone = zone1;
                unusedDoor = door2;
                unusedZone = zone2;
            }
            else
            {
                pairedDoor = door2;
                pairedZone = zone2;
                unusedDoor = door1;
                unusedZone = zone1;
            }

            unusedDoor.Do((_, __) => { }, DeliveryPolicy.LatestMessage, prefix + "-UnusedDoorKeep");
            unusedZone.Do((_, __) => { }, DeliveryPolicy.LatestMessage, prefix + "-UnusedZoneKeep");

            // --- Derived filters (nodes 1–9) ---
            IProducer<bool> moduleSuccess = ModuleGenerationSuccessFilter.Apply(
                pipeline, moduleStatus, DeliveryPolicy.Unlimited, prefix + "-ModuleSuccess");

            IProducer<bool> doorClosed = DoorClosedFilter.Apply(
                pairedDoor, generatorIndex, DeliveryPolicy.Unlimited, prefix + "-DoorClosed");

            IProducer<bool> postDoorSelectOrValidation = PostDoorSelectOrValidationFilter.Apply(
                pipeline,
                doorClosed,
                selectModule,
                validation,
                DeliveryPolicy.Unlimited,
                prefix + "-PostDoorSelectOrVal");

            IProducer<bool> exitZone = ExitGeneratorZoneFilter.Apply(
                pipeline, pairedZone, generatorIndex, DeliveryPolicy.Unlimited, prefix + "-ExitZone");

            // HandNear: wrists wired; door world pose absent → fail-closed (always false). Door∧exit arm still works.
            IProducer<bool> handNear = HandNearDoorFilter.Apply(
                pipeline,
                leftWrist,
                rightWrist,
                doorWorldPose: null,
                DeliveryPolicy.Unlimited,
                prefix + "-HandNear");

            // Node 3: sticky/held DoorClosed∧exit and HandNear∧exit (not ±250 ms Join).
            IProducer<bool> doorAndExit = BoolStreamOps.HeldAnd(
                pipeline, doorClosed, exitZone, prefix + "-DoorAndExit");
            IProducer<bool> handAndExit = BoolStreamOps.HeldAnd(
                pipeline, handNear, exitZone, prefix + "-HandAndExit");
            IProducer<bool> node3Hit = BoolStreamOps.HeldOr(
                pipeline, doorAndExit, handAndExit, prefix + "-Node3Hit");

            IProducer<bool> gazeCombined = GazeDwellFilter.ApplyCombinedWithinWindow(
                pipeline, gazeEvent, participant, DeliveryPolicy.Unlimited, prefix + "-GazeCombine");

            RepeatedValidationSequenceFilter.Component alphaFilter = RepeatedValidationSequenceFilter.Create(
                pipeline, prefix + "-RepeatedVal");
            selectModule.PipeTo(alphaFilter.SelectModuleIn, DeliveryPolicy.Unlimited);
            validation.PipeTo(alphaFilter.ValidationIn, DeliveryPolicy.Unlimited);

            DifferentGeneratorButtonFilter.Component betaFilter = DifferentGeneratorButtonFilter.Create(
                pipeline, prefix + "-DiffButton");
            selectModule.PipeTo(betaFilter.SelectModuleIn, DeliveryPolicy.Unlimited);
            validation.PipeTo(betaFilter.ValidationIn, DeliveryPolicy.Unlimited);

            IProducer<bool> doorClosure = DoorClosureFilter.Apply(
                pipeline, pairedDoor, generatorIndex, DeliveryPolicy.Unlimited, prefix + "-DoorClosure");

            // windowMs validated above — closed over instance for AD-8 attribution.
            _ = windowMs;

            return Logigramme1NodeStepper.Apply(
                pipeline,
                participant,
                moduleSuccess,
                postDoorSelectOrValidation,
                node3Hit,
                gazeCombined,
                doorClosed,
                alphaFilter,
                betaFilter,
                doorClosure,
                prefix + "-Stepper");
        }
    }
}
