// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Poc
{
    /// <summary>
    /// Unified inject payload for A∧B⇒C (AD-7 Core analysis DTO — not a capture catalog type).
    /// Stream shape: one tagged stream of A|B; hosts inject; Core windows and derives C.
    /// </summary>
    public sealed class PocTaggedEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PocTaggedEvent"/> class.
        /// </summary>
        /// <param name="kind">Whether this message is A or B.</param>
        public PocTaggedEvent(PocEventKind kind)
        {
            this.Kind = kind;
        }

        /// <summary>
        /// Gets whether this message is premise A or premise B.
        /// </summary>
        public PocEventKind Kind { get; }
    }
}
