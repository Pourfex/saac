// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.

namespace SaacAnalysisCasper.PsiStudioPlugin.Services
{
    using System;
    using System.IO;
    using Newtonsoft.Json.Linq;
    using SaacAnalysisCasper.Core.Config;

    /// <summary>
    /// Loads Core <see cref="AnalysisRunConfig"/> for the Plugin host; may default <c>outputRoot</c> only (AD-8).
    /// </summary>
    public static class PluginRunConfigLoader
    {
        /// <summary>
        /// Default output root when the JSON omits or leaves <c>outputRoot</c> empty.
        /// </summary>
        public static readonly string DefaultOutputRoot =
            @"C:\Users\alexi\Documents\Dev\Ressources\_analysis-out-plugin";

        /// <summary>
        /// Default sample run-config path shipped with Core.
        /// </summary>
        public static readonly string DefaultRunConfigPath =
            @"C:\Users\alexi\Documents\Dev\saac\Components\SaacAnalysisCasper.Core\Config\Samples\analysis-run.sample.json";

        /// <summary>
        /// Loads and validates Core run-config from <paramref name="path"/>, defaulting <c>outputRoot</c> when absent/empty.
        /// </summary>
        /// <param name="path">JSON file path.</param>
        /// <param name="defaultOutputRoot">Optional override for the default output root.</param>
        /// <returns>Validated Core config (AD-5 templates unchanged).</returns>
        public static AnalysisRunConfig Load(string path, string? defaultOutputRoot = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Run-config path must be non-empty.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Run-config JSON not found.", path);
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

            string effectiveDefault = string.IsNullOrWhiteSpace(defaultOutputRoot)
                ? DefaultOutputRoot
                : defaultOutputRoot;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Run-config JSON is malformed or incompatible with the Core schema.",
                    ex);
            }

            JToken? outputRootToken = root["outputRoot"];
            bool needsDefault = outputRootToken == null
                || outputRootToken.Type == JTokenType.Null
                || outputRootToken.Type != JTokenType.String
                || string.IsNullOrWhiteSpace(outputRootToken.Value<string>());
            if (needsDefault)
            {
                root["outputRoot"] = effectiveDefault;
            }

            return AnalysisRunConfig.Parse(root.ToString());
        }
    }
}
