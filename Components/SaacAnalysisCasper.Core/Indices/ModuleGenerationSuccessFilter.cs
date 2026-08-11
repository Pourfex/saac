// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Indices
{
    using System;
    using Microsoft.Psi;

    /// <summary>
    /// Derives module-generation success from catalog <c>ModuleStatus</c> (<c>ValueTuple&lt;int,string&gt;</c>).
    /// </summary>
    /// <remarks>
    /// Assumption: <see cref="IndexFilterAssumptions.ModuleGenerationSuccessHeuristic"/>.
    /// Emits streams only (AD-9); preserves envelope OriginatingTime.
    /// </remarks>
    public static class ModuleGenerationSuccessFilter
    {
        // UTF-8 "réuss" via \u00e9 so source encoding cannot mojibake the accented e.
        private const string ReussAccented = "r\u00e9uss";

        private const string ReussAscii = "reuss";

        /// <summary>
        /// Projects each module-status message to a success bool.
        /// </summary>
        /// <param name="moduleStatus">Catalog Module status stream.</param>
        /// <param name="deliveryPolicy">Optional delivery policy.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>Stream of success flags (true = generation succeeded).</returns>
        public static IProducer<bool> Apply(
            IProducer<ValueTuple<int, string>> moduleStatus,
            DeliveryPolicy<ValueTuple<int, string>>? deliveryPolicy = null,
            string? name = null)
        {
            if (moduleStatus == null)
            {
                throw new ArgumentNullException(nameof(moduleStatus));
            }

            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(ModuleGenerationSuccessFilter) : name;
            return moduleStatus.Select(
                (ValueTuple<int, string> status) => IsSuccess(status),
                deliveryPolicy ?? DeliveryPolicy.Unlimited,
                operatorName);
        }

        /// <summary>
        /// Evaluates the documented success heuristic.
        /// </summary>
        /// <param name="status">Module status payload.</param>
        /// <returns>True when generation is considered successful.</returns>
        public static bool IsSuccess(ValueTuple<int, string> status)
        {
            if (status.Item1 == 1)
            {
                return true;
            }

            string text = status.Item2;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (text.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (text.IndexOf(ReussAccented, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (text.IndexOf(ReussAscii, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }
    }
}
