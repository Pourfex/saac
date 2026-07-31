// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Windowing
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Psi;

    /// <summary>
    /// Shared Core hopping/sliding window operator (AD-4 / AD-6).
    /// Wraps Psi <c>source.Window(lookback)</c> using <see cref="HoppingWindowPolicy.CreateLookback"/>.
    /// Each call constructs a per-branch operator instance — no static/shared mutable window state across M1/M2 (AD-3).
    /// Emits streams only; does not open CSV or stores (AD-9).
    /// Output <c>OriginatingTime</c> comes from the Psi <c>RelativeTimeWindow</c> anchor (per source message), never <see cref="DateTime.Now"/>.
    /// </summary>
    public static class HoppingWindow
    {
        /// <summary>
        /// Applies the frozen hopping lookback of length <paramref name="windowMs"/> to <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">Type of source messages.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="windowMs">Window length W from run-config (not hard-coded in graphs).</param>
        /// <param name="deliveryPolicy">Optional delivery policy for the source stream.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>
        /// Stream of windowed message arrays. <c>OriginatingTime</c> is the Psi window anchor time.
        /// </returns>
        public static IProducer<T[]> Apply<T>(
            IProducer<T> source,
            int windowMs,
            DeliveryPolicy<T>? deliveryPolicy = null,
            string? name = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            RelativeTimeInterval lookback = HoppingWindowPolicy.CreateLookback(windowMs);
            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(HoppingWindow) : name;
            return source.Window(lookback, deliveryPolicy, operatorName);
        }

        /// <summary>
        /// Applies the frozen hopping lookback and projects each window with <paramref name="selector"/>.
        /// </summary>
        /// <typeparam name="TSource">Type of source messages.</typeparam>
        /// <typeparam name="TOutput">Type of projected output messages.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="windowMs">Window length W from run-config (not hard-coded in graphs).</param>
        /// <param name="selector">Projects the window messages (with envelopes) to an output value.</param>
        /// <param name="deliveryPolicy">Optional delivery policy for the source stream.</param>
        /// <param name="name">Optional operator name.</param>
        /// <returns>
        /// Stream of projected window results. <c>OriginatingTime</c> is the Psi window anchor time.
        /// </returns>
        public static IProducer<TOutput> Apply<TSource, TOutput>(
            IProducer<TSource> source,
            int windowMs,
            Func<IEnumerable<Message<TSource>>, TOutput> selector,
            DeliveryPolicy<TSource>? deliveryPolicy = null,
            string? name = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            RelativeTimeInterval lookback = HoppingWindowPolicy.CreateLookback(windowMs);
            string operatorName = string.IsNullOrWhiteSpace(name) ? nameof(HoppingWindow) : name;
            return source.Window(lookback, selector, deliveryPolicy, operatorName);
        }
    }
}
