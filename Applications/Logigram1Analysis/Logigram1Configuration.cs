// <copyright file="Logigram1Configuration.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;
    using System.IO;
    using System.Security;
    using Newtonsoft.Json;

    /// <summary>
    /// Slim JSON config for Logigram 1: selects the Casper <c>.pds</c> store path (not an Expe2 session folder).
    /// </summary>
    public sealed class Logigram1Configuration
    {
        /// <summary>
        /// Gets or sets the absolute or relative path to the Casper Dataset <c>.pds</c> file.
        /// </summary>
        [JsonProperty("StorePath")]
        public string StorePath { get; set; }

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

            return configuration;
        }
    }
}
