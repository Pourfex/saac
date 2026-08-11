// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using System.Numerics;
    using Microsoft.Psi;

    /// <summary>
    /// Derives hand-near-door proximity from wrist pose(s) + door parent pose(s).
    /// </summary>
    /// <remarks>
    /// Distance threshold: <see cref="IndexFilterAssumptions.HandNearDoorDistanceMeters"/> meters.
    /// Door pose parent = door stream <c>Item2</c> Vector3 (not a separate catalog pose topic).
    /// Join tolerance: ±<see cref="IndexFilterAssumptions.HandNearDoorJoinToleranceMs"/> ms (not Infinite).
    /// M2: LeftWrist only. M1: LeftWrist required; optional RightWrist when connected.
    /// Dual-wrist combine uses sticky OR (not last-writer-wins Merge unwrap).
    /// </remarks>
    public static class HandNearDoorFilter
    {
        /// <summary>
        /// Emits true when any connected wrist is within threshold of any provided door position.
        /// </summary>
        /// <param name="leftWrist">Required left-wrist pose stream.</param>
        /// <param name="door1">Generator door 1 open/pose stream.</param>
        /// <param name="door2">Generator door 2 open/pose stream.</param>
        /// <param name="rightWrist">Optional right-wrist pose (M1 only when connected); null skips.</param>
        /// <param name="deliveryPolicy">Optional delivery policy for primary fusion.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Proximity bool stream.</returns>
        public static IProducer<bool> Apply(
            IProducer<Tuple<Vector3, Vector3>> leftWrist,
            IProducer<ValueTuple<bool, Vector3>> door1,
            IProducer<ValueTuple<bool, Vector3>> door2,
            IProducer<Tuple<Vector3, Vector3>>? rightWrist = null,
            DeliveryPolicy? deliveryPolicy = null,
            string? name = null)
        {
            if (leftWrist == null)
            {
                throw new ArgumentNullException(nameof(leftWrist));
            }

            if (door1 == null)
            {
                throw new ArgumentNullException(nameof(door1));
            }

            if (door2 == null)
            {
                throw new ArgumentNullException(nameof(door2));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(HandNearDoorFilter) : name;
            float threshold = IndexFilterAssumptions.HandNearDoorDistanceMeters;
            DeliveryPolicy policy = deliveryPolicy ?? DeliveryPolicy.Unlimited;
            int joinMs = IndexFilterAssumptions.HandNearDoorJoinToleranceMs;
            RelativeTimeInterval joinTol = new RelativeTimeInterval(
                TimeSpan.FromMilliseconds(-joinMs),
                TimeSpan.FromMilliseconds(joinMs));

            IProducer<Vector3> door1Pos = door1.Select(
                (ValueTuple<bool, Vector3> door) => door.Item2,
                DeliveryPolicy.Unlimited,
                operatorName + "-Door1Pos");
            IProducer<Vector3> door2Pos = door2.Select(
                (ValueTuple<bool, Vector3> door) => door.Item2,
                DeliveryPolicy.Unlimited,
                operatorName + "-Door2Pos");

            IProducer<bool> leftNear = ProjectNear(
                leftWrist,
                door1Pos,
                door2Pos,
                threshold,
                joinTol,
                policy,
                operatorName + "-Left");

            if (rightWrist == null)
            {
                return leftNear;
            }

            IProducer<bool> rightNear = ProjectNear(
                rightWrist,
                door1Pos,
                door2Pos,
                threshold,
                joinTol,
                policy,
                operatorName + "-Right");

            return BoolStreamOps.StickyOr(
                leftNear,
                rightNear,
                IndexFilterAssumptions.StickyOrWindowMs,
                operatorName + "-StickyOr");
        }

        private static IProducer<bool> ProjectNear(
            IProducer<Tuple<Vector3, Vector3>> wrist,
            IProducer<Vector3> door1Pos,
            IProducer<Vector3> door2Pos,
            float threshold,
            RelativeTimeInterval joinTol,
            DeliveryPolicy policy,
            string operatorName)
        {
            // Null-check pose Tuple before reading Item1.
            IProducer<Tuple<Vector3, Vector3>> nonNullWrist = wrist.Where(
                (Tuple<Vector3, Vector3> pose) => pose != null,
                DeliveryPolicy.Unlimited,
                operatorName + "-NonNull");

            IProducer<Vector3> safeHand = nonNullWrist.Select(
                (Tuple<Vector3, Vector3> pose) => pose.Item1,
                DeliveryPolicy.Unlimited,
                operatorName + "-SafeHand");

            return safeHand
                .Join(door1Pos, joinTol, policy, DeliveryPolicy.Unlimited, operatorName + "-J1")
                .Join(door2Pos, joinTol, DeliveryPolicy.Unlimited, DeliveryPolicy.Unlimited, operatorName + "-J2")
                .Select(
                    (ValueTuple<Vector3, Vector3, Vector3> fused) =>
                        IsNear(fused.Item1, fused.Item2, threshold)
                        || IsNear(fused.Item1, fused.Item3, threshold),
                    DeliveryPolicy.Unlimited,
                    operatorName + "-Near");
        }

        private static bool IsNear(Vector3 hand, Vector3 door, float thresholdMeters)
        {
            return Vector3.Distance(hand, door) <= thresholdMeters;
        }
    }
}
