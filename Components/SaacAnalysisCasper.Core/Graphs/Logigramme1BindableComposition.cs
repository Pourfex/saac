// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Graphs
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using Microsoft.Psi;
    using SAAC.PsiFormats;
    using SaacAnalysisCasper.Core.Classification;
    using SaacAnalysisCasper.Core.Logigrammes;
    using SaacAnalysisCasper.Core.Mapping;
    using SaacAnalysisCasper.Core.Windowing;

    /// <summary>
    /// Thin <see cref="IBindableComposition"/> adapter for Logigramme 1.
    /// Hosts bind catalog <see cref="Logigramme1PortRequirements.Logigramme1RequiredRoles"/> only;
    /// derived roles are produced inside Core.
    /// </summary>
    /// <remarks>
    /// Wiring is deferred until <see cref="ClassificationOut"/> is first accessed so optional
    /// <see cref="PortRoleIds.RightWrist"/> may be connected after required roles without an
    /// "already wired" failure. Required-only host bind (current DualUserGraphBinder) works unchanged.
    /// </remarks>
    public sealed class Logigramme1BindableComposition : IBindableComposition, IClassificationExportSurface
    {
        private readonly Pipeline pipeline;
        private readonly HashSet<string> connectedRoles;
        private readonly Dictionary<string, object> producersByRole;
        private readonly object wireGate;
        private IProducer<ClassificationEvent>? classificationOut;
        private bool wired;

        /// <summary>
        /// Initializes a new instance of the <see cref="Logigramme1BindableComposition"/> class.
        /// </summary>
        /// <param name="pipeline">Owning analysis pipeline.</param>
        /// <param name="participant">Participant this instance serves (AD-3).</param>
        /// <param name="windowMs">Window length W from AD-8 (closed over; not hard-coded).</param>
        public Logigramme1BindableComposition(Pipeline pipeline, ParticipantId participant, int windowMs)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            HoppingWindowPolicy.ValidateWindowMs(windowMs, nameof(windowMs));

            this.pipeline = pipeline;
            this.Participant = participant;
            this.WindowMs = windowMs;
            this.connectedRoles = new HashSet<string>(StringComparer.Ordinal);
            this.producersByRole = new Dictionary<string, object>(StringComparer.Ordinal);
            this.wireGate = new object();
            this.Name = "Logigramme1-" + participant + "-W" + windowMs;
        }

        /// <inheritdoc/>
        public string GraphId => Logigramme1Operator.GraphIdValue;

        /// <inheritdoc/>
        public ParticipantId Participant { get; }

        /// <inheritdoc/>
        public int WindowMs { get; }

        /// <inheritdoc/>
        public IReadOnlyList<string> RequiredPortRoles => Logigramme1PortRequirements.Logigramme1RequiredRoles;

        /// <summary>
        /// Gets the component name (unique per participant × W instance).
        /// </summary>
        public string Name { get; }

        /// <inheritdoc/>
        public IProducer<ClassificationEvent> ClassificationOut
        {
            get
            {
                this.EnsureWired();
                return this.classificationOut!;
            }
        }

        /// <inheritdoc/>
        public void Connect(string roleId, object producer)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                throw new ArgumentException("Port role id must be non-empty.", nameof(roleId));
            }

            if (producer == null)
            {
                throw new ArgumentNullException(nameof(producer));
            }

            lock (this.wireGate)
            {
                if (this.wired)
                {
                    if (string.Equals(roleId, PortRoleIds.RightWrist, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Optional RightWrist on " + this.Name
                            + " must be Connect'd before ClassificationOut is first accessed "
                            + "(operator already wired without RightWrist).");
                    }

                    throw new InvalidOperationException(
                        "Cannot Connect '" + roleId + "' after " + this.Name + " has been wired "
                        + "(ClassificationOut already accessed).");
                }

                if (this.connectedRoles.Contains(roleId))
                {
                    throw new InvalidOperationException(
                        "Port role '" + roleId + "' is already connected on " + this.Name + ".");
                }

                // Optional RightWrist (M1): accepted when host connects it; not in RequiredPortRoles.
                if (string.Equals(roleId, PortRoleIds.RightWrist, StringComparison.Ordinal))
                {
                    IProducer<Tuple<Vector3, Vector3>> typedRight = producer as IProducer<Tuple<Vector3, Vector3>>;
                    if (typedRight == null)
                    {
                        throw new InvalidOperationException(
                            "Role '" + roleId + "' on " + this.Name
                            + " requires IProducer<Tuple<Vector3, Vector3>>, got " + producer.GetType().FullName + ".");
                    }

                    this.producersByRole[roleId] = typedRight;
                    this.connectedRoles.Add(roleId);
                    return;
                }

                if (!IsRequiredCatalogRole(roleId))
                {
                    throw new ArgumentException(
                        "Unknown port role '" + roleId + "' for Logigramme1 (catalog RequiredPortRoles only; derived roles are internal).",
                        nameof(roleId));
                }

                object typed = CoerceCatalogProducer(roleId, producer, this.Name);
                this.producersByRole[roleId] = typed;
                this.connectedRoles.Add(roleId);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => this.Name;

        private static bool IsRequiredCatalogRole(string roleId)
        {
            IReadOnlyList<string> required = Logigramme1PortRequirements.Logigramme1RequiredRoles;
            for (int i = 0; i < required.Count; i++)
            {
                if (string.Equals(required[i], roleId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static object CoerceCatalogProducer(string roleId, object producer, string compositionName)
        {
            switch (roleId)
            {
                case PortRoleIds.SelectModule:
                    return RequireProducer<string>(roleId, producer, compositionName);
                case PortRoleIds.Validation:
                case PortRoleIds.ModuleOut:
                case PortRoleIds.ModuleOutZone:
                    return RequireProducer<bool>(roleId, producer, compositionName);
                case PortRoleIds.AddModule:
                case PortRoleIds.RemoveModule:
                    return RequireProducer<PsiBatteryModuleEvent>(roleId, producer, compositionName);
                case PortRoleIds.ModuleStatus:
                    return RequireProducer<ValueTuple<int, string>>(roleId, producer, compositionName);
                case PortRoleIds.GeneratorDoor1:
                case PortRoleIds.GeneratorDoor2:
                    return RequireProducer<ValueTuple<bool, Vector3>>(roleId, producer, compositionName);
                case PortRoleIds.GeneratorZone1:
                case PortRoleIds.GeneratorZone2:
                case PortRoleIds.Grab:
                    return RequireProducer<ValueTuple<int, bool, string>>(roleId, producer, compositionName);
                case PortRoleIds.GazeEvent:
                    return RequireProducer<PsiGazeObjectEvent>(roleId, producer, compositionName);
                case PortRoleIds.Head:
                case PortRoleIds.LeftWrist:
                case PortRoleIds.GazeHeadOrientation:
                case PortRoleIds.EyeLeft:
                case PortRoleIds.EyeRight:
                    return RequireProducer<Tuple<Vector3, Vector3>>(roleId, producer, compositionName);
                default:
                    throw new ArgumentException(
                        "Unsupported catalog role '" + roleId + "' on " + compositionName + ".",
                        nameof(roleId));
            }
        }

        private static IProducer<T> RequireProducer<T>(string roleId, object producer, string compositionName)
        {
            IProducer<T> typed = producer as IProducer<T>;
            if (typed == null)
            {
                throw new InvalidOperationException(
                    "Role '" + roleId + "' on " + compositionName
                    + " requires IProducer<" + typeof(T).FullName + ">, got " + producer.GetType().FullName + ".");
            }

            return typed;
        }

        private bool AllRequiredConnected()
        {
            IReadOnlyList<string> required = Logigramme1PortRequirements.Logigramme1RequiredRoles;
            for (int i = 0; i < required.Count; i++)
            {
                if (!this.connectedRoles.Contains(required[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void EnsureWired()
        {
            lock (this.wireGate)
            {
                if (this.wired)
                {
                    return;
                }

                if (!this.AllRequiredConnected())
                {
                    throw new InvalidOperationException(
                        this.Name + " ClassificationOut requires all catalog RequiredPortRoles connected first.");
                }

                this.WireOperatorUnlocked();
            }
        }

        private void WireOperatorUnlocked()
        {
            IProducer<string> selectModule = (IProducer<string>)this.producersByRole[PortRoleIds.SelectModule];
            IProducer<bool> validation = (IProducer<bool>)this.producersByRole[PortRoleIds.Validation];
            IProducer<ValueTuple<int, string>> moduleStatus =
                (IProducer<ValueTuple<int, string>>)this.producersByRole[PortRoleIds.ModuleStatus];
            IProducer<ValueTuple<bool, Vector3>> door1 =
                (IProducer<ValueTuple<bool, Vector3>>)this.producersByRole[PortRoleIds.GeneratorDoor1];
            IProducer<ValueTuple<bool, Vector3>> door2 =
                (IProducer<ValueTuple<bool, Vector3>>)this.producersByRole[PortRoleIds.GeneratorDoor2];
            IProducer<ValueTuple<int, bool, string>> zone1 =
                (IProducer<ValueTuple<int, bool, string>>)this.producersByRole[PortRoleIds.GeneratorZone1];
            IProducer<ValueTuple<int, bool, string>> zone2 =
                (IProducer<ValueTuple<int, bool, string>>)this.producersByRole[PortRoleIds.GeneratorZone2];
            IProducer<PsiGazeObjectEvent> gaze =
                (IProducer<PsiGazeObjectEvent>)this.producersByRole[PortRoleIds.GazeEvent];
            IProducer<Tuple<Vector3, Vector3>> leftWrist =
                (IProducer<Tuple<Vector3, Vector3>>)this.producersByRole[PortRoleIds.LeftWrist];

            IProducer<Tuple<Vector3, Vector3>>? rightWrist = null;
            object rightObj;
            if (this.producersByRole.TryGetValue(PortRoleIds.RightWrist, out rightObj))
            {
                rightWrist = (IProducer<Tuple<Vector3, Vector3>>)rightObj;
            }

            this.KeepAliveBool(PortRoleIds.ModuleOut);
            this.KeepAliveBool(PortRoleIds.ModuleOutZone);
            this.KeepAliveBatteryModule(PortRoleIds.AddModule);
            this.KeepAliveBatteryModule(PortRoleIds.RemoveModule);
            this.KeepAlivePose(PortRoleIds.Head);
            this.KeepAlivePose(PortRoleIds.GazeHeadOrientation);
            this.KeepAlivePose(PortRoleIds.EyeLeft);
            this.KeepAlivePose(PortRoleIds.EyeRight);
            this.KeepAliveIntBoolString(PortRoleIds.Grab);

            this.classificationOut = Logigramme1Operator.Apply(
                this.pipeline,
                this.Participant,
                this.WindowMs,
                selectModule,
                validation,
                moduleStatus,
                door1,
                door2,
                zone1,
                zone2,
                gaze,
                leftWrist,
                rightWrist,
                this.Name);

            this.wired = true;
        }

        private void KeepAliveBool(string roleId)
        {
            IProducer<bool> stream = (IProducer<bool>)this.producersByRole[roleId];
            stream.Do((_, __) => { }, DeliveryPolicy.LatestMessage, this.Name + "-Keep-" + roleId);
        }

        private void KeepAliveBatteryModule(string roleId)
        {
            IProducer<PsiBatteryModuleEvent> stream =
                (IProducer<PsiBatteryModuleEvent>)this.producersByRole[roleId];
            stream.Do((_, __) => { }, DeliveryPolicy.LatestMessage, this.Name + "-Keep-" + roleId);
        }

        private void KeepAlivePose(string roleId)
        {
            IProducer<Tuple<Vector3, Vector3>> stream =
                (IProducer<Tuple<Vector3, Vector3>>)this.producersByRole[roleId];
            stream.Do((_, __) => { }, DeliveryPolicy.LatestMessage, this.Name + "-Keep-" + roleId);
        }

        private void KeepAliveIntBoolString(string roleId)
        {
            IProducer<ValueTuple<int, bool, string>> stream =
                (IProducer<ValueTuple<int, bool, string>>)this.producersByRole[roleId];
            stream.Do((_, __) => { }, DeliveryPolicy.LatestMessage, this.Name + "-Keep-" + roleId);
        }
    }
}
