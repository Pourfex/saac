// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Config
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Newtonsoft.Json;

    /// <summary>
    /// Core-owned analysis run-config (AD-8). Hosts deserialize this schema only — no host-local DTO.
    /// </summary>
    public sealed class AnalysisRunConfig
    {
        private static readonly HashSet<string> AllowedGraphIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "Poc",
            "Logigramme1",
            "Logigramme2",
            "Logigramme3",
            "Logigramme4",
        };

        /// <summary>
        /// Gets or sets the single window length in milliseconds, when not sweeping.
        /// Exactly one of <see cref="WindowMs"/> or <see cref="WindowMsSweep"/> must be set.
        /// </summary>
        [JsonProperty("windowMs")]
        public int? WindowMs { get; set; }

        /// <summary>
        /// Gets or sets the multi-window sweep lengths in milliseconds.
        /// AD-8 text said <c>windowMsSweep</c> as a lone int, but Epic Stories 1.6/1.7 need multiple W
        /// values under the same key — this property is therefore an int array, not a lone int.
        /// Exactly one of <see cref="WindowMs"/> or <see cref="WindowMsSweep"/> must be set.
        /// </summary>
        [JsonProperty("windowMsSweep")]
        public IReadOnlyList<int> WindowMsSweep { get; set; }

        /// <summary>
        /// Gets or sets the output root directory path.
        /// </summary>
        [JsonProperty("outputRoot")]
        public string OutputRoot { get; set; }

        /// <summary>
        /// Gets or sets the graph ids to run (e.g. <c>Poc</c>, <c>Logigramme1</c>).
        /// </summary>
        [JsonProperty("graphs")]
        public string[] Graphs { get; set; }

        /// <summary>
        /// Loads and validates an <see cref="AnalysisRunConfig"/> from a JSON file path.
        /// </summary>
        /// <param name="path">Path to a JSON run-config file.</param>
        /// <returns>A validated configuration instance.</returns>
        public static AnalysisRunConfig Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Run-config path must be non-empty.", nameof(path));
            }

            string json = File.ReadAllText(path);
            return Parse(json);
        }

        /// <summary>
        /// Parses and validates an <see cref="AnalysisRunConfig"/> from a JSON string.
        /// </summary>
        /// <param name="json">JSON run-config text.</param>
        /// <returns>A validated configuration instance.</returns>
        public static AnalysisRunConfig Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Run-config JSON must be non-empty.", nameof(json));
            }

            AnalysisRunConfig config = JsonConvert.DeserializeObject<AnalysisRunConfig>(json);
            if (config == null)
            {
                throw new InvalidOperationException("Run-config JSON deserialized to null.");
            }

            config.Validate();
            return config;
        }

        /// <summary>
        /// Validates required fields, window mode exclusivity, and allowed graph ids.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(this.OutputRoot))
            {
                throw new InvalidOperationException("Run-config requires a non-empty 'outputRoot'.");
            }

            if (this.Graphs == null || this.Graphs.Length == 0)
            {
                throw new InvalidOperationException("Run-config requires a non-empty 'graphs' array.");
            }

            for (int i = 0; i < this.Graphs.Length; i++)
            {
                string graphId = this.Graphs[i];
                if (string.IsNullOrWhiteSpace(graphId))
                {
                    throw new InvalidOperationException("Run-config 'graphs' must not contain null or empty entries.");
                }

                if (!AllowedGraphIds.Contains(graphId))
                {
                    throw new InvalidOperationException(
                        "Unknown graph id '" + graphId + "'. Allowed: " + string.Join(", ", AllowedGraphIds.OrderBy(id => id)) + ".");
                }
            }

            bool hasWindowMs = this.WindowMs.HasValue;
            bool hasWindowMsSweep = this.WindowMsSweep != null && this.WindowMsSweep.Count > 0;

            if (hasWindowMs == hasWindowMsSweep)
            {
                throw new InvalidOperationException(
                    "Run-config requires exactly one of 'windowMs' (single value) or non-empty 'windowMsSweep' (array), not both and not neither.");
            }

            // Positive-duration sanity only; W bounds [100 ms, 2 min] are Story 1.6.
            if (hasWindowMs && this.WindowMs.Value <= 0)
            {
                throw new InvalidOperationException("Run-config 'windowMs' must be a positive duration in milliseconds.");
            }

            if (hasWindowMsSweep)
            {
                for (int i = 0; i < this.WindowMsSweep.Count; i++)
                {
                    if (this.WindowMsSweep[i] <= 0)
                    {
                        throw new InvalidOperationException(
                            "Run-config 'windowMsSweep' entries must be positive durations in milliseconds.");
                    }
                }
            }
        }
    }
}
