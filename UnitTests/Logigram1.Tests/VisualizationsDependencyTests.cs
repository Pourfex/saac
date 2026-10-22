// <copyright file="VisualizationsDependencyTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Xml.Linq;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Ensures Visualizations depends on Logigram1 contracts only (NFR5 / AD-5).
    /// </summary>
    [TestClass]
    public class VisualizationsDependencyTests
    {
        private static readonly string[] ForbiddenLogicFragments =
        {
            "Logigram1.Logic",
            "SAAC-Logigram1.Logic",
            "Logigram1Logic",
            "LogigramLogic",
            "LogicUnit",
            "LogicUnits",
        };

        /// <summary>Visualizations references contracts Logigram1 and never Logigram1.Logic.</summary>
        [TestMethod]
        public void VisualizationsCsproj_ReferencesContractsOnly_AmongLogigramAssemblies()
        {
            string csprojPath = FindVisualizationsCsproj();
            XDocument document = XDocument.Load(csprojPath);
            XNamespace msbuild = document.Root?.Name.Namespace ?? XNamespace.None;

            var projectReferences = document
                .Descendants(msbuild + "ProjectReference")
                .Select(element => (string)element.Attribute("Include") ?? string.Empty)
                .ToList();

            var packageReferences = document
                .Descendants(msbuild + "PackageReference")
                .Select(element => (string)element.Attribute("Include") ?? string.Empty)
                .ToList();

            Assert.IsTrue(
                projectReferences.Any(IsLogigram1ContractsReference),
                "Visualizations must reference Logigram1 contracts.");

            AssertNoForbiddenLogicReference(projectReferences, "ProjectReference");
            AssertNoForbiddenLogicReference(packageReferences, "PackageReference");
        }

        private static void AssertNoForbiddenLogicReference(System.Collections.Generic.List<string> references, string kind)
        {
            foreach (string reference in references)
            {
                foreach (string forbidden in ForbiddenLogicFragments)
                {
                    Assert.IsFalse(
                        reference.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0,
                        $"Visualizations must not have {kind} '{reference}' matching '{forbidden}'.");
                }
            }
        }

        private static bool IsLogigram1ContractsReference(string include)
        {
            string normalized = include.Replace('/', '\\');
            return normalized.EndsWith("Logigram1\\Logigram1.csproj", StringComparison.OrdinalIgnoreCase)
                && normalized.IndexOf("Logigram1.Logic", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string FindVisualizationsCsproj()
        {
            string[] relativeCandidates =
            {
                Path.Combine("Components", "Visualizations", "Visualizations.csproj"),
                Path.Combine("..", "..", "..", "..", "Components", "Visualizations", "Visualizations.csproj"),
                Path.Combine("..", "..", "..", "..", "..", "Components", "Visualizations", "Visualizations.csproj"),
            };

            foreach (string relative in relativeCandidates)
            {
                string full = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relative));
                if (File.Exists(full))
                {
                    return full;
                }
            }

            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "Components", "Visualizations", "Visualizations.csproj");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Could not locate Visualizations.csproj for dependency audit.");
            return null;
        }
    }
}
