// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using System.Numerics;
    using Microsoft.Psi;

    /// <summary>
    /// Derives hand-near-door proximity from wrist pose(s) + optional door world pose.
    /// </summary>
    /// <remarks>
        /// Threshold: <see cref="IndexFilterAssumptions.HandNearDoorDistanceMeters"/> (&lt;1 m).
        /// Wrists: <c>Item1</c> world meters. Catalog has no door world pose today (<c>PorteN</c> Item2 = Euler) —
        /// without a door world-pose parent the hand arm <strong>fail-closes</strong> (always false).
        /// Do not invent pose/bounds topics. M2: LeftWrist only; M1 may bind optional RightWrist.
    /// </remarks>
    public static class HandNearDoorFilter
    {
        /// <summary>
        /// Emits true when any connected wrist is within threshold of a provided door world pose.
        /// When <paramref name="doorWorldPose"/> is null, wrists are keep-alive observed and output stays false.
        /// </summary>
        /// <param name="pipeline">Owning pipeline (required for fail-closed path naming).</param>
        /// <param name="leftWrist">Required left-wrist pose stream.</param>
        /// <param name="rightWrist">Optional right-wrist pose (M1 when connected); null skips.</param>
        /// <param name="doorWorldPose">Optional door world position; null → fail-closed hand arm.</param>
        /// <param name="deliveryPolicy">Optional delivery policy for primary fusion.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Proximity bool stream (false until a door world pose parent exists).</returns>
        public static IProducer<bool> Apply(
            Pipeline pipeline,
            IProducer<Tuple<Vector3, Vector3>> leftWrist,
            IProducer<Tuple<Vector3, Vector3>>? rightWrist = null,
            IProducer<Vector3>? doorWorldPose = null,
            DeliveryPolicy? deliveryPolicy = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (leftWrist == null)
            {
                throw new ArgumentNullException(nameof(leftWrist));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(HandNearDoorFilter) : name;
            DeliveryPolicy policy = deliveryPolicy ?? DeliveryPolicy.Unlimited;

            // Keep wrists observed even when fail-closed so catalog Connect stays live.
            leftWrist.Do((_, __) => { }, DeliveryPolicy.LatestMessage, operatorName + "-LeftKeep");
            if (rightWrist != null)
            {
                rightWrist.Do((_, __) => { }, DeliveryPolicy.LatestMessage, operatorName + "-RightKeep");
            }

            if (doorWorldPose == null)
            {
                // Fail-close hand arm until Alexis confirms a pose/bounds parent (Ask First — no invented topics).
                return leftWrist.Select(
                    (Tuple<Vector3, Vector3> _) => false,
                    policy,
                    operatorName + "-FailClosed");
            }

            float threshold = IndexFilterAssumptions.HandNearDoorDistanceMeters;
            int joinMs = IndexFilterAssumptions.HandNearDoorJoinToleranceMs;
            RelativeTimeInterval joinTol = new RelativeTimeInterval(
                TimeSpan.FromMilliseconds(-joinMs),
                TimeSpan.FromMilliseconds(joinMs));

            IProducer<bool> leftNear = ProjectNear(
                leftWrist,
                doorWorldPose,
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
                doorWorldPose,
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
            IProducer<Vector3> doorPos,
            float threshold,
            RelativeTimeInterval joinTol,
            DeliveryPolicy policy,
            string operatorName)
        {
            IProducer<Tuple<Vector3, Vector3>> nonNullWrist = wrist.Where(
                (Tuple<Vector3, Vector3> pose) => pose != null,
                DeliveryPolicy.Unlimited,
                operatorName + "-NonNull");

            IProducer<Vector3> safeHand = nonNullWrist.Select(
                (Tuple<Vector3, Vector3> pose) => pose.Item1,
                DeliveryPolicy.Unlimited,
                operatorName + "-SafeHand");

            return safeHand
                .Join(doorPos, joinTol, policy, DeliveryPolicy.Unlimited, operatorName + "-Join")
                .Select(
                    (ValueTuple<Vector3, Vector3> fused) => IsNear(fused.Item1, fused.Item2, threshold),
                    DeliveryPolicy.Unlimited,
                    operatorName + "-Near");
        }

        private static bool IsNear(Vector3 hand, Vector3 door, float thresholdMeters)
        {
            return Vector3.Distance(hand, door) < thresholdMeters;
        }
    }
}
