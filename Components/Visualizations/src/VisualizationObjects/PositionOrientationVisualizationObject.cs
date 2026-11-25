// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SAAC.Visualizations
{
    using System;
    using System.ComponentModel;
    using System.Runtime.Serialization;
    using System.Windows.Media;
    using System.Windows.Media.Media3D;
    using HelixToolkit.Wpf;
    using Microsoft.Psi.Visualization.VisualizationObjects;
    using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;
    using Win3D = System.Windows.Media.Media3D;

    /// <summary>
    /// Position + Euler orientation gizmo for Casper streams such as
    /// <c>1-Head</c>, <c>1-LeftWrist</c>, <c>1-RightWrist</c> (and person-2 twins).
    /// Draws a tunable sphere marker, optional RGB axes, and a stream-name billboard.
    /// </summary>
    [VisualizationObject("Position Orientation")]
    public class PositionOrientationVisualizationObject : ModelVisual3DValueVisualizationObject<Tuple<System.Numerics.Vector3, System.Numerics.Vector3>>
    {
        private readonly SphereVisual3D marker;

        private double billboardHeightCm = 15;
        private double markerSizeM = 0.08;
        private double axesLengthM = 0.25;
        private bool showAxes = true;
        private bool showMarker = true;
        private bool showLabel = true;
        private Color markerColor = Colors.DeepSkyBlue;

        /// <summary>
        /// Initializes a new instance of the <see cref="PositionOrientationVisualizationObject"/> class.
        /// </summary>
        public PositionOrientationVisualizationObject()
        {
            this.System = new CoordinateSystemVisualizationObject
            {
                Length = this.axesLengthM,
                Thickness = Math.Max(0.01, this.axesLengthM * 0.08),
            };
            this.System.RegisterChildPropertyChangedNotifications(this, nameof(this.System));

            this.Billboard = new BillboardTextVisualizationObject() { Visible = true };
            this.Billboard.RegisterChildPropertyChangedNotifications(this, nameof(this.Billboard));

            this.marker = new SphereVisual3D
            {
                Radius = this.markerSizeM / 2.0,
                Fill = new SolidColorBrush(this.markerColor),
                PhiDiv = 12,
                ThetaDiv = 12,
            };

            this.UpdateVisibility();
        }

        /// <summary>
        /// Gets or sets the diameter of the position sphere gizmo (meters).
        /// </summary>
        [DataMember]
        [PropertyOrder(0)]
        [DisplayName("Gizmo Size (m)")]
        [Description("Diameter of the Head/Wrist sphere marker in meters. Tune up if landmarks are hard to see.")]
        public double GizmoSizeM
        {
            get { return this.markerSizeM; }
            set { this.Set(nameof(this.GizmoSizeM), ref this.markerSizeM, Math.Max(0.01, value)); }
        }

        /// <summary>
        /// Gets or sets RGB axes length (meters). Also updates nested CoordinateSystem.Length.
        /// </summary>
        [DataMember]
        [PropertyOrder(1)]
        [DisplayName("Axes Length (m)")]
        [Description("Length of the XYZ orientation axes in meters.")]
        public double AxesLengthM
        {
            get { return this.axesLengthM; }
            set { this.Set(nameof(this.AxesLengthM), ref this.axesLengthM, Math.Max(0.02, value)); }
        }

        /// <summary>
        /// Gets or sets the sphere marker color.
        /// </summary>
        [DataMember]
        [PropertyOrder(2)]
        [DisplayName("Gizmo Color")]
        [Description("Color of the position sphere (auto-tinted from stream name on bind: Head / Wrist / person).")]
        public Color GizmoColor
        {
            get { return this.markerColor; }
            set { this.Set(nameof(this.GizmoColor), ref this.markerColor, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the sphere marker is drawn.
        /// </summary>
        [DataMember]
        [PropertyOrder(3)]
        [DisplayName("Show Gizmo")]
        [Description("Draw the sphere at the stream position.")]
        public bool ShowGizmo
        {
            get { return this.showMarker; }
            set { this.Set(nameof(this.ShowGizmo), ref this.showMarker, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether RGB axes are drawn.
        /// </summary>
        [DataMember]
        [PropertyOrder(4)]
        [DisplayName("Show Axes")]
        [Description("Draw RGB orientation axes at the stream pose.")]
        public bool ShowAxes
        {
            get { return this.showAxes; }
            set { this.Set(nameof(this.ShowAxes), ref this.showAxes, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the stream-name billboard is drawn.
        /// </summary>
        [DataMember]
        [PropertyOrder(5)]
        [DisplayName("Show Label")]
        [Description("Draw the stream name above the gizmo.")]
        public bool ShowLabel
        {
            get { return this.showLabel; }
            set { this.Set(nameof(this.ShowLabel), ref this.showLabel, value); }
        }

        /// <summary>
        /// Gets or sets the height at which to draw the billboard (cm).
        /// </summary>
        [DataMember]
        [PropertyOrder(6)]
        [DisplayName("Billboard Height (cm)")]
        [Description("Height above the landmark at which to draw the label (cm).")]
        public double BillboardHeightCm
        {
            get { return this.billboardHeightCm; }
            set { this.Set(nameof(this.BillboardHeightCm), ref this.billboardHeightCm, value); }
        }

        /// <summary>
        /// Gets or sets the billboard visualization object.
        /// </summary>
        [ExpandableObject]
        [DataMember]
        [PropertyOrder(7)]
        [DisplayName("Billboard")]
        [Description("The billboard properties.")]
        public BillboardTextVisualizationObject Billboard { get; set; }

        /// <summary>
        /// Gets or sets the coordinates system visualization object .
        /// </summary>
        [ExpandableObject]
        [DataMember]
        [PropertyOrder(8)]
        [DisplayName("CoordinateSystem")]
        [Description("The representation object.")]
        public CoordinateSystemVisualizationObject System { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether reversing Y and Z axes.
        /// </summary>
        [DataMember]
        [PropertyOrder(9)]
        [DisplayName("ReverseYZ")]
        [Description("Reverse Y & Z axes (default on for Unity Casper stores).")]
        public bool ReverseYZ { get; set; } = true;

        /// <inheritdoc/>
        protected override void OnStreamBound()
        {
            base.OnStreamBound();
            this.ApplyColorsForStreamName(this.SourceStreamName);
            this.UpdateVisibility();
        }

        /// <inheritdoc/>
        public override void UpdateVisual3D()
        {
            if (this.CurrentData != null)
            {
                this.UpdateVisuals();
            }

            this.UpdateVisibility();
        }

        /// <inheritdoc/>
        public override void NotifyPropertyChanged(string propertyName)
        {
            if (propertyName == nameof(this.GizmoSizeM))
            {
                this.marker.Radius = this.GizmoSizeM / 2.0;
                this.UpdateVisibility();
            }
            else if (propertyName == nameof(this.AxesLengthM))
            {
                this.System.Length = this.AxesLengthM;
                this.System.Thickness = Math.Max(0.01, this.AxesLengthM * 0.08);
                this.UpdateSystem();
            }
            else if (propertyName == nameof(this.GizmoColor))
            {
                this.marker.Fill = new SolidColorBrush(this.GizmoColor);
            }
            else if (propertyName == nameof(this.BillboardHeightCm) || propertyName == nameof(this.Name))
            {
                this.UpdateBillboard();
            }
            else if (propertyName == nameof(this.ReverseYZ))
            {
                this.UpdateVisuals();
                this.UpdateVisibility();
            }
            else if (propertyName == nameof(this.ShowGizmo)
                || propertyName == nameof(this.ShowAxes)
                || propertyName == nameof(this.ShowLabel)
                || propertyName == nameof(this.Visible))
            {
                this.UpdateVisibility();
            }
        }

        // The text of the billboard: the name of the visualization object, which PsiStudio sets to
        // the name of the stream and which its properties change; the name of the stream when it is empty.
        private string BillboardText => string.IsNullOrWhiteSpace(this.Name) ? this.SourceStreamName : this.Name;

        private void ApplyColorsForStreamName(string streamName)
        {
            if (string.IsNullOrEmpty(streamName))
            {
                return;
            }

            // Person tint: 2-* darker / warmer; landmark kind picks the base hue.
            bool person2 = streamName.StartsWith("2-", StringComparison.OrdinalIgnoreCase);
            if (streamName.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0
                && streamName.IndexOf("Gaze", StringComparison.OrdinalIgnoreCase) < 0)
            {
                this.GizmoColor = person2 ? Colors.OrangeRed : Colors.Gold;
            }
            else if (streamName.IndexOf("LeftWrist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                this.GizmoColor = person2 ? Colors.MediumVioletRed : Colors.LimeGreen;
            }
            else if (streamName.IndexOf("RightWrist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                this.GizmoColor = person2 ? Colors.DeepPink : Colors.DodgerBlue;
            }
            else if (streamName.IndexOf("Wrist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                this.GizmoColor = person2 ? Colors.Orange : Colors.Cyan;
            }
            else
            {
                this.GizmoColor = person2 ? Colors.Orange : Colors.DeepSkyBlue;
            }
        }

        private void UpdateVisuals()
        {
            this.UpdateMarker();
            this.UpdateSystem();
            this.UpdateBillboard();
        }

        private void UpdateMarker()
        {
            if (this.CurrentData == null)
            {
                return;
            }

            var origin = this.CurrentData.Item1;
            double x = origin.X;
            double y = this.ReverseYZ ? origin.Z : origin.Y;
            double z = this.ReverseYZ ? origin.Y : origin.Z;
            this.marker.Radius = this.GizmoSizeM / 2.0;
            this.marker.Transform = new TranslateTransform3D(x, y, z);
        }

        private void UpdateSystem()
        {
            if (this.CurrentData != null)
            {
                MathNet.Spatial.Euclidean.Point3D origin = new MathNet.Spatial.Euclidean.Point3D(
                    this.CurrentData.Item1.X,
                    this.ReverseYZ ? this.CurrentData.Item1.Z : this.CurrentData.Item1.Y,
                    this.ReverseYZ ? this.CurrentData.Item1.Y : this.CurrentData.Item1.Z);
                MathNet.Spatial.Euclidean.CoordinateSystem rot = MathNet.Spatial.Euclidean.CoordinateSystem.Rotation(
                    MathNet.Spatial.Units.Angle.FromDegrees(this.CurrentData.Item2.X),
                    MathNet.Spatial.Units.Angle.FromDegrees(this.ReverseYZ ? this.CurrentData.Item2.Z : this.CurrentData.Item2.Y),
                    MathNet.Spatial.Units.Angle.FromDegrees(this.ReverseYZ ? this.CurrentData.Item2.Y : this.CurrentData.Item2.Z));
                MathNet.Spatial.Euclidean.CoordinateSystem newValue = new MathNet.Spatial.Euclidean.CoordinateSystem(
                    origin, rot.XAxis, rot.YAxis, rot.ZAxis);
                this.System.Length = this.AxesLengthM;
                this.System.SetCurrentValue(this.SynthesizeMessage(newValue));
            }
        }

        private void UpdateBillboard()
        {
            if (this.CurrentData != null)
            {
                var origin = this.CurrentData.Item1;
                var pos = new Win3D.Point3D(
                    origin.X,
                    this.ReverseYZ ? origin.Z : origin.Y,
                    (this.ReverseYZ ? origin.Y : origin.Z) + (this.BillboardHeightCm / 100.0));
                this.Billboard.Visible = this.ShowLabel;
                this.Billboard.SetCurrentValue(this.SynthesizeMessage(Tuple.Create(pos, this.BillboardText)));
            }
        }

        private void UpdateVisibility()
        {
            bool hasData = this.Visible && this.CurrentData != null;

            this.UpdateChildVisibility(this.marker, hasData && this.ShowGizmo);
            this.UpdateChildVisibility(this.System.ModelVisual3D, hasData && this.ShowAxes);
            this.UpdateChildVisibility(this.Billboard.ModelVisual3D, hasData && this.ShowLabel);
        }
    }
}
