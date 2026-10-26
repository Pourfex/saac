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
    using Microsoft.Psi.Visualization.VisualizationObjects;
    using Microsoft.Psi.Visualization.VisualizationPanels;
    using SAAC.Logigram1;
    using SAAC.Visualizations.Views;

    /// <summary>
    /// Canvas Gamma-path (1→2→3→C) Logigram graph at the playhead.
    /// Primary bind is <see cref="LogigramState"/>; <see cref="LogigramOutcome"/> is a paired cursor value
    /// (auto-subscribed from the same partition when present) that lights terminal C for Gamma only.
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
        /// Static Gamma-path skeleton node numbers (GRAPH_SCOPE = GAMMA_PATH). Easy to extend later.
        /// </summary>
        public static readonly int[] GammaPathNodeNumbers = { 1, 2, 3 };

        /// <summary>Terminal C label on the Gamma path.</summary>
        public const string TerminalCLabel = "C";

        private static readonly Regex NodeStatusPrefix = new Regex(@"^node-(\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private LogigramOutcome? currentOutcome;
        private Guid outcomeSubscriberId = Guid.Empty;
        private StreamSource? outcomeStreamSource;

        /// <summary>
        /// Gets the status label: State Status + BatteryId when present, otherwise <see cref="EmptyDisplayText"/>.
        /// </summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public string DisplayText => FormatDisplayText(this.HasCurrentValue ? this.CurrentData : null);

        /// <summary>
        /// Gets the paired Outcome at the playhead (null when absent). Used for terminal C only.
        /// </summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public LogigramOutcome? CurrentOutcome => this.currentOutcome;

        /// <summary>Gets a value indicating whether Gamma-path node 1 is highlighted at the cursor.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsNode1Highlighted => IsPathNodeHighlighted(1, this.HasCurrentValue ? this.CurrentData : null);

        /// <summary>Gets a value indicating whether Gamma-path node 2 is highlighted at the cursor.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsNode2Highlighted => IsPathNodeHighlighted(2, this.HasCurrentValue ? this.CurrentData : null);

        /// <summary>Gets a value indicating whether Gamma-path node 3 is highlighted at the cursor.</summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsNode3Highlighted => IsPathNodeHighlighted(3, this.HasCurrentValue ? this.CurrentData : null);

        /// <summary>
        /// Gets a value indicating whether terminal C is highlighted from current Outcome (Gamma only; never invented from State).
        /// </summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public bool IsTerminalCHighlighted => IsTerminalCHighlightedFromOutcome(this.currentOutcome);

        /// <inheritdoc />
        [Browsable(false)]
        [IgnoreDataMember]
        public override DataTemplate DefaultViewTemplate =>
            XamlHelper.CreateTemplate(this.GetType(), typeof(LogigramGraphVisualizationObjectView));

        /// <summary>
        /// Formats the status label. Null State yields an honest empty label — never a fabricated regulation.
        /// </summary>
        /// <param name="state">State at the cursor, or null.</param>
        /// <returns>Status + BatteryId, or <see cref="EmptyDisplayText"/>.</returns>
        public static string FormatDisplayText(LogigramState? state)
        {
            if (state == null)
            {
                return EmptyDisplayText;
            }

            string status = state.Status ?? string.Empty;
            return $"{status} BatteryId={state.BatteryId}";
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
        /// CURSOR_ONLY path-node highlight: true only when current Status encodes exactly <paramref name="nodeNumber"/>.
        /// </summary>
        /// <param name="nodeNumber">Skeleton node id (1, 2, or 3).</param>
        /// <param name="state">Current State, or null.</param>
        /// <returns>True when the cursor Status is <c>node-{nodeNumber}:…</c>.</returns>
        public static bool IsPathNodeHighlighted(int nodeNumber, LogigramState? state)
        {
            int? parsed = TryParseNodeNumber(state?.Status);
            return parsed.HasValue && parsed.Value == nodeNumber;
        }

        /// <summary>
        /// Lights terminal C only when Outcome.Regulation is Gamma (OUTCOME_ON_GRAPH). Null/absent does not light C.
        /// </summary>
        /// <param name="outcome">Current Outcome, or null.</param>
        /// <returns>True when regulation is Gamma.</returns>
        public static bool IsTerminalCHighlightedFromOutcome(LogigramOutcome? outcome)
        {
            return outcome?.Regulation == LogigramRegulation.Gamma;
        }

        /// <summary>
        /// Sets the paired Outcome cursor value (tests and Outcome companion subscription).
        /// </summary>
        /// <param name="outcome">Outcome at the playhead, or null for honest empty terminal.</param>
        public void SetCurrentOutcome(LogigramOutcome? outcome)
        {
            this.currentOutcome = outcome;
            this.RaisePropertyChanged(nameof(this.CurrentOutcome));
            this.RaisePropertyChanged(nameof(this.IsTerminalCHighlighted));
        }

        /// <inheritdoc/>
        protected override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(sender, e);

            if (e.PropertyName == nameof(this.CurrentValue) ||
                e.PropertyName == nameof(this.CurrentData))
            {
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
        protected override void OnStreamBound()
        {
            base.OnStreamBound();
            this.TryBindOutcomeCompanion();
        }

        /// <inheritdoc/>
        protected override void OnStreamUnbound()
        {
            this.UnregisterOutcomeCompanion();
            base.OnStreamUnbound();
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
            this.SetCurrentOutcome(null);
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

                    this.SetCurrentOutcome(copy);
                }));
            }
            else
            {
                this.SetCurrentOutcome(copy);
            }
        }
    }
}
