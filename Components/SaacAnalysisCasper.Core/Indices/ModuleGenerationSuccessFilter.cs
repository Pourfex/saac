// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;
    using Microsoft.Psi.Components;

    /// <summary>
    /// Derives module-generation success from catalog <c>ModuleStatus</c> (<c>ValueTuple&lt;int,string&gt;</c>).
    /// </summary>
    /// <remarks>
    /// First-unseen module <c>Item1</c> id — session-global seen-set shared across both participant pipelines
    /// (<see cref="IndexFilterAssumptions.ModuleGenerationSuccessHeuristic"/>).
    /// Emits streams only (AD-9); preserves envelope OriginatingTime.
    /// Node 1 miss is timeout/non-event in the stepper — already-seen samples post false and must not force 1→4.
    /// </remarks>
    public static class ModuleGenerationSuccessFilter
    {
        private static readonly object SeenGate = new object();

        private static readonly HashSet<int> SeenModuleIds = new HashSet<int>();

        /// <summary>
        /// Clears the session-global seen module-id set.
        /// Call once per analysis run (factory/host) before either participant wires — never M1-only Apply.
        /// </summary>
        public static void ResetSession()
        {
            lock (SeenGate)
            {
                SeenModuleIds.Clear();
            }
        }

        /// <summary>
        /// Projects each module-status message to a first-unseen success bool.
        /// </summary>
        /// <param name="pipeline">Owning pipeline (stateful detector).</param>
        /// <param name="moduleStatus">Catalog Module status stream.</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Stream of success flags (true = first-unseen module id this session).</returns>
        public static IProducer<bool> Apply(
            Pipeline pipeline,
            IProducer<ValueTuple<int, string>> moduleStatus,
            DeliveryPolicy<ValueTuple<int, string>>? deliveryPolicy = null,
            string? name = null)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (moduleStatus == null)
            {
                throw new ArgumentNullException(nameof(moduleStatus));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(ModuleGenerationSuccessFilter) : name;
            FirstUnseenDetector detector = new FirstUnseenDetector(pipeline, operatorName);
            moduleStatus.PipeTo(detector.In, deliveryPolicy ?? DeliveryPolicy.Unlimited);
            return detector.Out;
        }

        /// <summary>
        /// Tries to register <paramref name="moduleId"/> as newly seen.
        /// </summary>
        /// <param name="moduleId">ModuleStatus Item1 id.</param>
        /// <returns>True when this id was not previously in the session-global set.</returns>
        public static bool TryRegisterFirstUnseen(int moduleId)
        {
            lock (SeenGate)
            {
                return SeenModuleIds.Add(moduleId);
            }
        }

        private sealed class FirstUnseenDetector : ConsumerProducer<ValueTuple<int, string>, bool>
        {
            public FirstUnseenDetector(Pipeline pipeline, string name)
                : base(pipeline, name)
            {
            }

            /// <inheritdoc/>
            protected override void Receive(ValueTuple<int, string> data, Envelope envelope)
            {
                bool firstUnseen = TryRegisterFirstUnseen(data.Item1);
                this.Out.Post(firstUnseen, envelope.OriginatingTime);
            }
        }
    }
}
