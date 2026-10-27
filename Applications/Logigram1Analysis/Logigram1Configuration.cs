// <copyright file="Logigram1Configuration.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security;
    using Newtonsoft.Json;

    /// <summary>
    /// Slim JSON config for Logigram 1: selects the Casper <c>.pds</c> store path (not an Expe2 session folder),
    /// which Logic units are enabled, and optional analysis output path (OUTPUT_HOME B).
    /// </summary>
    public sealed class Logigram1Configuration
    {
        /// <summary>
        /// Default enabled unit id shipped in <c>logigram1.json</c> (<c>Logigram1.GammaPath</c>).
        /// </summary>
        public const string DefaultGammaPathUnitId = Logigram1UnitTopicRegistry.GammaPathUnitId;

        /// <summary>
        /// Gets or sets the absolute or relative path to the Casper Dataset <c>.pds</c> file.
        /// </summary>
        [JsonProperty("StorePath")]
        public string StorePath { get; set; }

        /// <summary>
        /// Gets or sets Logic unit ids whose AD-7 source topics must be present in the store.
        /// Empty or absent → substrate smoke only (no Logic-topic check). Shipped default includes
        /// <see cref="DefaultGammaPathUnitId"/>.
        /// </summary>
        [JsonProperty("EnabledUnits")]
        public List<string> EnabledUnits { get; set; }

        /// <summary>
        /// Gets or sets optional analysis Dataset <c>.pds</c> path (OUTPUT_HOME B).
        /// Absent or empty → auto sibling <c>&lt;inputName&gt;.Logigram1Analysis.pds</c> beside the input.
        /// </summary>
        [JsonProperty("OutputPath")]
        public string OutputPath { get; set; }

        /// <summary>
        /// Loads configuration from a JSON file.
        /// </summary>
        /// <param name="path">Path to the JSON config file.</param>
        /// <returns>Deserialized configuration.</returns>
        /// <exception cref="ArgumentException">Path missing, file missing, invalid JSON, or empty <see cref="StorePath"/>.</exception>
        public static Logigram1Configuration Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Missing value for --config <file.json>.");
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is PathTooLongException
                || exception is NotSupportedException
                || exception is SecurityException)
            {
                throw new ArgumentException($"Invalid configuration path \"{path}\": {exception.Message}", exception);
            }

            if (!File.Exists(fullPath))
            {
                throw new ArgumentException($"Configuration file not found: \"{fullPath}\".");
            }

            string json;
            try
            {
                json = File.ReadAllText(fullPath);
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException
                || exception is SecurityException
                || exception is NotSupportedException)
            {
                throw new ArgumentException($"Failed to read configuration file \"{fullPath}\": {exception.Message}", exception);
            }

            Logigram1Configuration configuration;
            try
            {
                configuration = JsonConvert.DeserializeObject<Logigram1Configuration>(json);
            }
            catch (JsonException exception)
            {
                throw new ArgumentException($"Invalid configuration JSON in \"{fullPath}\": {exception.Message}", exception);
            }

            if (configuration == null)
            {
                throw new ArgumentException($"Invalid configuration JSON in \"{fullPath}\".");
            }

            configuration.StorePath = configuration.StorePath?.Trim();
            if (string.IsNullOrEmpty(configuration.StorePath))
            {
                throw new ArgumentException($"Configuration \"{fullPath}\" is missing a non-empty StorePath.");
            }

            // Absent EnabledUnits → empty list (substrate only). Callers that want the stub unit
            // ship it explicitly (see Applications/Logigram1Analysis/logigram1.json).
            if (configuration.EnabledUnits == null)
            {
                configuration.EnabledUnits = new List<string>();
            }

            configuration.OutputPath = configuration.OutputPath?.Trim();
            if (string.IsNullOrEmpty(configuration.OutputPath))
            {
                configuration.OutputPath = null;
            }

            return configuration;
        }
    }
}
