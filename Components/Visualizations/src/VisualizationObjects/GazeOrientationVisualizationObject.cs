// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SAAC.Visualizations
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.Serialization;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Media3D;
    using HelixToolkit.Wpf;
    using Microsoft.Psi;
    using Microsoft.Psi.Data;
    using Microsoft.Psi.Visualization;
    using Microsoft.Psi.Visualization.Data;
    using Microsoft.Psi.Visualization.VisualizationObjects;
    using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

    /// <summary>
    /// Gaze ray for <c>N-GazeHeadOrientation</c>: Item1 = world position, Item2 = Euler degrees.
    /// Forward + sphere-raycast match story 4.2 (radius 0, maxDistance 3 m by default).
    /// Raycast runs in Unity/store space; display uses <see cref="ReverseYZ"/> (default on).
    /// </summary>
    [VisualizationObject("Gaze Orientation")]
    public class GazeOrientationVisualizationObject : ModelVisual3DValueVisualizationObject<Tuple<Vector3, Vector3>>
    {
        private const string ObjectBoundStreamName = "ObjectBound";

        private readonly ArrowVisual3D rayArrow;
        private readonly SphereVisual3D originMarker;
        private readonly SphereVisual3D hitMarker;
        private readonly BillboardTextVisual3D statusBillboard;
        private readonly Rect3DVisualizationObject nearestHitBox;

        private Dictionary<string, (Vector3 Center, Vector3 Size)> boundsByName =
            new Dictionary<string, (Vector3 Center, Vector3 Size)>(StringComparer.Ordinal);

        private ObservableKeyedCache<DateTime, Message<ValueTuple<string, Vector3, Vector3>>>.ObservableKeyedView? objectBoundsView;

        // Defaults match person 1; OnStreamBound recolors from stream name (1- = yellow/orange, 2- = red/black).
        private Color rayColor = Colors.Yellow;
        private Color hitColor = Colors.Orange;
        private double rayThicknessMm = 25;
        private float maxDistanceM = 3f;
        private float radiusM = 0f;
        private bool reverseYZ = true;
        private bool highlightHits = true;
        private bool ignoreZones = true;
        private string hitLabel = string.Empty;
        private DateTime lastEnvelopeTime = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="GazeOrientationVisualizationObject"/> class.
        /// </summary>
        public GazeOrientationVisualizationObject()
        {
            this.rayArrow = new ArrowVisual3D
            {
                Diameter = this.rayThicknessMm / 1000.0,
                Fill = new SolidColorBrush(this.rayColor),
                ThetaDiv = 8,
            };

            this.originMarker = new SphereVisual3D
            {
                Radius = 0.05,
                Fill = new SolidColorBrush(this.rayColor),
                PhiDiv = 10,
                ThetaDiv = 10,
            };

            this.hitMarker = new SphereVisual3D
            {
                Radius = 0.06,
                Fill = new SolidColorBrush(this.hitColor),
                PhiDiv = 10,
                ThetaDiv = 10,
            };

            this.statusBillboard = new BillboardTextVisual3D
            {
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush(Color.FromArgb(180, 30, 30, 30)),
                FontSize = 13,
                Padding = new Thickness(5),
                Text = "Gaze: (no data)",
            };

            this.nearestHitBox = new Rect3DVisualizationObject
            {
                Color = this.hitColor,
                Opacity = 95,
                ThicknessMm = 28,
            };

            this.UpdateVisibility();
        }

        /// <summary>
        /// Gets or sets the gaze ray color.
        /// </summary>
        [DataMember]
        [PropertyOrder(1)]
        [Description("Color of the gaze ray.")]
        public Color RayColor
        {
            get { return this.rayColor; }
            set { this.Set(nameof(this.RayColor), ref this.rayColor, value); }
        }

        /// <summary>
        /// Gets or sets the hit highlight color.
        /// </summary>
        [DataMember]
        [PropertyOrder(2)]
        [DisplayName("Hit Color")]
        [Description("Color used for hit markers and highlighted ObjectBound boxes.")]
        public Color HitColor
        {
            get { return this.hitColor; }
            set { this.Set(nameof(this.HitColor), ref this.hitColor, value); }
        }

        /// <summary>
        /// Gets or sets the ray thickness in millimeters.
        /// </summary>
        [DataMember]
        [PropertyOrder(3)]
        [DisplayName("Thickness (mm)")]
        [Description("Gaze ray diameter in millimeters.")]
        public double ThicknessMm
        {
            get { return this.rayThicknessMm; }
            set { this.Set(nameof(this.ThicknessMm), ref this.rayThicknessMm, value); }
        }

        /// <summary>
        /// Gets or sets max raycast distance in meters (story 4.2 lock = 3).
        /// </summary>
        [DataMember]
        [PropertyOrder(4)]
        [DisplayName("Max Distance (m)")]
        [Description("Sphere-raycast max distance in meters (locked 3.0 for Alpha/Beta gaze).")]
        public float MaxDistanceM
        {
            get { return this.maxDistanceM; }
            set { this.Set(nameof(this.MaxDistanceM), ref this.maxDistanceM, value < 0f ? 0f : value); }
        }

        /// <summary>
        /// Gets or sets sphere radius inflate in meters (story 4.2 lock = 0).
        /// </summary>
        [DataMember]
        [PropertyOrder(5)]
        [DisplayName("Radius (m)")]
        [Description("Sphere-raycast radius inflate around each AABB (locked 0.0 for Alpha/Beta gaze).")]
        public float RadiusM
        {
            get { return this.radiusM; }
            set { this.Set(nameof(this.RadiusM), ref this.radiusM, value < 0f ? 0f : value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether Y and Z are swapped for display.
        /// </summary>
        [DataMember]
        [PropertyOrder(6)]
        [DisplayName("ReverseYZ")]
        [Description("Reverse Y & Z axes for display (default on). Raycast stays in store/Unity space.")]
        public bool ReverseYZ
        {
            get { return this.reverseYZ; }
            set { this.Set(nameof(this.ReverseYZ), ref this.reverseYZ, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the nearest ObjectBound hit is highlighted.
        /// </summary>
        [DataMember]
        [PropertyOrder(7)]
        [DisplayName("Highlight Hits")]
        [Description("Highlight only the nearest non-ignored ObjectBound the gaze ray hits.")]
        public bool HighlightHits
        {
            get { return this.highlightHits; }
            set { this.Set(nameof(this.HighlightHits), ref this.highlightHits, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether names containing "zone" are skipped by raycast.
        /// </summary>
        [DataMember]
        [PropertyOrder(8)]
        [DisplayName("Ignore Zones")]
        [Description("Skip ObjectBound names containing \"zone\" (large volumes that eat the ray). Default on.")]
        public bool IgnoreZones
        {
            get { return this.ignoreZones; }
            set { this.Set(nameof(this.IgnoreZones), ref this.ignoreZones, value); }
        }

        /// <summary>
        /// Gets the nearest hit ObjectBound name at the cursor (empty if none).
        /// </summary>
        [PropertyOrder(9)]
        [DisplayName("Hit Label")]
        [Description("Nearest ObjectBound name currently hit by the gaze ray.")]
        public string HitLabel => this.hitLabel;

        /// <summary>
        /// Gets how many ObjectBound entries were preloaded for raycast.
        /// </summary>
        [PropertyOrder(10)]
        [DisplayName("Bound Count")]
        [Description("Number of ObjectBound names loaded for hit tests.")]
        public int BoundCount => this.boundsByName.Count;

        /// <inheritdoc/>
        public override void UpdateVisual3D()
        {
            this.UpdateGazeVisuals();
            this.UpdateVisibility();
        }

        /// <inheritdoc/>
        public override void NotifyPropertyChanged(string propertyName)
        {
            if (propertyName == nameof(this.RayColor))
            {
                this.rayArrow.Fill = new SolidColorBrush(this.RayColor);
                this.originMarker.Fill = new SolidColorBrush(this.RayColor);
            }
            else if (propertyName == nameof(this.HitColor))
            {
                this.hitMarker.Fill = new SolidColorBrush(this.HitColor);
                this.nearestHitBox.Color = this.HitColor;
            }
            else if (propertyName == nameof(this.ThicknessMm))
            {
                this.rayArrow.Diameter = this.ThicknessMm / 1000.0;
            }
            else if (propertyName == nameof(this.MaxDistanceM) ||
                     propertyName == nameof(this.RadiusM) ||
                     propertyName == nameof(this.ReverseYZ) ||
                     propertyName == nameof(this.HighlightHits) ||
                     propertyName == nameof(this.IgnoreZones))
            {
                this.UpdateGazeVisuals();
                this.UpdateVisibility();
            }
            else if (propertyName == nameof(this.Visible))
            {
                this.UpdateVisibility();
            }
        }

        /// <inheritdoc/>
        protected override void OnStreamBound()
        {
            base.OnStreamBound();
            this.ApplyColorsForStreamName(this.SourceStreamName);
            if (IsGazeHeadOrientationStream(this.SourceStreamName))
            {
                this.EnsureObjectBoundsLoaded();
                this.ScheduleObjectBoundsRetry();
            }
            else
            {
                this.SetHitLabel(string.Empty);
                this.statusBillboard.Text =
                    "Gaze Orientation: bind 1-GazeHeadOrientation or 2-GazeHeadOrientation (not Eye/Wrist)";
            }

            this.UpdateVisibility();
        }

        /// <summary>
        /// True only for <c>1-GazeHeadOrientation</c> / <c>2-GazeHeadOrientation</c> (and bare
        /// <c>GazeHeadOrientation</c>). Eye / wrist / head pose streams share the same CLR type but must not raycast.
        /// </summary>
        /// <param name="streamName">Bound stream name.</param>
        /// <returns>Whether this visualizer should draw a gaze ray.</returns>
        internal static bool IsGazeHeadOrientationStream(string? streamName)
        {
            if (string.IsNullOrEmpty(streamName))
            {
                return false;
            }

            // Accept "GazeHeadOrientation", "1-GazeHeadOrientation", "2-GazeHeadOrientation" (case-insensitive).
            // Reject "1-EyeLeft", "1-EyeRight", "1-LeftWrist", "1-RightWrist", "1-Head", etc.
            return streamName.Equals("GazeHeadOrientation", StringComparison.OrdinalIgnoreCase)
                || streamName.EndsWith("-GazeHeadOrientation", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Person 1 → yellow ray / orange highlight; person 2 → red ray / black highlight.
        /// Only applies to GazeHeadOrientation streams (not Eye/Wrist).
        /// </summary>
        private void ApplyColorsForStreamName(string? streamName)
        {
            if (!IsGazeHeadOrientationStream(streamName))
            {
                return;
            }

            bool person2 = streamName.StartsWith("2-", StringComparison.OrdinalIgnoreCase)
                || streamName.IndexOf("2-GazeHeadOrientation", StringComparison.OrdinalIgnoreCase) >= 0;
            bool person1 = streamName.StartsWith("1-", StringComparison.OrdinalIgnoreCase)
                || streamName.IndexOf("1-GazeHeadOrientation", StringComparison.OrdinalIgnoreCase) >= 0;

            if (person2)
            {
                this.RayColor = Colors.Red;
                this.HitColor = Colors.Black;
            }
            else if (person1)
            {
                this.RayColor = Colors.Yellow;
                this.HitColor = Colors.Orange;
            }
        }

        private void ScheduleObjectBoundsRetry()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => this.EnsureObjectBoundsLoaded()));
        }

        /// <summary>
        /// Prefer sibling Object Bounds viz in the same panel; else read ObjectBound from the Dataset.
        /// </summary>
        private void EnsureObjectBoundsLoaded()
        {
            if (this.TryShareBoundsFromPanelSibling())
            {
                return;
            }

            if (this.boundsByName.Count == 0)
            {
                this.TryBindObjectBoundCompanion();
            }
        }

        private bool TryShareBoundsFromPanelSibling()
        {
            try
            {
                var panel = this.Panel;
                if (panel?.VisualizationObjects == null)
                {
                    return false;
                }

                foreach (var vo in panel.VisualizationObjects)
                {
                    if (vo is ObjectBoundVisualizationObject boundsViz && boundsViz.BoundCount > 0)
                    {
                        this.boundsByName = boundsViz.CopyBoundsSnapshot();
                        this.RaisePropertyChanged(nameof(this.BoundCount));
                        return this.boundsByName.Count > 0;
                    }
                }
            }
            catch (Exception)
            {
                // Sibling share is best-effort.
            }

            return false;
        }

        /// <inheritdoc/>
        protected override void OnStreamUnbound()
        {
            this.StopObjectBoundPreload();
            this.SetHitLabel(string.Empty);
            this.statusBillboard.Text = "Gaze: (unbound)";
            base.OnStreamUnbound();
        }

        private void UpdateGazeVisuals()
        {
            if (!IsGazeHeadOrientationStream(this.SourceStreamName))
            {
                // Same Tuple<Vector3,Vector3> type as Eye/Wrist/Head — refuse raycast for those streams.
                this.SetHitLabel(string.Empty);
                this.statusBillboard.Text =
                    "Gaze Orientation: bind 1-GazeHeadOrientation or 2-GazeHeadOrientation (not Eye/Wrist)";
                return;
            }

            if (!this.HasCurrentValue || this.CurrentData == null)
            {
                this.SetHitLabel(string.Empty);
                this.statusBillboard.Text = "Gaze: (no sample at cursor)";
                return;
            }

            this.lastEnvelopeTime = this.CurrentOriginatingTime;

            // Layout reopen: ObjectBound companion often binds before the store/catalog is ready.
            if (this.boundsByName.Count == 0)
            {
                this.EnsureObjectBoundsLoaded();
            }

            Vector3 origin = this.CurrentData.Item1;
            Vector3 forward = EulerDegreesToForward(this.CurrentData.Item2);
            Vector3 displayOrigin = this.ToDisplay(origin);
            Vector3 displayForward = this.ToDisplayDirection(forward);

            // Always draw the full max-distance ray so direction is obvious.
            float displayRayLength = Math.Max(this.MaxDistanceM, 0.05f);
            Vector3 displayEnd = displayOrigin + (displayForward * displayRayLength);
            this.rayArrow.Point1 = new Point3D(displayOrigin.X, displayOrigin.Y, displayOrigin.Z);
            this.rayArrow.Point2 = new Point3D(displayEnd.X, displayEnd.Y, displayEnd.Z);
            this.originMarker.Transform = new TranslateTransform3D(displayOrigin.X, displayOrigin.Y, displayOrigin.Z);
            this.statusBillboard.Position = new Point3D(displayOrigin.X, displayOrigin.Y, displayOrigin.Z + 0.25);

            string nearestName = string.Empty;
            float nearestDist = float.PositiveInfinity;
            Vector3 nearestCenter = default;
            Vector3 nearestSize = default;

            if (this.boundsByName.Count > 0)
            {
                foreach (var pair in this.boundsByName)
                {
                    if (this.IgnoreZones && IsZoneName(pair.Key))
                    {
                        continue;
                    }

                    if (SphereRaycastAabb(origin, forward, pair.Value.Center, pair.Value.Size, this.RadiusM, this.MaxDistanceM, out float dist)
                        && dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestName = pair.Key;
                        nearestCenter = pair.Value.Center;
                        nearestSize = pair.Value.Size;
                    }
                }
            }

            this.SetHitLabel(nearestName);
            this.RaisePropertyChanged(nameof(this.BoundCount));

            if (!string.IsNullOrEmpty(nearestName))
            {
                Vector3 hitPoint = origin + (forward * nearestDist);
                Vector3 displayHit = this.ToDisplay(hitPoint);
                this.hitMarker.Transform = new TranslateTransform3D(displayHit.X, displayHit.Y, displayHit.Z);
                this.statusBillboard.Text = $"Gaze → {nearestName} ({nearestDist:0.00} m)";

                if (this.HighlightHits)
                {
                    Vector3 displayCenter = this.ToDisplay(nearestCenter);
                    Vector3 displaySize = this.ToDisplaySize(nearestSize);
                    var location = new Point3D(
                        displayCenter.X - (displaySize.X / 2.0),
                        displayCenter.Y - (displaySize.Y / 2.0),
                        displayCenter.Z - (displaySize.Z / 2.0));
                    Rect3D? rect = new Rect3D(location, new Size3D(
                        Math.Max(displaySize.X, 0.05),
                        Math.Max(displaySize.Y, 0.05),
                        Math.Max(displaySize.Z, 0.05)));
                    this.nearestHitBox.Color = this.HitColor;
                    this.nearestHitBox.SetCurrentValue(this.ChildMessage(rect));
                }
            }
            else
            {
                string boundInfo = this.boundsByName.Count == 0
                    ? "no ObjectBound loaded"
                    : (this.IgnoreZones ? "no non-zone hit" : "no hit");
                this.statusBillboard.Text = $"Gaze → ({boundInfo})";
            }
        }

        private Message<T>? ChildMessage<T>(T data)
        {
            DateTime t = this.HasCurrentValue ? this.CurrentOriginatingTime : this.lastEnvelopeTime;
            return new Message<T>(data, t, t, 0, 0);
        }

        private static bool IsZoneName(string name) =>
            !string.IsNullOrEmpty(name) && name.IndexOf("zone", StringComparison.OrdinalIgnoreCase) >= 0;

        private void SetHitLabel(string value)
        {
            string next = value ?? string.Empty;
            if (this.hitLabel == next)
            {
                return;
            }

            this.hitLabel = next;
            this.RaisePropertyChanged(nameof(this.HitLabel));
        }

        private void TryBindObjectBoundCompanion()
        {
            this.StopObjectBoundPreload();

            try
            {
                if (this.StreamBinding == null)
                {
                    return;
                }

                var session = VisualizationContext.Instance?.DatasetViewModel?.CurrentSessionViewModel;
                if (session == null)
                {
                    return;
                }

                StreamSource? source = null;

                // 1) Same partition as the gaze stream.
                source = session.CreateStreamSource(
                    new StreamBinding(ObjectBoundStreamName, this.StreamBinding.PartitionName),
                    null,
                    null);

                // 2) Same store path as the gaze StreamSource (partition display name may differ).
                if (source == null && this.StreamSource != null)
                {
                    string gazeStorePath = this.StreamSource.StorePath ?? string.Empty;
                    string gazeStoreName = this.StreamSource.StoreName ?? string.Empty;
                    foreach (var partition in session.PartitionViewModels)
                    {
                        bool sameStore =
                            (!string.IsNullOrEmpty(gazeStorePath)
                                && string.Equals(partition.StorePath, gazeStorePath, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrEmpty(gazeStoreName)
                                && string.Equals(partition.StoreName, gazeStoreName, StringComparison.OrdinalIgnoreCase));
                        if (!sameStore)
                        {
                            continue;
                        }

                        source = session.CreateStreamSource(new StreamBinding(ObjectBoundStreamName, partition.Name), null, null);
                        if (source != null)
                        {
                            break;
                        }
                    }
                }

                // 3) Any partition that has ObjectBound (Casper Server in combined .pds).
                if (source == null)
                {
                    foreach (var partition in session.PartitionViewModels)
                    {
                        source = session.CreateStreamSource(new StreamBinding(ObjectBoundStreamName, partition.Name), null, null);
                        if (source != null)
                        {
                            break;
                        }
                    }
                }

                if (source?.StreamMetadata == null)
                {
                    return;
                }

                ObjectBoundVisualizationObject.GetPreloadTimeRange(source.StreamMetadata, out DateTime start, out DateTime end);

                this.objectBoundsView = DataManager.Instance.ReadStream<ValueTuple<string, Vector3, Vector3>>(source, start, end);
                this.objectBoundsView.DetailedCollectionChanged += this.OnObjectBoundsCollectionChanged;
                this.RebuildBoundsFromView();
            }
            catch (Exception)
            {
                this.StopObjectBoundPreload();
            }
        }

        private void StopObjectBoundPreload()
        {
            if (this.objectBoundsView != null)
            {
                this.objectBoundsView.DetailedCollectionChanged -= this.OnObjectBoundsCollectionChanged;
                this.objectBoundsView = null;
            }

            this.boundsByName.Clear();
            this.RaisePropertyChanged(nameof(this.BoundCount));
        }

        private void OnObjectBoundsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                this.RebuildBoundsFromView();
            }
            else
            {
                dispatcher.Invoke(this.RebuildBoundsFromView);
            }
        }

        private void RebuildBoundsFromView()
        {
            var next = new Dictionary<string, (Vector3 Center, Vector3 Size)>(StringComparer.Ordinal);
            if (this.objectBoundsView != null)
            {
                foreach (var message in this.objectBoundsView)
                {
                    var data = message.Data;
                    if (!string.IsNullOrWhiteSpace(data.Item1))
                    {
                        next[data.Item1] = (data.Item2, data.Item3);
                    }
                }
            }

            this.boundsByName = next;
            this.RaisePropertyChanged(nameof(this.BoundCount));
            this.UpdateGazeVisuals();
            this.UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            bool isGazeStream = IsGazeHeadOrientationStream(this.SourceStreamName);
            // Billboard stays visible on wrong streams so the refuse message is readable.
            bool showStatus = this.Visible && (!isGazeStream || (this.HasCurrentValue && this.CurrentData != null));
            bool showRay = this.Visible && isGazeStream && this.HasCurrentValue && this.CurrentData != null;
            this.UpdateChildVisibility(this.rayArrow, showRay);
            this.UpdateChildVisibility(this.originMarker, showRay);
            this.UpdateChildVisibility(this.statusBillboard, showStatus);

            bool showHit = showRay && !string.IsNullOrEmpty(this.hitLabel);
            this.UpdateChildVisibility(this.hitMarker, showHit);
            this.UpdateChildVisibility(this.nearestHitBox.ModelVisual3D, showHit && this.HighlightHits);
        }

        private Vector3 ToDisplay(Vector3 v) =>
            this.ReverseYZ ? new Vector3(v.X, v.Z, v.Y) : v;

        private Vector3 ToDisplayDirection(Vector3 v) => this.ToDisplay(v);

        private Vector3 ToDisplaySize(Vector3 size) =>
            this.ReverseYZ
                ? new Vector3(Math.Abs(size.X), Math.Abs(size.Z), Math.Abs(size.Y))
                : new Vector3(Math.Abs(size.X), Math.Abs(size.Y), Math.Abs(size.Z));

        /// <summary>
        /// Unity Euler degrees → look direction for Meta/avatar head bone (local +Y).
        /// Matches CollaborationIndices HeadForwardAxis "+Y (avatar)" / q * Vector3.UnitY.
        /// </summary>
        /// <param name="eulerDeg">Euler degrees (pitch, yaw, roll).</param>
        /// <returns>Unit look vector in store/Unity space.</returns>
        internal static Vector3 EulerDegreesToForward(Vector3 eulerDeg)
        {
            const float Deg2Rad = (float)(Math.PI / 180.0);
            System.Numerics.Quaternion q = System.Numerics.Quaternion.CreateFromYawPitchRoll(
                eulerDeg.Y * Deg2Rad,
                eulerDeg.X * Deg2Rad,
                eulerDeg.Z * Deg2Rad);
            Vector3 f = Vector3.Transform(Vector3.UnitY, q);
            float len = f.Length();
            return len < 1e-8f ? Vector3.UnitY : f / len;
        }

        /// <summary>
        /// Sphere-inflated AABB raycast (story 4.2 / Spike42).
        /// </summary>
        /// <param name="origin">Ray origin (store space).</param>
        /// <param name="dir">Unit direction (store space).</param>
        /// <param name="center">AABB center.</param>
        /// <param name="size">AABB size.</param>
        /// <param name="radius">Sphere inflate.</param>
        /// <param name="maxDistance">Max distance.</param>
        /// <param name="hitDistance">Hit distance when true.</param>
        /// <returns>True when the ray hits the inflated AABB within maxDistance.</returns>
        internal static bool SphereRaycastAabb(
            Vector3 origin,
            Vector3 dir,
            Vector3 center,
            Vector3 size,
            float radius,
            float maxDistance,
            out float hitDistance)
        {
            hitDistance = float.PositiveInfinity;
            Vector3 half = size * 0.5f;
            Vector3 min = center - half - new Vector3(radius, radius, radius);
            Vector3 max = center + half + new Vector3(radius, radius, radius);

            if (origin.X >= min.X && origin.X <= max.X
                && origin.Y >= min.Y && origin.Y <= max.Y
                && origin.Z >= min.Z && origin.Z <= max.Z
                && maxDistance >= 0f)
            {
                hitDistance = 0f;
                return true;
            }

            float tMin = 0f;
            float tMax = maxDistance;
            if (!Slab(origin.X, dir.X, min.X, max.X, ref tMin, ref tMax)
                || !Slab(origin.Y, dir.Y, min.Y, max.Y, ref tMin, ref tMax)
                || !Slab(origin.Z, dir.Z, min.Z, max.Z, ref tMin, ref tMax))
            {
                return false;
            }

            if (tMax < 0f || tMin > maxDistance)
            {
                return false;
            }

            hitDistance = tMin >= 0f ? tMin : tMax;
            return hitDistance >= 0f && hitDistance <= maxDistance;
        }

        private static bool Slab(float o, float d, float min, float max, ref float tMin, ref float tMax)
        {
            const float eps = 1e-8f;
            if (Math.Abs(d) < eps)
            {
                return o >= min && o <= max;
            }

            float inv = 1f / d;
            float t1 = (min - o) * inv;
            float t2 = (max - o) * inv;
            if (t1 > t2)
            {
                float tmp = t1;
                t1 = t2;
                t2 = tmp;
            }

            if (t1 > tMin)
            {
                tMin = t1;
            }

            if (t2 < tMax)
            {
                tMax = t2;
            }

            return tMin <= tMax;
        }
    }
}
