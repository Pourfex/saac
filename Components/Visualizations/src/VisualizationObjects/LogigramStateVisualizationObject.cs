// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SAAC.Visualizations
{
    using System.ComponentModel;
    using System.Runtime.Serialization;
    using System.Windows;
    using Microsoft.Psi.Visualization.Helpers;
    using Microsoft.Psi.Visualization.VisualizationObjects;
    using Microsoft.Psi.Visualization.VisualizationPanels;
    using SAAC.Logigram1;
    using SAAC.Visualizations.Views;

    /// <summary>
    /// Canvas/instant shell for session-scoped <see cref="LogigramState"/> at the playhead.
    /// </summary>
    [VisualizationObject("Logigram State")]
    [VisualizationPanelType(VisualizationPanelType.Canvas)]
    public class LogigramStateVisualizationObject : StreamValueVisualizationObject<LogigramState>
    {
        /// <summary>
        /// Honest label when the cursor has no message (unbound, gap, or empty store).
        /// </summary>
        public const string EmptyDisplayText = "no state";

        /// <summary>
        /// Gets the text shown in the shell: status when a message is present, otherwise <see cref="EmptyDisplayText"/>.
        /// </summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public string DisplayText => FormatDisplayText(this.HasCurrentValue ? this.CurrentData : null);

        /// <inheritdoc />
        [Browsable(false)]
        [IgnoreDataMember]
        public override DataTemplate DefaultViewTemplate =>
            XamlHelper.CreateTemplate(this.GetType(), typeof(LogigramStateVisualizationObjectView));

        /// <summary>
        /// Formats display text for a cursor value. Null (missing/empty at cursor) yields an honest empty label — never a fabricated regulation value.
        /// </summary>
        /// <param name="state">The state at the cursor, or null when unavailable.</param>
        /// <returns>Status text, or <see cref="EmptyDisplayText"/> when <paramref name="state"/> is null.</returns>
        public static string FormatDisplayText(LogigramState? state)
        {
            if (state == null)
            {
                return EmptyDisplayText;
            }

            return state.Status ?? string.Empty;
        }

        /// <inheritdoc/>
        protected override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(sender, e);

            if (e.PropertyName == nameof(this.CurrentValue) ||
                e.PropertyName == nameof(this.CurrentData))
            {
                this.RaisePropertyChanged(nameof(this.DisplayText));
            }
        }
    }
}
