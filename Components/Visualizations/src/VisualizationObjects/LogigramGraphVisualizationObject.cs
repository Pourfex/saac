// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SAAC.Visualizations
{
    using System;
    using System.ComponentModel;
    using System.Runtime.Serialization;
    using System.Text.RegularExpressions;
    using System.Windows;
    using Microsoft.Psi.Visualization;
    using Microsoft.Psi.Visualization.Data;
    using Microsoft.Psi.Visualization.Helpers;
    using Microsoft.Psi.Visualization.Navigation;
    using Microsoft.Psi.Visualization.VisualizationObjects;
    using Microsoft.Psi.Visualization.VisualizationPanels;
    using SAAC.Logigram1;
    using SAAC.Visualizations.Views;

    /// <summary>
    /// Canvas Gamma-path (1→2→3→C) Logigram graph at the playhead for one Battery under view.
    /// Primary bind is <see cref="LogigramState"/>; <see cref="LogigramOutcome"/> is a paired cursor value
    /// (auto-subscribed from the same partition when present) that lights terminal C for Gamma only
    /// when Outcome.BatteryId matches <see cref="BatteryId"/>.
    /// CURSOR_ONLY / PARSE_STATUS — never invents regulation or accumulates prior nodes.
    /// </summary>
    [VisualizationObject("Logigram Graph")]
    [VisualizationPanelType(VisualizationPanelType.Canvas)]
    public class LogigramGraphVisualizationObject : StreamValueVisualizationObject<LogigramState>
    {
        /// <summary>
        /// Honest label when the cursor has no State message (unbound, gap, or empty store).
        /// </summary>
        public const string EmptyDisplayText = "no graph state";

        /// <summary>
        /// Label when the cursor State belongs to a different Battery than <see cref="BatteryId"/>.
        /// </summary>
        public const string OtherBatteryDisplayTextPrefix = "other Battery (under view BatteryId=";

        /// <summary>
        /// Static Gamma-path skeleton node numbers (GRAPH_SCOPE = GAMMA_PATH). Easy to extend later.
        /// </summary>
        public static readonly int[] GammaPathNodeNumbers = { 1, 2, 3 };

        /// <summary>Terminal C label on the Gamma path.</summary>
        public const string TerminalCLabel = "C";

        private static readonly Regex NodeStatusPrefix = new Regex(@"^node-(\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private int batteryId = 1;
        private LogigramState? heldStateForBattery;
        private DateTime heldStateOriginatingTime = DateTime.MinValue;
        private LogigramOutcome? heldOutcomeForBattery;
        private DateTime heldOutcomeOriginatingTime = DateTime.MinValue;
        private Guid outcomeSubscriberId = Guid.Empty;
        private StreamSource? outcomeStreamSource;

        /// <summary>
        /// Gets or sets the Battery under view. Only State/Outcome for this BatteryId drive highlights.
        /// Add one Logigram Graph panel per Battery and set this property on each.
        /// While scrubbing, holds the last matching message for this Battery whose originating time
        /// is still at or before the playhead (other-Battery cursor values are ignored). Scrubbing
        /// earlier than a held message clears it so the panel stays tied to the timeline.
        /// </summary>
        [DataMember]
        [DisplayName("BatteryId")]
        [Description("Battery under view for this panel. Other Batteries’ cursor messages are ignored.")]
        public int BatteryId
        {
            get => this.batteryId;
            set
            {
                if (this.batteryId == value)
                {
                    return;
                }

                this.Set(nameof(this.BatteryId), ref this.batteryId, value);
                this.ClearHeldState();
                this.ClearHeldOutcome();
                this.ApplyCursorState();
                this.RaiseGraphProperties();
                this.RaisePropertyChanged(nameof(this.IsTerminalCHighlighted));
            }
        }

        /// <summary>
        /// Gets the status label for the held under-view Battery State (or honest empty).
        /// </summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public string DisplayText => FormatDisplayText(this.heldStateForBattery, this.batteryId);

        /// <summary>
        /// Gets the held Outcome for the under-view Battery (null when none yet). Used for terminal C only.
        /// </summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public LogigramOutcome? CurrentOutcome => this.heldOutcomeForBattery;

        /// <summary>Gets a value indicating whether Gamma-path node 1 is highlighted at the cursor.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsNode1Highlighted => IsPathNodeHighlighted(1, this.heldStateForBattery, this.batteryId);

        /// <summary>Gets a value indicating whether Gamma-path node 2 is highlighted at the cursor.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsNode2Highlighted => IsPathNodeHighlighted(2, this.heldStateForBattery, this.batteryId);

        /// <summary>Gets a value indicating whether Gamma-path node 3 is highlighted at the cursor.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsNode3Highlighted => IsPathNodeHighlighted(3, this.heldStateForBattery, this.batteryId);

        /// <summary>
        /// Gets a value indicating whether terminal C is highlighted from held Outcome (Gamma only; never invented from State).
        /// </summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsTerminalCHighlighted => IsTerminalCHighlightedFromOutcome(
            this.heldOutcomeForBattery,
            this.batteryId);

        /// <inheritdoc />
        [Browsable(false)]
        [IgnoreDataMember]
        public override DataTemplate DefaultViewTemplate =>
            XamlHelper.CreateTemplate(this.GetType(), typeof(LogigramGraphVisualizationObjectView));

        /// <summary>
        /// Formats the status label for the Battery under view. Null → honest empty — never a fabricated regulation.
        /// Always prefixes the under-view BatteryId so each panel is identifiable.
        /// </summary>
        /// <param name="state">Held State for the under-view Battery, or null.</param>
        /// <param name="underViewBatteryId">Battery this panel is watching.</param>
        /// <returns>Label including under-view BatteryId.</returns>
        public static string FormatDisplayText(LogigramState? state, int underViewBatteryId)
        {
            string prefix = $"Battery {underViewBatteryId} | ";
            if (state == null || state.BatteryId != underViewBatteryId)
            {
                return prefix + EmptyDisplayText;
            }

            string status = state.Status ?? string.Empty;
            return $"{prefix}{status}";
        }

        /// <summary>
        /// Timeline-aware hold: accept a matching Battery cursor message; keep the previous hold only while
        /// the playhead is still at or after that message’s originating time; otherwise clear.
        /// </summary>
        /// <param name="cursor">State at the stream cursor, or null.</param>
        /// <param name="hasCursorValue">Whether a stream value is available at the cursor.</param>
        /// <param name="cursorMessageTime">Originating time of <paramref name="cursor"/> when available.</param>
        /// <param name="playhead">Navigator playhead time.</param>
        /// <param name="underViewBatteryId">Battery this panel is watching.</param>
        /// <param name="previousHeld">Previously held State.</param>
        /// <param name="previousHeldTime">Originating time of <paramref name="previousHeld"/>.</param>
        /// <param name="heldTime">Receives the originating time of the returned hold (or MinValue when cleared).</param>
        /// <returns>Updated held State, or null when cleared.</returns>
        public static LogigramState? HoldStateForBattery(
            LogigramState? cursor,
            bool hasCursorValue,
            DateTime cursorMessageTime,
            DateTime playhead,
            int underViewBatteryId,
            LogigramState? previousHeld,
            DateTime previousHeldTime,
            out DateTime heldTime)
        {
            if (hasCursorValue && cursor != null && cursor.BatteryId == underViewBatteryId)
            {
                heldTime = cursorMessageTime;
                return new LogigramState
                {
                    Status = cursor.Status ?? string.Empty,
                    BatteryId = cursor.BatteryId,
                };
            }

            // Gap / other Battery: keep last match only if playhead has not rewound before it.
            if (previousHeld != null && playhead >= previousHeldTime)
            {
                heldTime = previousHeldTime;
                return previousHeld;
            }

            heldTime = DateTime.MinValue;
            return null;
        }

        /// <summary>
        /// Timeline-aware Outcome hold (same rules as <see cref="HoldStateForBattery"/>).
        /// </summary>
        public static LogigramOutcome? HoldOutcomeForBattery(
            LogigramOutcome? cursor,
            bool dataAvailable,
            DateTime cursorMessageTime,
            DateTime playhead,
            int underViewBatteryId,
            LogigramOutcome? previousHeld,
            DateTime previousHeldTime,
            out DateTime heldTime)
        {
            if (dataAvailable && cursor != null && cursor.BatteryId == underViewBatteryId)
            {
                heldTime = cursorMessageTime;
                return new LogigramOutcome
                {
                    Regulation = cursor.Regulation,
                    BatteryId = cursor.BatteryId,
                };
            }

            if (previousHeld != null && playhead >= previousHeldTime)
            {
                heldTime = previousHeldTime;
                return previousHeld;
            }

            heldTime = DateTime.MinValue;
            return null;
        }

        /// <summary>
        /// Parses leading <c>node-N</c> from <see cref="LogigramState.Status"/> (PARSE_STATUS).
        /// </summary>
        /// <param name="status">Status string, or null.</param>
        /// <returns>Node number when the prefix matches; otherwise null (no fabricated node).</returns>
        public static int? TryParseNodeNumber(string? status)
        {
            if (string.IsNullOrEmpty(status))
            {
                return null;
            }

            Match match = NodeStatusPrefix.Match(status);
            if (!match.Success)
            {
                return null;
            }

            return int.TryParse(match.Groups[1].Value, out int nodeNumber) ? nodeNumber : (int?)null;
        }

        /// <summary>
        /// CURSOR_ONLY path-node highlight: true only when current Status encodes exactly <paramref name="nodeNumber"/>
        /// and State.BatteryId matches the panel under view.
        /// </summary>
        /// <param name="nodeNumber">Skeleton node id (1, 2, or 3).</param>
        /// <param name="state">Current State, or null.</param>
        /// <param name="underViewBatteryId">Battery this panel is watching.</param>
        /// <returns>True when the cursor Status is <c>node-{nodeNumber}:…</c> for the under-view Battery.</returns>
        public static bool IsPathNodeHighlighted(int nodeNumber, LogigramState? state, int underViewBatteryId)
        {
            if (state == null || state.BatteryId != underViewBatteryId)
            {
                return false;
            }

            int? parsed = TryParseNodeNumber(state.Status);
            return parsed.HasValue && parsed.Value == nodeNumber;
        }

        /// <summary>
        /// Lights terminal C only when Outcome.Regulation is Gamma and BatteryId matches the under-view Battery.
        /// </summary>
        /// <param name="outcome">Current Outcome, or null.</param>
        /// <param name="underViewBatteryId">Battery this panel is watching.</param>
        /// <returns>True when regulation is Gamma for the under-view Battery.</returns>
        public static bool IsTerminalCHighlightedFromOutcome(LogigramOutcome? outcome, int underViewBatteryId)
        {
            return outcome?.Regulation == LogigramRegulation.Gamma
                && outcome.BatteryId == underViewBatteryId;
        }

        /// <summary>
        /// Sets the paired Outcome cursor value (tests and Outcome companion subscription).
        /// Only messages for <see cref="BatteryId"/> update the held Outcome; others are ignored.
        /// </summary>
        /// <param name="outcome">Outcome at the playhead, or null when unavailable.</param>
        public void SetCurrentOutcome(LogigramOutcome? outcome)
        {
            if (outcome == null)
            {
                // Explicit clear (tests / callers).
                this.ClearHeldOutcome();
            }
            else
            {
                DateTime playhead = this.Navigator?.Cursor ?? DateTime.MaxValue;
                this.heldOutcomeForBattery = HoldOutcomeForBattery(
                    outcome,
                    dataAvailable: true,
                    cursorMessageTime: playhead,
                    playhead: playhead,
                    this.batteryId,
                    this.heldOutcomeForBattery,
                    this.heldOutcomeOriginatingTime,
                    out this.heldOutcomeOriginatingTime);
            }

            this.RaisePropertyChanged(nameof(this.CurrentOutcome));
            this.RaisePropertyChanged(nameof(this.IsTerminalCHighlighted));
        }

        /// <inheritdoc/>
        protected override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(sender, e);

            if (e.PropertyName == nameof(this.CurrentValue) ||
                e.PropertyName == nameof(this.CurrentData) ||
                e.PropertyName == nameof(this.CurrentOriginatingTime))
            {
                this.ApplyCursorState();
                this.RaiseGraphProperties();
            }

            if (e.PropertyName == nameof(this.CursorEpsilon) &&
                this.outcomeStreamSource != null &&
                this.outcomeSubscriberId != Guid.Empty)
            {
                StreamSource source = this.outcomeStreamSource;
                this.UnregisterOutcomeCompanion();
                this.RegisterOutcomeCompanion(source);
            }
        }

        /// <inheritdoc/>
        protected override void OnCursorChanged(object sender, NavigatorTimeChangedEventArgs e)
        {
            base.OnCursorChanged(sender, e);
            this.ApplyCursorState();
            this.ApplyOutcomePlayhead(e.NewTime);
            this.RaiseGraphProperties();
            this.RaisePropertyChanged(nameof(this.IsTerminalCHighlighted));
        }

        /// <inheritdoc/>
        protected override void OnStreamBound()
        {
            base.OnStreamBound();
            this.TryBindOutcomeCompanion();
        }

        /// <inheritdoc/>
        protected override void OnStreamUnbound()
        {
            this.UnregisterOutcomeCompanion();
            this.ClearHeldState();
            this.RaiseGraphProperties();
            base.OnStreamUnbound();
        }

        private void ApplyCursorState()
        {
            DateTime playhead = this.Navigator?.Cursor ?? DateTime.MaxValue;
            DateTime messageTime = this.HasCurrentValue ? this.CurrentOriginatingTime : DateTime.MinValue;
            this.heldStateForBattery = HoldStateForBattery(
                this.HasCurrentValue ? this.CurrentData : null,
                this.HasCurrentValue,
                messageTime,
                playhead,
                this.batteryId,
                this.heldStateForBattery,
                this.heldStateOriginatingTime,
                out this.heldStateOriginatingTime);
        }

        private void ApplyOutcomePlayhead(DateTime playhead)
        {
            this.heldOutcomeForBattery = HoldOutcomeForBattery(
                cursor: null,
                dataAvailable: false,
                cursorMessageTime: DateTime.MinValue,
                playhead: playhead,
                this.batteryId,
                this.heldOutcomeForBattery,
                this.heldOutcomeOriginatingTime,
                out this.heldOutcomeOriginatingTime);
        }

        private void ClearHeldState()
        {
            this.heldStateForBattery = null;
            this.heldStateOriginatingTime = DateTime.MinValue;
        }

        private void ClearHeldOutcome()
        {
            this.heldOutcomeForBattery = null;
            this.heldOutcomeOriginatingTime = DateTime.MinValue;
        }

        private void RaiseGraphProperties()
        {
            this.RaisePropertyChanged(nameof(this.DisplayText));
            this.RaisePropertyChanged(nameof(this.IsNode1Highlighted));
            this.RaisePropertyChanged(nameof(this.IsNode2Highlighted));
            this.RaisePropertyChanged(nameof(this.IsNode3Highlighted));
        }

        private void TryBindOutcomeCompanion()
        {
            this.UnregisterOutcomeCompanion();

            try
            {
                if (this.StreamBinding == null || this.StreamSource == null)
                {
                    return;
                }

                var session = VisualizationContext.Instance?.DatasetViewModel?.CurrentSessionViewModel;
                if (session == null)
                {
                    return;
                }

                var outcomeBinding = new StreamBinding(
                    LogigramOutcome.StreamName,
                    this.StreamBinding.PartitionName);

                StreamSource? source = session.CreateStreamSource(outcomeBinding, null, null);
                if (source == null)
                {
                    return;
                }

                this.RegisterOutcomeCompanion(source);
            }
            catch (Exception)
            {
                // Companion bind is best-effort; graph still works for State-only scrub.
                this.UnregisterOutcomeCompanion();
            }
        }

        private void RegisterOutcomeCompanion(StreamSource source)
        {
            if (this.Navigator == null)
            {
                return;
            }

            this.outcomeStreamSource = source;
            this.outcomeSubscriberId = DataManager.Instance.RegisterStreamValueSubscriber<LogigramOutcome>(
                source,
                this.CursorEpsilon,
                this.OnOutcomeReceived,
                this.Navigator.ViewRange.AsTimeInterval);

            DataManager.Instance.ReadAndPublishStreamValue(this.Navigator.Cursor);
        }

        private void UnregisterOutcomeCompanion()
        {
            if (this.outcomeSubscriberId != Guid.Empty)
            {
                DataManager.Instance.UnregisterStreamValueSubscriber<LogigramOutcome>(this.outcomeSubscriberId);
                this.outcomeSubscriberId = Guid.Empty;
            }

            this.outcomeStreamSource = null;
            this.ClearHeldOutcome();
            this.RaisePropertyChanged(nameof(this.CurrentOutcome));
            this.RaisePropertyChanged(nameof(this.IsTerminalCHighlighted));
        }

        private void OnOutcomeReceived(bool dataAvailable, LogigramOutcome value, DateTime originatingTime, DateTime creationTime)
        {
            // Value is only valid for the duration of this callback — copy fields.
            LogigramOutcome? copy = null;
            if (dataAvailable && value != null)
            {
                copy = new LogigramOutcome
                {
                    Regulation = value.Regulation,
                    BatteryId = value.BatteryId,
                };
            }

            bool available = dataAvailable && copy != null;
            DateTime messageTime = available ? originatingTime : DateTime.MinValue;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    // Skip if unbound since this callback was queued (avoids re-lighting C).
                    if (this.outcomeSubscriberId == Guid.Empty)
                    {
                        return;
                    }

                    this.ApplyOutcomeCursor(copy, available, messageTime);
                }));
            }
            else
            {
                this.ApplyOutcomeCursor(copy, available, messageTime);
            }
        }

        private void ApplyOutcomeCursor(LogigramOutcome? cursor, bool dataAvailable, DateTime messageTime)
        {
            DateTime playhead = this.Navigator?.Cursor ?? DateTime.MaxValue;
            this.heldOutcomeForBattery = HoldOutcomeForBattery(
                cursor,
                dataAvailable,
                messageTime,
                playhead,
                this.batteryId,
                this.heldOutcomeForBattery,
                this.heldOutcomeOriginatingTime,
                out this.heldOutcomeOriginatingTime);
            this.RaisePropertyChanged(nameof(this.CurrentOutcome));
            this.RaisePropertyChanged(nameof(this.IsTerminalCHighlighted));
        }
    }
}
