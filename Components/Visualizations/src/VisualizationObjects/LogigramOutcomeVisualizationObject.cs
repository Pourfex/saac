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
    /// Canvas/instant shell for <see cref="LogigramOutcome"/> at the playhead (AD-10).
    /// </summary>
    [VisualizationObject("Logigram Outcome")]
    [VisualizationPanelType(VisualizationPanelType.Canvas)]
    public class LogigramOutcomeVisualizationObject : StreamValueVisualizationObject<LogigramOutcome>
    {
        /// <summary>
        /// Honest label when the cursor has no message (unbound, gap, or empty store).
        /// </summary>
        public const string EmptyDisplayText = "no outcome";

        /// <summary>
        /// Gets the text shown in the shell: regulation + BatteryId when present, otherwise <see cref="EmptyDisplayText"/>.
        /// </summary>
        [Browsable(false)]
        [IgnoreDataMember]
        public string DisplayText => FormatDisplayText(this.HasCurrentValue ? this.CurrentData : null);

        /// <inheritdoc />
        [Browsable(false)]
        [IgnoreDataMember]
        public override DataTemplate DefaultViewTemplate =>
            XamlHelper.CreateTemplate(this.GetType(), typeof(LogigramOutcomeVisualizationObjectView));

        /// <summary>
        /// Formats display text for a cursor value. Null yields an honest empty label — never invents Alpha|Beta|Gamma.
        /// </summary>
        /// <param name="outcome">The outcome at the cursor, or null when unavailable.</param>
        /// <returns>Regulation label with BatteryId, or <see cref="EmptyDisplayText"/>.</returns>
        public static string FormatDisplayText(LogigramOutcome? outcome)
        {
            if (outcome == null)
            {
                return EmptyDisplayText;
            }

            return outcome.ToString();
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
