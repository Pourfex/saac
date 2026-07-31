// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.Core.Config
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using Newtonsoft.Json;
    using SaacAnalysisCasper.Core.Windowing;

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
        /// Gets the single window length in milliseconds, when not sweeping.
        /// Exactly one of <see cref="WindowMs"/> or <see cref="WindowMsSweep"/> must be set.
        /// </summary>
        [JsonProperty("windowMs")]
        public int? WindowMs { get; private set; }

        /// <summary>
        /// Gets the multi-window sweep lengths in milliseconds.
        /// AD-8 text said <c>windowMsSweep</c> as a lone int, but Epic Stories 1.6/1.7 need multiple W
        /// values under the same key — this property is therefore an int array, not a lone int.
        /// Exactly one of <see cref="WindowMs"/> or <see cref="WindowMsSweep"/> must be set.
        /// </summary>
        [JsonProperty("windowMsSweep")]
        public IReadOnlyList<int> WindowMsSweep { get; private set; }

        /// <summary>
        /// Gets the output root directory path.
        /// </summary>
        [JsonProperty("outputRoot")]
        public string OutputRoot { get; private set; }

        /// <summary>
        /// Gets the graph ids to run (e.g. <c>Poc</c>, <c>Logigramme1</c>).
        /// </summary>
        [JsonProperty("graphs")]
        public IReadOnlyList<string> Graphs { get; private set; }

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

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Failed to read run-config file '" + path + "'.", ex);
            }

            try
            {
                return Parse(json);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("Invalid run-config file '" + path + "': " + ex.Message, ex);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException("Invalid run-config file '" + path + "': " + ex.Message, ex);
            }
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

            AnalysisRunConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<AnalysisRunConfig>(json);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Run-config JSON is malformed or incompatible with the Core schema.", ex);
            }

            if (config == null)
            {
                throw new InvalidOperationException("Run-config JSON deserialized to null.");
            }

            config.FreezeCollections();
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

            if (this.Graphs == null || this.Graphs.Count == 0)
            {
                throw new InvalidOperationException("Run-config requires a non-empty 'graphs' array.");
            }

            HashSet<string> seenGraphIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < this.Graphs.Count; i++)
            {
                string graphId = this.Graphs[i];
                if (string.IsNullOrWhiteSpace(graphId))
                {
                    throw new InvalidOperationException("Run-config 'graphs' must not contain null or empty entries.");
                }

                if (!seenGraphIds.Add(graphId))
                {
                    throw new InvalidOperationException("Run-config 'graphs' contains duplicate id '" + graphId + "'.");
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

            if (hasWindowMs)
            {
                HoppingWindowPolicy.ValidateWindowMs(this.WindowMs.Value, "windowMs");
            }

            if (hasWindowMsSweep)
            {
                for (int i = 0; i < this.WindowMsSweep.Count; i++)
                {
                    HoppingWindowPolicy.ValidateWindowMs(this.WindowMsSweep[i], "windowMsSweep[" + i + "]");
                }
            }
        }

        private void FreezeCollections()
        {
            if (this.Graphs != null)
            {
                this.Graphs = new ReadOnlyCollection<string>(this.Graphs.ToArray());
            }

            if (this.WindowMsSweep != null)
            {
                this.WindowMsSweep = new ReadOnlyCollection<int>(this.WindowMsSweep.ToArray());
            }
        }
    }
}
