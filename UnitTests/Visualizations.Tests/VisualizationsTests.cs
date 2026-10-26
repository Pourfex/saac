// <copyright file="VisualizationsTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Visualizations.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using Microsoft.Psi;
    using Microsoft.Psi.Visualization.VisualizationObjects;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SAAC.Logigram1;
    using SAAC.Visualizations;

    [TestClass]
    public class VisualizationsTests
    {
        private static readonly string[] ForbiddenLogicUnitProjectNameFragments =
        {
            "LogicUnit",
            "LogicUnits",
            "Logigram1.Logic",
            "SAAC-Logigram1.Logic",
            "Logigram1Logic",
            "LogigramLogic",
        };

        [TestMethod]
        public void LogigramStateVisualizationObject_HasVisualizationObjectAttribute()
        {
            VisualizationObjectAttribute attribute = typeof(LogigramStateVisualizationObject)
                .GetCustomAttribute<VisualizationObjectAttribute>();

            Assert.IsNotNull(attribute);
            Assert.AreEqual("Logigram State", attribute.CommandText);
        }

        [TestMethod]
        public void LogigramOutcomeVisualizationObject_HasVisualizationObjectAttribute()
        {
            VisualizationObjectAttribute attribute = typeof(LogigramOutcomeVisualizationObject)
                .GetCustomAttribute<VisualizationObjectAttribute>();

            Assert.IsNotNull(attribute);
            Assert.AreEqual("Logigram Outcome", attribute.CommandText);
        }

        [TestMethod]
        public void LogigramOutcomeVisualizationObject_BindsToLogigram1ContractType()
        {
            Type visualizationType = typeof(LogigramOutcomeVisualizationObject);
            Type baseType = visualizationType.BaseType;

            Assert.IsNotNull(baseType);
            Assert.IsTrue(baseType.IsGenericType);
            Assert.AreEqual(typeof(StreamValueVisualizationObject<>), baseType.GetGenericTypeDefinition());
            Assert.AreEqual(typeof(LogigramOutcome), baseType.GetGenericArguments()[0]);
        }

        [TestMethod]
        public void FormatDisplayText_Outcome_Null_ReturnsHonestEmptyLabel()
        {
            Assert.AreEqual(
                LogigramOutcomeVisualizationObject.EmptyDisplayText,
                LogigramOutcomeVisualizationObject.FormatDisplayText(null));
            Assert.AreEqual("no outcome", LogigramOutcomeVisualizationObject.FormatDisplayText(null));
        }

        [TestMethod]
        public void FormatDisplayText_Outcome_WithGamma_ShowsRegulationAndBatteryId()
        {
            var outcome = new LogigramOutcome
            {
                Regulation = LogigramRegulation.Gamma,
                BatteryId = 7,
            };

            Assert.AreEqual("Gamma BatteryId=7", LogigramOutcomeVisualizationObject.FormatDisplayText(outcome));
        }

        [TestMethod]
        public void FormatDisplayText_Outcome_NullRegulation_ShowsAbsentNotAlpha()
        {
            var outcome = new LogigramOutcome { BatteryId = 2 };

            Assert.AreEqual("absent BatteryId=2", LogigramOutcomeVisualizationObject.FormatDisplayText(outcome));
            Assert.IsFalse(
                LogigramOutcomeVisualizationObject.FormatDisplayText(outcome)
                    .IndexOf("Alpha", StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void LogigramOutcomeVisualizationObject_SetCurrentValue_UpdatesDisplayText()
        {
            var visualization = new LogigramOutcomeVisualizationObject();
            Assert.AreEqual(LogigramOutcomeVisualizationObject.EmptyDisplayText, visualization.DisplayText);

            DateTime now = DateTime.UtcNow;
            visualization.SetCurrentValue(
                new Message<LogigramOutcome>(
                    new LogigramOutcome { Regulation = LogigramRegulation.Gamma, BatteryId = 1 },
                    now,
                    now,
                    0,
                    0));

            Assert.AreEqual("Gamma BatteryId=1", visualization.DisplayText);

            visualization.SetCurrentValue(null);
            Assert.AreEqual(LogigramOutcomeVisualizationObject.EmptyDisplayText, visualization.DisplayText);
        }

        [TestMethod]
        public void LogigramStateVisualizationObject_BindsToLogigram1ContractType()
        {
            Type visualizationType = typeof(LogigramStateVisualizationObject);
            Type baseType = visualizationType.BaseType;

            Assert.IsNotNull(baseType);
            Assert.IsTrue(baseType.IsGenericType);
            Assert.AreEqual(typeof(StreamValueVisualizationObject<>), baseType.GetGenericTypeDefinition());
            Assert.AreEqual(typeof(LogigramState), baseType.GetGenericArguments()[0]);
            Assert.AreEqual("SAAC.Logigram1", typeof(LogigramState).Namespace);
        }

        [TestMethod]
        public void LogigramState_StreamNameConstant_IsLockedAd11Value()
        {
            Assert.AreEqual("Logigram1.State", LogigramState.StreamName);
        }

        [TestMethod]
        public void FormatDisplayText_NullCurrentValue_ReturnsHonestEmptyLabel()
        {
            Assert.AreEqual(
                LogigramStateVisualizationObject.EmptyDisplayText,
                LogigramStateVisualizationObject.FormatDisplayText(null));
            Assert.AreEqual("no state", LogigramStateVisualizationObject.FormatDisplayText(null));
        }

        [TestMethod]
        public void FormatDisplayText_WithStatus_ReturnsStatusWithoutInventingRegulation()
        {
            var state = new LogigramState { Status = "placeholder-status", BatteryId = 3 };

            Assert.AreEqual(
                "placeholder-status BatteryId=3",
                LogigramStateVisualizationObject.FormatDisplayText(state));
            Assert.AreEqual(
                " BatteryId=0",
                LogigramStateVisualizationObject.FormatDisplayText(new LogigramState { Status = string.Empty }));
        }

        [TestMethod]
        public void FormatDisplayText_IncludesBatteryId()
        {
            var state = new LogigramState { Status = "at-node", BatteryId = 12 };
            StringAssert.Contains(LogigramStateVisualizationObject.FormatDisplayText(state), "BatteryId=12");
        }

        [TestMethod]
        public void LogigramStateVisualizationObject_Unbound_DisplayTextIsEmptyLabel()
        {
            var visualization = new LogigramStateVisualizationObject();

            Assert.AreEqual(LogigramStateVisualizationObject.EmptyDisplayText, visualization.DisplayText);
        }

        [TestMethod]
        public void LogigramStateVisualizationObject_SetCurrentValue_UpdatesDisplayTextAndRaisesPropertyChanged()
        {
            var visualization = new LogigramStateVisualizationObject();
            var raised = new List<string>();
            visualization.PropertyChanged += (sender, e) => raised.Add(e.PropertyName);

            Assert.AreEqual(LogigramStateVisualizationObject.EmptyDisplayText, visualization.DisplayText);

            DateTime now = DateTime.UtcNow;
            var state = new LogigramState { Status = "placeholder-status", BatteryId = 5 };
            visualization.SetCurrentValue(new Message<LogigramState>(state, now, now, 0, 0));

            Assert.AreEqual("placeholder-status BatteryId=5", visualization.DisplayText);
            Assert.IsTrue(raised.Contains(nameof(LogigramStateVisualizationObject.DisplayText)));

            raised.Clear();
            visualization.SetCurrentValue(null);

            Assert.AreEqual(LogigramStateVisualizationObject.EmptyDisplayText, visualization.DisplayText);
            Assert.IsTrue(raised.Contains(nameof(LogigramStateVisualizationObject.DisplayText)));
        }

        [TestMethod]
        public void VisualizationsCsproj_DoesNotReferenceLogicUnitProjects()
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

            foreach (string reference in projectReferences.Concat(packageReferences))
            {
                foreach (string forbidden in ForbiddenLogicUnitProjectNameFragments)
                {
                    Assert.IsFalse(
                        reference.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0,
                        $"Visualizations must not reference Logic via ProjectReference/PackageReference '{reference}'.");
                }
            }
        }

        private static bool IsLogigram1ContractsReference(string include)
        {
            string normalized = include.Replace('/', '\\');
            return normalized.EndsWith("Logigram1\\Logigram1.csproj", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindVisualizationsCsproj()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(LogigramStateVisualizationObject).Assembly.Location)
                ?? Directory.GetCurrentDirectory();

            DirectoryInfo directory = new DirectoryInfo(assemblyDir);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "Components", "Visualizations", "Visualizations.csproj");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                // Walk up from UnitTests output toward the saac repo root.
                candidate = Path.Combine(directory.FullName, "Visualizations.csproj");
                if (File.Exists(candidate) && directory.Name.Equals("Visualizations", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            // Fallback: relative to this test source tree via AppDomain base / cwd.
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

            Assert.Fail("Could not locate Visualizations.csproj for dependency audit.");
            return null;
        }
    }
}
