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
    /// ONE_PANEL_TWO_SKELETONS: stacked person-1 (yellow) and person-2 (orange) skeletons.
    /// Primary bind is <see cref="LogigramState"/>; <see cref="LogigramOutcome"/> is a paired cursor value
    /// (auto-subscribed from the same partition when present) that lights terminal C for Gamma only
    /// when Outcome.BatteryId matches <see cref="BatteryId"/> and Outcome.PersonId matches the row.
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

        /// <summary>Person 1 highlight fill (yellow).</summary>
        public const string Person1HighlightColor = "#FFCC00";

        /// <summary>Person 2 highlight fill (orange).</summary>
        public const string Person2HighlightColor = "#E87A2E";

        private static readonly Regex NodeStatusPrefix = new Regex(@"^node-(\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private int batteryId = 1;
        private LogigramState? heldStatePerson1;
        private DateTime heldStatePerson1Time = DateTime.MinValue;
        private LogigramState? heldStatePerson2;
        private DateTime heldStatePerson2Time = DateTime.MinValue;
        private LogigramOutcome? heldOutcomePerson1;
        private DateTime heldOutcomePerson1Time = DateTime.MinValue;
        private LogigramOutcome? heldOutcomePerson2;
        private DateTime heldOutcomePerson2Time = DateTime.MinValue;
        private Guid outcomeSubscriberId = Guid.Empty;
        private StreamSource? outcomeStreamSource;

        /// <summary>
        /// Gets or sets the Battery under view. Only State/Outcome for this BatteryId drive highlights.
        /// Add one Logigram Graph panel per Battery and set this property on each.
        /// While scrubbing, holds the last matching message per PersonId for this Battery whose originating
        /// time is still at or before the playhead (other-Battery cursor values are ignored). Scrubbing
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
            }
        }

        /// <summary>
        /// Gets the combined status label for both persons under the viewed Battery (or honest empty).
        /// </summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public string DisplayText => FormatDisplayText(
            this.heldStatePerson1,
            this.heldStatePerson2,
            this.batteryId);

        /// <summary>Gets person-1 status line for the stacked skeleton label.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public string Person1DisplayText => FormatPersonDisplayText(this.heldStatePerson1, personId: 1, this.batteryId);

        /// <summary>Gets person-2 status line for the stacked skeleton label.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public string Person2DisplayText => FormatPersonDisplayText(this.heldStatePerson2, personId: 2, this.batteryId);

        /// <summary>Gets the held Outcome for person 1 (null when none yet).</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public LogigramOutcome? CurrentOutcomePerson1 => this.heldOutcomePerson1;

        /// <summary>Gets the held Outcome for person 2 (null when none yet).</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public LogigramOutcome? CurrentOutcomePerson2 => this.heldOutcomePerson2;

        /// <summary>Gets a value indicating whether person-1 node 1 is highlighted.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsPerson1Node1Highlighted => IsPathNodeHighlighted(1, this.heldStatePerson1, this.batteryId, personId: 1);

        /// <summary>Gets a value indicating whether person-1 node 2 is highlighted.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsPerson1Node2Highlighted => IsPathNodeHighlighted(2, this.heldStatePerson1, this.batteryId, personId: 1);

        /// <summary>Gets a value indicating whether person-1 node 3 is highlighted.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsPerson1Node3Highlighted => IsPathNodeHighlighted(3, this.heldStatePerson1, this.batteryId, personId: 1);

        /// <summary>Gets a value indicating whether person-1 terminal C is highlighted (Gamma only).</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsPerson1TerminalCHighlighted => IsTerminalCHighlightedFromOutcome(
            this.heldOutcomePerson1,
            this.batteryId,
            personId: 1);

        /// <summary>Gets a value indicating whether person-2 node 1 is highlighted.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsPerson2Node1Highlighted => IsPathNodeHighlighted(1, this.heldStatePerson2, this.batteryId, personId: 2);

        /// <summary>Gets a value indicating whether person-2 node 2 is highlighted.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsPerson2Node2Highlighted => IsPathNodeHighlighted(2, this.heldStatePerson2, this.batteryId, personId: 2);

        /// <summary>Gets a value indicating whether person-2 node 3 is highlighted.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsPerson2Node3Highlighted => IsPathNodeHighlighted(3, this.heldStatePerson2, this.batteryId, personId: 2);

        /// <summary>Gets a value indicating whether person-2 terminal C is highlighted (Gamma only).</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsPerson2TerminalCHighlighted => IsTerminalCHighlightedFromOutcome(
            this.heldOutcomePerson2,
            this.batteryId,
            personId: 2);

        /// <inheritdoc />
        [Browsable(false)]
        [IgnoreDataMember]
        public override DataTemplate DefaultViewTemplate =>
            XamlHelper.CreateTemplate(this.GetType(), typeof(LogigramGraphVisualizationObjectView));

        /// <summary>
        /// Formats the dual-person status label for the Battery under view.
        /// </summary>
        /// <param name="person1">Held State for person 1, or null.</param>
        /// <param name="person2">Held State for person 2, or null.</param>
        /// <param name="underViewBatteryId">Battery this panel is watching.</param>
        /// <returns>Label including under-view BatteryId and both person lines.</returns>
        public static string FormatDisplayText(LogigramState? person1, LogigramState? person2, int underViewBatteryId)
        {
            return $"Battery {underViewBatteryId} | "
                + FormatPersonDisplayText(person1, personId: 1, underViewBatteryId)
                + " | "
                + FormatPersonDisplayText(person2, personId: 2, underViewBatteryId);
        }

        /// <summary>
        /// Formats one person's status segment (or honest empty for that person).
        /// </summary>
        public static string FormatPersonDisplayText(LogigramState? state, int personId, int underViewBatteryId)
        {
            string prefix = $"P{personId}: ";
            if (state == null
                || state.BatteryId != underViewBatteryId
                || state.PersonId != personId)
            {
                return prefix + EmptyDisplayText;
            }

            return prefix + (state.Status ?? string.Empty);
        }

        /// <summary>
        /// Timeline-aware hold for one PersonId: accept a matching Battery+Person cursor message; keep the
        /// previous hold only while the playhead is still at or after that message’s originating time.
        /// </summary>
        public static LogigramState? HoldStateForPerson(
            LogigramState? cursor,
            bool hasCursorValue,
            DateTime cursorMessageTime,
            DateTime playhead,
            int underViewBatteryId,
            int personId,
            LogigramState? previousHeld,
            DateTime previousHeldTime,
            out DateTime heldTime)
        {
            if (hasCursorValue
                && cursor != null
                && cursor.BatteryId == underViewBatteryId
                && cursor.PersonId == personId)
            {
                heldTime = cursorMessageTime;
                return new LogigramState
                {
                    Status = cursor.Status ?? string.Empty,
                    BatteryId = cursor.BatteryId,
                    PersonId = cursor.PersonId,
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
        /// Timeline-aware Outcome hold for one PersonId (same rules as <see cref="HoldStateForPerson"/>).
        /// </summary>
        public static LogigramOutcome? HoldOutcomeForPerson(
            LogigramOutcome? cursor,
            bool dataAvailable,
            DateTime cursorMessageTime,
            DateTime playhead,
            int underViewBatteryId,
            int personId,
            LogigramOutcome? previousHeld,
            DateTime previousHeldTime,
            out DateTime heldTime)
        {
            if (dataAvailable
                && cursor != null
                && cursor.BatteryId == underViewBatteryId
                && cursor.PersonId == personId)
            {
                heldTime = cursorMessageTime;
                return new LogigramOutcome
                {
                    Regulation = cursor.Regulation,
                    BatteryId = cursor.BatteryId,
                    PersonId = cursor.PersonId,
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
        /// and State.BatteryId / PersonId match the under-view panel row.
        /// </summary>
        public static bool IsPathNodeHighlighted(
            int nodeNumber,
            LogigramState? state,
            int underViewBatteryId,
            int personId)
        {
            if (state == null
                || state.BatteryId != underViewBatteryId
                || state.PersonId != personId)
            {
                return false;
            }

            int? parsed = TryParseNodeNumber(state.Status);
            return parsed.HasValue && parsed.Value == nodeNumber;
        }

        /// <summary>
        /// Lights terminal C only when Outcome.Regulation is Gamma and BatteryId/PersonId match the row.
        /// </summary>
        public static bool IsTerminalCHighlightedFromOutcome(
            LogigramOutcome? outcome,
            int underViewBatteryId,
            int personId)
        {
            return outcome?.Regulation == LogigramRegulation.Gamma
                && outcome.BatteryId == underViewBatteryId
                && outcome.PersonId == personId;
        }

        /// <summary>
        /// Sets the paired Outcome cursor value (tests and Outcome companion subscription).
        /// Only messages for <see cref="BatteryId"/> update the matching PersonId hold; others are ignored.
        /// </summary>
        /// <param name="outcome">Outcome at the playhead, or null when unavailable.</param>
        public void SetCurrentOutcome(LogigramOutcome? outcome)
        {
            if (outcome == null)
            {
                this.ClearHeldOutcome();
            }
            else
            {
                DateTime playhead = this.Navigator?.Cursor ?? DateTime.MaxValue;
                this.ApplyOutcomeCursor(outcome, dataAvailable: true, messageTime: playhead);
                return;
            }

            this.RaisePropertyChanged(nameof(this.CurrentOutcomePerson1));
            this.RaisePropertyChanged(nameof(this.CurrentOutcomePerson2));
            this.RaisePropertyChanged(nameof(this.IsPerson1TerminalCHighlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson2TerminalCHighlighted));
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
            LogigramState? cursor = this.HasCurrentValue ? this.CurrentData : null;
            bool hasCursor = this.HasCurrentValue;

            this.heldStatePerson1 = HoldStateForPerson(
                cursor,
                hasCursor,
                messageTime,
                playhead,
                this.batteryId,
                personId: 1,
                this.heldStatePerson1,
                this.heldStatePerson1Time,
                out this.heldStatePerson1Time);

            this.heldStatePerson2 = HoldStateForPerson(
                cursor,
                hasCursor,
                messageTime,
                playhead,
                this.batteryId,
                personId: 2,
                this.heldStatePerson2,
                this.heldStatePerson2Time,
                out this.heldStatePerson2Time);
        }

        private void ApplyOutcomePlayhead(DateTime playhead)
        {
            this.heldOutcomePerson1 = HoldOutcomeForPerson(
                cursor: null,
                dataAvailable: false,
                cursorMessageTime: DateTime.MinValue,
                playhead: playhead,
                this.batteryId,
                personId: 1,
                this.heldOutcomePerson1,
                this.heldOutcomePerson1Time,
                out this.heldOutcomePerson1Time);

            this.heldOutcomePerson2 = HoldOutcomeForPerson(
                cursor: null,
                dataAvailable: false,
                cursorMessageTime: DateTime.MinValue,
                playhead: playhead,
                this.batteryId,
                personId: 2,
                this.heldOutcomePerson2,
                this.heldOutcomePerson2Time,
                out this.heldOutcomePerson2Time);
        }

        private void ClearHeldState()
        {
            this.heldStatePerson1 = null;
            this.heldStatePerson1Time = DateTime.MinValue;
            this.heldStatePerson2 = null;
            this.heldStatePerson2Time = DateTime.MinValue;
        }

        private void ClearHeldOutcome()
        {
            this.heldOutcomePerson1 = null;
            this.heldOutcomePerson1Time = DateTime.MinValue;
            this.heldOutcomePerson2 = null;
            this.heldOutcomePerson2Time = DateTime.MinValue;
        }

        private void RaiseGraphProperties()
        {
            this.RaisePropertyChanged(nameof(this.DisplayText));
            this.RaisePropertyChanged(nameof(this.Person1DisplayText));
            this.RaisePropertyChanged(nameof(this.Person2DisplayText));
            this.RaisePropertyChanged(nameof(this.IsPerson1Node1Highlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson1Node2Highlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson1Node3Highlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson1TerminalCHighlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson2Node1Highlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson2Node2Highlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson2Node3Highlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson2TerminalCHighlighted));
            this.RaisePropertyChanged(nameof(this.CurrentOutcomePerson1));
            this.RaisePropertyChanged(nameof(this.CurrentOutcomePerson2));
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
            this.RaisePropertyChanged(nameof(this.CurrentOutcomePerson1));
            this.RaisePropertyChanged(nameof(this.CurrentOutcomePerson2));
            this.RaisePropertyChanged(nameof(this.IsPerson1TerminalCHighlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson2TerminalCHighlighted));
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
                    PersonId = value.PersonId,
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

            this.heldOutcomePerson1 = HoldOutcomeForPerson(
                cursor,
                dataAvailable,
                messageTime,
                playhead,
                this.batteryId,
                personId: 1,
                this.heldOutcomePerson1,
                this.heldOutcomePerson1Time,
                out this.heldOutcomePerson1Time);

            this.heldOutcomePerson2 = HoldOutcomeForPerson(
                cursor,
                dataAvailable,
                messageTime,
                playhead,
                this.batteryId,
                personId: 2,
                this.heldOutcomePerson2,
                this.heldOutcomePerson2Time,
                out this.heldOutcomePerson2Time);

            this.RaisePropertyChanged(nameof(this.CurrentOutcomePerson1));
            this.RaisePropertyChanged(nameof(this.CurrentOutcomePerson2));
            this.RaisePropertyChanged(nameof(this.IsPerson1TerminalCHighlighted));
            this.RaisePropertyChanged(nameof(this.IsPerson2TerminalCHighlighted));
        }
    }
}
