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
    using Microsoft.Psi;
    using Microsoft.Psi.Visualization;
    using Microsoft.Psi.Visualization.Data;
    using Microsoft.Psi.Visualization.VisualizationObjects;
    using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

    /// <summary>
    /// 3D visualization of an <c>ObjectBound</c> stream: preloads every message on bind
    /// and draws a labeled AABB for each distinct object name (latest bounds wins).
    /// Stream payload is <c>(string name, Vector3 center, Vector3 size)</c> with corners = center ± size/2.
    /// </summary>
    [VisualizationObject("Object Bounds")]
    public class ObjectBoundVisualizationObject : ModelVisual3DValueVisualizationObject<ValueTuple<string, Vector3, Vector3>>
    {
        private readonly Dictionary<string, BoundVisual> visuals = new Dictionary<string, BoundVisual>(StringComparer.Ordinal);
        private Dictionary<string, (Vector3 Center, Vector3 Size)> boundsByName =
            new Dictionary<string, (Vector3 Center, Vector3 Size)>(StringComparer.Ordinal);

        private ObservableKeyedCache<DateTime, Message<ValueTuple<string, Vector3, Vector3>>>.ObservableKeyedView? allBoundsView;
        private Color color = Colors.DeepSkyBlue;
        private double opacity = 80;
        private double thicknessMm = 15;
        private double billboardHeightCm = 20;
        private double zoneOpacity = 18;
        private double zoneLabelOpacity = 28;
        private double zoneThicknessMm = 7;
        private bool reverseYZ = true;
        private DateTime lastEnvelopeTime = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectBoundVisualizationObject"/> class.
        /// </summary>
        public ObjectBoundVisualizationObject()
        {
            this.UpdateVisibility();
        }

        /// <summary>
        /// Gets or sets the box edge color.
        /// </summary>
        [DataMember]
        [PropertyOrder(1)]
        [Description("The color of the bound boxes.")]
        public Color Color
        {
            get { return this.color; }
            set { this.Set(nameof(this.Color), ref this.color, value); }
        }

        /// <summary>
        /// Gets or sets the box edge opacity (0–100).
        /// </summary>
        [DataMember]
        [PropertyOrder(2)]
        [Description("The opacity of the bound boxes (0–100).")]
        public double Opacity
        {
            get { return this.opacity; }
            set { this.Set(nameof(this.Opacity), ref this.opacity, value); }
        }

        /// <summary>
        /// Gets or sets the box edge thickness in millimeters.
        /// </summary>
        [DataMember]
        [PropertyOrder(3)]
        [DisplayName("Thickness (mm)")]
        [Description("The thickness of the bound box edges in millimeters.")]
        public double ThicknessMm
        {
            get { return this.thicknessMm; }
            set { this.Set(nameof(this.ThicknessMm), ref this.thicknessMm, value); }
        }

        /// <summary>
        /// Gets or sets the height above the box top at which to draw the label (cm).
        /// </summary>
        [DataMember]
        [PropertyOrder(4)]
        [DisplayName("Billboard Height (cm)")]
        [Description("Height above the top of each bound box at which to draw the name label (cm).")]
        public double BillboardHeightCm
        {
            get { return this.billboardHeightCm; }
            set { this.Set(nameof(this.BillboardHeightCm), ref this.billboardHeightCm, value); }
        }

        /// <summary>
        /// Gets or sets opacity for bounds whose name contains "zone" (0–100).
        /// </summary>
        [DataMember]
        [PropertyOrder(5)]
        [DisplayName("Zone Opacity")]
        [Description("Opacity for ObjectBound names containing \"zone\" (0–100). Other bounds use Opacity.")]
        public double ZoneOpacity
        {
            get { return this.zoneOpacity; }
            set { this.Set(nameof(this.ZoneOpacity), ref this.zoneOpacity, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether Y and Z axes are swapped (Unity → PsiStudio).
        /// </summary>
        [DataMember]
        [PropertyOrder(6)]
        [DisplayName("ReverseYZ")]
        [Description("Reverse Y & Z axes (default on for Unity Casper stores).")]
        public bool ReverseYZ
        {
            get { return this.reverseYZ; }
            set { this.Set(nameof(this.ReverseYZ), ref this.reverseYZ, value); }
        }

        /// <summary>
        /// Gets how many distinct ObjectBound names are currently loaded.
        /// </summary>
        [PropertyOrder(7)]
        [DisplayName("Bound Count")]
        [Description("Distinct ObjectBound names preloaded from the store.")]
        public int BoundCount => this.boundsByName.Count;

        /// <summary>
        /// Copies the latest preloaded bounds (name → center, size) for companion visualizers.
        /// </summary>
        /// <returns>A snapshot dictionary (never null).</returns>
        public Dictionary<string, (Vector3 Center, Vector3 Size)> CopyBoundsSnapshot() =>
            new Dictionary<string, (Vector3 Center, Vector3 Size)>(this.boundsByName, StringComparer.Ordinal);

        /// <inheritdoc/>
        public override void UpdateVisual3D()
        {
            // Cursor value still merges into the scene (live / late messages), but the
            // primary content comes from the full-stream preload in OnStreamBound.
            if (this.HasCurrentValue)
            {
                var data = this.CurrentData;
                if (!string.IsNullOrWhiteSpace(data.Item1))
                {
                    this.boundsByName[data.Item1] = (data.Item2, data.Item3);
                    this.lastEnvelopeTime = this.CurrentOriginatingTime;
                }
            }

            // Layout restore: metadata First/Last can be default on first bind — retry once data path is live.
            if (this.boundsByName.Count == 0 && this.StreamSource != null)
            {
                this.StartFullStreamPreload();
            }

            this.UpdateAllVisuals();
            this.UpdateVisibility();
            this.RaisePropertyChanged(nameof(this.BoundCount));
        }

        /// <inheritdoc/>
        public override void NotifyPropertyChanged(string propertyName)
        {
            if (propertyName == nameof(this.Color) ||
                propertyName == nameof(this.Opacity) ||
                propertyName == nameof(this.ThicknessMm) ||
                propertyName == nameof(this.ZoneOpacity))
            {
                this.ApplySharedStyle();
            }
            else if (propertyName == nameof(this.BillboardHeightCm) ||
                     propertyName == nameof(this.ReverseYZ))
            {
                this.UpdateAllVisuals();
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
            this.StartFullStreamPreload();
            this.SchedulePreloadRetry();
        }

        /// <inheritdoc/>
        protected override void OnStreamUnbound()
        {
            this.StopFullStreamPreload();
            this.boundsByName.Clear();
            this.ClearVisuals();
            base.OnStreamUnbound();
        }

        private void SchedulePreloadRetry()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            // Layout reopen: stream metadata / DataManager may not be ready in OnStreamBound.
            dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    if (this.StreamSource != null && this.boundsByName.Count == 0)
                    {
                        this.StartFullStreamPreload();
                    }
                }));
        }

        private void StartFullStreamPreload()
        {
            this.StopFullStreamPreload();

            if (this.StreamSource?.StreamMetadata == null)
            {
                return;
            }

            // Prefer catalog First/Last when valid; otherwise full range.
            // On PsiStudio layout restore First/Last are often still default → empty window → no boxes.
            GetPreloadTimeRange(this.StreamSource.StreamMetadata, out DateTime start, out DateTime end);

            this.allBoundsView = DataManager.Instance.ReadStream<ValueTuple<string, Vector3, Vector3>>(
                this.StreamSource,
                start,
                end);
            this.allBoundsView.DetailedCollectionChanged += this.OnAllBoundsCollectionChanged;

            // Apply anything already present; remaining messages arrive asynchronously.
            this.RebuildBoundsFromView();
        }

        /// <summary>
        /// Fixed ReadStream end is exclusive. Use a real OT window when metadata is populated; else [Min, Max].
        /// </summary>
        internal static void GetPreloadTimeRange(Microsoft.Psi.IStreamMetadata meta, out DateTime start, out DateTime end)
        {
            start = meta.FirstMessageOriginatingTime;
            end = meta.LastMessageOriginatingTime;

            bool valid =
                start > DateTime.MinValue
                && end > DateTime.MinValue
                && end >= start;

            if (!valid)
            {
                start = DateTime.MinValue;
                end = DateTime.MaxValue;
                return;
            }

            if (end < DateTime.MaxValue)
            {
                end = end.AddTicks(1);
            }
        }

        private void StopFullStreamPreload()
        {
            if (this.allBoundsView != null)
            {
                this.allBoundsView.DetailedCollectionChanged -= this.OnAllBoundsCollectionChanged;
                this.allBoundsView = null;
            }
        }

        private void OnAllBoundsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                this.RebuildBoundsFromView();
                return;
            }

            if (dispatcher.CheckAccess())
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
            if (this.allBoundsView != null)
            {
                foreach (var message in this.allBoundsView)
                {
                    var data = message.Data;
                    if (string.IsNullOrWhiteSpace(data.Item1))
                    {
                        continue;
                    }

                    next[data.Item1] = (data.Item2, data.Item3);
                    this.lastEnvelopeTime = message.OriginatingTime;
                }
            }

            this.boundsByName = next;
            this.UpdateAllVisuals();
            this.UpdateVisibility();
            this.RaisePropertyChanged(nameof(this.BoundCount));
        }

        private void UpdateAllVisuals()
        {
            var liveNames = new HashSet<string>(this.boundsByName.Keys, StringComparer.Ordinal);

            foreach (var name in this.visuals.Keys.Where(n => !liveNames.Contains(n)).ToList())
            {
                this.RemoveVisual(name);
            }

            foreach (var pair in this.boundsByName)
            {
                this.UpdateOneVisual(pair.Key, pair.Value.Center, pair.Value.Size);
            }
        }

        private void UpdateOneVisual(string name, Vector3 center, Vector3 size)
        {
            if (!this.visuals.TryGetValue(name, out BoundVisual visual))
            {
                visual = new BoundVisual(
                    new Rect3DVisualizationObject(),
                    new BillboardTextVisualizationObject() { Visible = true });
                this.visuals[name] = visual;
            }

            this.ApplyStyleTo(visual, name);

            Vector3 displayCenter = this.ToDisplay(center);
            Vector3 displaySize = this.ToDisplaySize(size);

            // Rect3D Location is the min corner; ObjectBound is center + size.
            var location = new Point3D(
                displayCenter.X - (displaySize.X / 2.0),
                displayCenter.Y - (displaySize.Y / 2.0),
                displayCenter.Z - (displaySize.Z / 2.0));
            Rect3D? rect = new Rect3D(location, new Size3D(displaySize.X, displaySize.Y, displaySize.Z));
            visual.Volume.SetCurrentValue(this.ChildMessage(rect));

            var labelPos = new Point3D(
                displayCenter.X,
                displayCenter.Y,
                displayCenter.Z + (displaySize.Z / 2.0) + (this.BillboardHeightCm / 100.0));
            visual.Billboard.SetCurrentValue(this.ChildMessage(Tuple.Create(labelPos, name)));

            bool show = this.Visible;
            this.UpdateChildVisibility(visual.Volume.ModelVisual3D, show);
            this.UpdateChildVisibility(visual.Billboard.ModelVisual3D, show);
        }

        private static bool IsZoneName(string name) =>
            !string.IsNullOrEmpty(name) && name.IndexOf("zone", StringComparison.OrdinalIgnoreCase) >= 0;

        private Vector3 ToDisplay(Vector3 v) =>
            this.ReverseYZ ? new Vector3(v.X, v.Z, v.Y) : v;

        private Vector3 ToDisplaySize(Vector3 size) =>
            this.ReverseYZ ? new Vector3(Math.Abs(size.X), Math.Abs(size.Z), Math.Abs(size.Y)) : new Vector3(Math.Abs(size.X), Math.Abs(size.Y), Math.Abs(size.Z));

        private Message<T>? ChildMessage<T>(T data)
        {
            DateTime t = this.HasCurrentValue ? this.CurrentOriginatingTime : this.lastEnvelopeTime;
            return new Message<T>(data, t, t, 0, 0);
        }

        private void ApplySharedStyle()
        {
            foreach (var pair in this.visuals)
            {
                this.ApplyStyleTo(pair.Value, pair.Key);
            }
        }

        private void ApplyStyleTo(BoundVisual visual, string name)
        {
            bool zone = IsZoneName(name);
            visual.Volume.Color = this.Color;
            visual.Volume.Opacity = zone ? this.ZoneOpacity : this.Opacity;
            visual.Volume.ThicknessMm = zone ? this.zoneThicknessMm : this.ThicknessMm;

            if (zone)
            {
                visual.Billboard.BillboardOpacity = (int)Math.Max(5, Math.Min(100, this.zoneLabelOpacity));
                visual.Billboard.BillboardBackgroundColor = Color.FromArgb(70, 48, 52, 56);
                visual.Billboard.BillboardForegroundColor = Color.FromArgb(160, 200, 210, 216);
                visual.Billboard.BillboardFontSize = 8;
                visual.Billboard.BillboardBorderThickness = 0;
            }
            else
            {
                visual.Billboard.BillboardOpacity = 100;
                visual.Billboard.BillboardBackgroundColor = Colors.Gray;
                visual.Billboard.BillboardForegroundColor = Colors.White;
                visual.Billboard.BillboardFontSize = 10;
                visual.Billboard.BillboardBorderThickness = 1;
            }
        }

        private void UpdateVisibility()
        {
            bool show = this.Visible && this.boundsByName.Count > 0;
            foreach (var visual in this.visuals.Values)
            {
                this.UpdateChildVisibility(visual.Volume.ModelVisual3D, show);
                this.UpdateChildVisibility(visual.Billboard.ModelVisual3D, show);
            }
        }

        private void ClearVisuals()
        {
            foreach (var name in this.visuals.Keys.ToList())
            {
                this.RemoveVisual(name);
            }
        }

        private void RemoveVisual(string name)
        {
            if (!this.visuals.TryGetValue(name, out BoundVisual visual))
            {
                return;
            }

            this.UpdateChildVisibility(visual.Volume.ModelVisual3D, false);
            this.UpdateChildVisibility(visual.Billboard.ModelVisual3D, false);
            this.visuals.Remove(name);
        }

        private sealed class BoundVisual
        {
            public BoundVisual(Rect3DVisualizationObject volume, BillboardTextVisualizationObject billboard)
            {
                this.Volume = volume;
                this.Billboard = billboard;
            }

            public Rect3DVisualizationObject Volume { get; }

            public BillboardTextVisualizationObject Billboard { get; }
        }
    }
}
