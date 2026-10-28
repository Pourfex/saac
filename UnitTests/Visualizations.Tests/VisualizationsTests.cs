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

            Assert.AreEqual("Gamma BatteryId=7 PersonId=0", LogigramOutcomeVisualizationObject.FormatDisplayText(outcome));
        }

        [TestMethod]
        public void FormatDisplayText_Outcome_NullRegulation_ShowsAbsentNotAlpha()
        {
            var outcome = new LogigramOutcome { BatteryId = 2 };

            Assert.AreEqual("absent BatteryId=2 PersonId=0", LogigramOutcomeVisualizationObject.FormatDisplayText(outcome));
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
                    new LogigramOutcome { Regulation = LogigramRegulation.Gamma, BatteryId = 1, PersonId = 2 },
                    now,
                    now,
                    0,
                    0));

            Assert.AreEqual("Gamma BatteryId=1 PersonId=2", visualization.DisplayText);

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
            var state = new LogigramState { Status = "placeholder-status", BatteryId = 3, PersonId = 1 };

            Assert.AreEqual(
                "placeholder-status BatteryId=3 PersonId=1",
                LogigramStateVisualizationObject.FormatDisplayText(state));
            Assert.AreEqual(
                " BatteryId=0 PersonId=0",
                LogigramStateVisualizationObject.FormatDisplayText(new LogigramState { Status = string.Empty }));
        }

        [TestMethod]
        public void FormatDisplayText_IncludesBatteryIdAndPersonId()
        {
            var state = new LogigramState { Status = "at-node", BatteryId = 12, PersonId = 2 };
            string text = LogigramStateVisualizationObject.FormatDisplayText(state);
            StringAssert.Contains(text, "BatteryId=12");
            StringAssert.Contains(text, "PersonId=2");
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
            var state = new LogigramState { Status = "placeholder-status", BatteryId = 5, PersonId = 1 };
            visualization.SetCurrentValue(new Message<LogigramState>(state, now, now, 0, 0));

            Assert.AreEqual("placeholder-status BatteryId=5 PersonId=1", visualization.DisplayText);
            Assert.IsTrue(raised.Contains(nameof(LogigramStateVisualizationObject.DisplayText)));

            raised.Clear();
            visualization.SetCurrentValue(null);

            Assert.AreEqual(LogigramStateVisualizationObject.EmptyDisplayText, visualization.DisplayText);
            Assert.IsTrue(raised.Contains(nameof(LogigramStateVisualizationObject.DisplayText)));
        }

        [TestMethod]
        public void LogigramGraphVisualizationObject_HasVisualizationObjectAttribute()
        {
            VisualizationObjectAttribute attribute = typeof(LogigramGraphVisualizationObject)
                .GetCustomAttribute<VisualizationObjectAttribute>();

            Assert.IsNotNull(attribute);
            Assert.AreEqual("Logigram Graph", attribute.CommandText);
        }

        [TestMethod]
        public void LogigramGraphVisualizationObject_BindsToLogigram1StateContractType()
        {
            Type visualizationType = typeof(LogigramGraphVisualizationObject);
            Type baseType = visualizationType.BaseType;

            Assert.IsNotNull(baseType);
            Assert.IsTrue(baseType.IsGenericType);
            Assert.AreEqual(typeof(StreamValueVisualizationObject<>), baseType.GetGenericTypeDefinition());
            Assert.AreEqual(typeof(LogigramState), baseType.GetGenericArguments()[0]);
        }

        [TestMethod]
        public void LogigramGraph_GammaPathSkeleton_IsNodes1Through3AndC()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, LogigramGraphVisualizationObject.GammaPathNodeNumbers);
            Assert.AreEqual("C", LogigramGraphVisualizationObject.TerminalCLabel);
        }

        [TestMethod]
        public void TryParseNodeNumber_ParsesNode1AndNode2Prefixes()
        {
            Assert.AreEqual(1, LogigramGraphVisualizationObject.TryParseNodeNumber("node-1: Module status id=7 (new this Battery)"));
            Assert.AreEqual(2, LogigramGraphVisualizationObject.TryParseNodeNumber("node-2: door-closed + Validation (within 2s; source M1-Validation)"));
        }

        [TestMethod]
        public void TryParseNodeNumber_UnparseableOrEmpty_ReturnsNull()
        {
            Assert.IsNull(LogigramGraphVisualizationObject.TryParseNodeNumber(null));
            Assert.IsNull(LogigramGraphVisualizationObject.TryParseNodeNumber(string.Empty));
            Assert.IsNull(LogigramGraphVisualizationObject.TryParseNodeNumber("placeholder-status"));
            Assert.IsNull(LogigramGraphVisualizationObject.TryParseNodeNumber("at-node-1"));
        }

        [TestMethod]
        public void LogigramGraph_DualScrubColors_AreYellowAndOrange()
        {
            Assert.AreEqual("#FFCC00", LogigramGraphVisualizationObject.Person1HighlightColor);
            Assert.AreEqual("#E87A2E", LogigramGraphVisualizationObject.Person2HighlightColor);

            string csprojDir = Path.GetDirectoryName(FindVisualizationsCsproj());
            Assert.IsNotNull(csprojDir);
            string xamlPath = Path.Combine(
                csprojDir,
                "src",
                "Views",
                "LogigramGraphVisualizationObjectView.xaml");
            string xaml = File.ReadAllText(xamlPath);
            StringAssert.Contains(xaml, LogigramGraphVisualizationObject.Person1HighlightColor);
            StringAssert.Contains(xaml, LogigramGraphVisualizationObject.Person2HighlightColor);
            Assert.IsFalse(xaml.IndexOf("#E8B84A", StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void LogigramGraph_HappyScrub_PersonScopedNodeHighlights()
        {
            const int underView = 4;
            var state1 = new LogigramState
            {
                Status = "node-1: Module status id=1 (new this Battery)",
                BatteryId = 4,
                PersonId = 1,
            };
            Assert.IsTrue(LogigramGraphVisualizationObject.IsPathNodeHighlighted(1, state1, underView, personId: 1));
            Assert.IsFalse(LogigramGraphVisualizationObject.IsPathNodeHighlighted(1, state1, underView, personId: 2));
            Assert.IsFalse(LogigramGraphVisualizationObject.IsPathNodeHighlighted(2, state1, underView, personId: 1));

            var state2 = new LogigramState
            {
                Status = "node-2: door-closed + SelectModule (within 2s; source M1-SelectModule)",
                BatteryId = 4,
                PersonId = 1,
            };
            Assert.IsTrue(LogigramGraphVisualizationObject.IsPathNodeHighlighted(2, state2, underView, personId: 1));
            Assert.IsFalse(LogigramGraphVisualizationObject.IsPathNodeHighlighted(2, state2, underView, personId: 2));
        }

        [TestMethod]
        public void LogigramGraph_DualScrub_OnlyMatchingPersonHighlights()
        {
            var visualization = new LogigramGraphVisualizationObject { BatteryId = 4 };
            DateTime now = DateTime.UtcNow;

            visualization.SetCurrentValue(
                new Message<LogigramState>(
                    new LogigramState { Status = "node-1: hit", BatteryId = 4, PersonId = 1 },
                    now,
                    now,
                    0,
                    0));
            Assert.IsTrue(visualization.IsPerson1Node1Highlighted);
            Assert.IsFalse(visualization.IsPerson2Node1Highlighted);

            visualization.SetCurrentValue(
                new Message<LogigramState>(
                    new LogigramState { Status = "node-3: leave", BatteryId = 4, PersonId = 2 },
                    now,
                    now,
                    0,
                    0));
            Assert.IsTrue(visualization.IsPerson1Node1Highlighted);
            Assert.IsTrue(visualization.IsPerson2Node3Highlighted);
            Assert.IsFalse(visualization.IsPerson2Node1Highlighted);
            StringAssert.Contains(visualization.Person1DisplayText, "node-1: hit");
            StringAssert.Contains(visualization.Person2DisplayText, "node-3: leave");
        }

        [TestMethod]
        public void LogigramGraph_OtherBattery_DoesNotHighlight_HoldKeepsPrior()
        {
            var prior = new LogigramState { Status = "node-1: hit", BatteryId = 1, PersonId = 1 };
            var other = new LogigramState { Status = "node-2: other", BatteryId = 2, PersonId = 1 };
            DateTime t1 = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc);
            DateTime t2 = t1.AddSeconds(5);
            DateTime heldTime;
            LogigramState held = LogigramGraphVisualizationObject.HoldStateForPerson(
                prior,
                hasCursorValue: true,
                cursorMessageTime: t1,
                playhead: t1,
                underViewBatteryId: 1,
                personId: 1,
                previousHeld: null,
                previousHeldTime: DateTime.MinValue,
                out heldTime);
            held = LogigramGraphVisualizationObject.HoldStateForPerson(
                other,
                hasCursorValue: true,
                cursorMessageTime: t2,
                playhead: t2,
                underViewBatteryId: 1,
                personId: 1,
                previousHeld: held,
                previousHeldTime: heldTime,
                out heldTime);
            Assert.IsNotNull(held);
            Assert.AreEqual(1, held.BatteryId);
            Assert.AreEqual(t1, heldTime);
            Assert.IsTrue(LogigramGraphVisualizationObject.IsPathNodeHighlighted(1, held, underViewBatteryId: 1, personId: 1));
            Assert.IsFalse(LogigramGraphVisualizationObject.IsPathNodeHighlighted(1, other, underViewBatteryId: 1, personId: 1));
            Assert.IsFalse(
                LogigramGraphVisualizationObject.IsTerminalCHighlightedFromOutcome(
                    new LogigramOutcome { Regulation = LogigramRegulation.Gamma, BatteryId = 2, PersonId = 1 },
                    underViewBatteryId: 1,
                    personId: 1));
        }

        [TestMethod]
        public void LogigramGraph_HoldClearsWhenPlayheadRewindsBeforeHeldMessage()
        {
            var prior = new LogigramState { Status = "node-2: hit", BatteryId = 1, PersonId = 2 };
            DateTime tFire = new DateTime(2026, 1, 1, 0, 0, 10, DateTimeKind.Utc);
            DateTime tStart = tFire.AddSeconds(-9);
            DateTime heldTime;
            LogigramState held = LogigramGraphVisualizationObject.HoldStateForPerson(
                prior,
                hasCursorValue: true,
                cursorMessageTime: tFire,
                playhead: tFire,
                underViewBatteryId: 1,
                personId: 2,
                previousHeld: null,
                previousHeldTime: DateTime.MinValue,
                out heldTime);
            Assert.IsNotNull(held);

            held = LogigramGraphVisualizationObject.HoldStateForPerson(
                cursor: null,
                hasCursorValue: false,
                cursorMessageTime: DateTime.MinValue,
                playhead: tStart,
                underViewBatteryId: 1,
                personId: 2,
                previousHeld: held,
                previousHeldTime: heldTime,
                out heldTime);
            Assert.IsNull(held);
            Assert.AreEqual(DateTime.MinValue, heldTime);
        }

        [TestMethod]
        public void LogigramGraph_PartialCoverage_Node3NeverLitFromMissingStatus()
        {
            var state = new LogigramState { Status = "node-2: door-closed + Validation", BatteryId = 1, PersonId = 1 };
            Assert.IsFalse(LogigramGraphVisualizationObject.IsPathNodeHighlighted(3, state, underViewBatteryId: 1, personId: 1));
            Assert.IsFalse(
                LogigramGraphVisualizationObject.IsTerminalCHighlightedFromOutcome(null, underViewBatteryId: 1, personId: 1));
        }

        [TestMethod]
        public void LogigramGraph_UnparseableStatus_ShowsLabelWithoutPathHighlight()
        {
            var state = new LogigramState { Status = "waiting", BatteryId = 9, PersonId = 1 };
            Assert.AreEqual(
                "Battery 9 | P1: waiting | P2: " + LogigramGraphVisualizationObject.EmptyDisplayText,
                LogigramGraphVisualizationObject.FormatDisplayText(state, null, underViewBatteryId: 9));
            Assert.IsFalse(LogigramGraphVisualizationObject.IsPathNodeHighlighted(1, state, underViewBatteryId: 9, personId: 1));
        }

        [TestMethod]
        public void LogigramGraph_EmptyGap_HonestEmptyAndNoHighlights()
        {
            Assert.AreEqual(
                "Battery 1 | P1: " + LogigramGraphVisualizationObject.EmptyDisplayText
                    + " | P2: " + LogigramGraphVisualizationObject.EmptyDisplayText,
                LogigramGraphVisualizationObject.FormatDisplayText(null, null, underViewBatteryId: 1));
            Assert.IsFalse(LogigramGraphVisualizationObject.IsPathNodeHighlighted(1, null, underViewBatteryId: 1, personId: 1));
            Assert.IsFalse(LogigramGraphVisualizationObject.IsTerminalCHighlightedFromOutcome(null, underViewBatteryId: 1, personId: 1));

            var visualization = new LogigramGraphVisualizationObject();
            StringAssert.Contains(visualization.DisplayText, LogigramGraphVisualizationObject.EmptyDisplayText);
            Assert.IsFalse(visualization.IsPerson1Node1Highlighted);
            Assert.IsFalse(visualization.IsPerson2Node1Highlighted);
            Assert.IsFalse(visualization.IsPerson1TerminalCHighlighted);
            Assert.IsFalse(visualization.IsPerson2TerminalCHighlighted);
        }

        [TestMethod]
        public void LogigramGraph_OutcomeGamma_LightsTerminalC_PerPerson()
        {
            Assert.IsTrue(
                LogigramGraphVisualizationObject.IsTerminalCHighlightedFromOutcome(
                    new LogigramOutcome { Regulation = LogigramRegulation.Gamma, BatteryId = 3, PersonId = 1 },
                    underViewBatteryId: 3,
                    personId: 1));
            Assert.IsFalse(
                LogigramGraphVisualizationObject.IsTerminalCHighlightedFromOutcome(
                    new LogigramOutcome { Regulation = LogigramRegulation.Gamma, BatteryId = 3, PersonId = 1 },
                    underViewBatteryId: 3,
                    personId: 2));
            Assert.IsFalse(
                LogigramGraphVisualizationObject.IsTerminalCHighlightedFromOutcome(
                    new LogigramOutcome { Regulation = null, BatteryId = 3, PersonId = 1 },
                    underViewBatteryId: 3,
                    personId: 1));

            var visualization = new LogigramGraphVisualizationObject { BatteryId = 3 };
            visualization.SetCurrentOutcome(
                new LogigramOutcome { Regulation = LogigramRegulation.Gamma, BatteryId = 3, PersonId = 1 });
            Assert.IsTrue(visualization.IsPerson1TerminalCHighlighted);
            Assert.IsFalse(visualization.IsPerson2TerminalCHighlighted);

            visualization.SetCurrentOutcome(
                new LogigramOutcome { Regulation = LogigramRegulation.Gamma, BatteryId = 3, PersonId = 2 });
            Assert.IsTrue(visualization.IsPerson1TerminalCHighlighted);
            Assert.IsTrue(visualization.IsPerson2TerminalCHighlighted);

            visualization.SetCurrentOutcome(new LogigramOutcome { BatteryId = 3, PersonId = 2 });
            Assert.IsTrue(visualization.IsPerson1TerminalCHighlighted);
            Assert.IsFalse(visualization.IsPerson2TerminalCHighlighted);

            visualization.SetCurrentOutcome(null);
            Assert.IsFalse(visualization.IsPerson1TerminalCHighlighted);
            Assert.IsFalse(visualization.IsPerson2TerminalCHighlighted);
        }

        [TestMethod]
        public void LogigramGraph_SetCurrentValue_UpdatesHighlightsAndKeepsOtherPerson()
        {
            var visualization = new LogigramGraphVisualizationObject { BatteryId = 2 };
            DateTime now = DateTime.UtcNow;

            visualization.SetCurrentValue(
                new Message<LogigramState>(
                    new LogigramState { Status = "node-1: hit", BatteryId = 2, PersonId = 1 },
                    now,
                    now,
                    0,
                    0));

            Assert.IsTrue(visualization.IsPerson1Node1Highlighted);
            Assert.IsFalse(visualization.IsPerson1Node2Highlighted);
            Assert.IsFalse(visualization.IsPerson2Node1Highlighted);
            StringAssert.Contains(visualization.DisplayText, "Battery 2 |");

            visualization.SetCurrentValue(
                new Message<LogigramState>(
                    new LogigramState { Status = "node-2: hit", BatteryId = 2, PersonId = 1 },
                    now,
                    now,
                    0,
                    0));

            Assert.IsFalse(visualization.IsPerson1Node1Highlighted);
            Assert.IsTrue(visualization.IsPerson1Node2Highlighted);

            // Other Battery keeps last hold (scrub-friendly); clear only on unbind.
            visualization.SetCurrentValue(
                new Message<LogigramState>(
                    new LogigramState { Status = "node-1: other", BatteryId = 9, PersonId = 1 },
                    now,
                    now,
                    0,
                    0));
            Assert.IsFalse(visualization.IsPerson1Node1Highlighted);
            Assert.IsTrue(visualization.IsPerson1Node2Highlighted);
            StringAssert.Contains(visualization.Person1DisplayText, "node-2: hit");
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
