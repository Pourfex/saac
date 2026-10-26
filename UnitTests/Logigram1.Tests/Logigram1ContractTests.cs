// <copyright file="Logigram1ContractTests.cs" company="SAAC">
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
    using SAAC.Logigram1;

    /// <summary>
    /// Contract surface tests for Logigram1 State / Outcome / alphabet.
    /// </summary>
    [TestClass]
    public class Logigram1ContractTests
    {
        /// <summary>AD-11 state stream name stays locked.</summary>
        [TestMethod]
        public void LogigramState_StreamName_RemainsLockedAd11Value()
        {
            Assert.AreEqual("Logigram1.State", LogigramState.StreamName);
        }

        /// <summary>OUTCOME_SHAPE B uses a separate Outcome stream name.</summary>
        [TestMethod]
        public void LogigramOutcome_StreamName_IsSeparateOutcomeStream()
        {
            Assert.AreEqual("Logigram1.Outcome", LogigramOutcome.StreamName);
            Assert.AreNotEqual(LogigramState.StreamName, LogigramOutcome.StreamName);
        }

        /// <summary>Evidence stub is neither State nor Outcome.</summary>
        [TestMethod]
        public void LogigramEvidence_StreamName_IsNotOutcomeOrState()
        {
            Assert.AreEqual("Logigram1.Evidence", LogigramEvidence.StreamName);
            Assert.AreNotEqual(LogigramOutcome.StreamName, LogigramEvidence.StreamName);
            Assert.AreNotEqual(LogigramState.StreamName, LogigramEvidence.StreamName);
        }

        /// <summary>Alphabet is Alpha | Beta | Gamma | N/A | Apprentissage.</summary>
        [TestMethod]
        public void LogigramRegulation_Alphabet_MatchesAd10Contract()
        {
            LogigramRegulation[] expected =
            {
                LogigramRegulation.Alpha,
                LogigramRegulation.Beta,
                LogigramRegulation.Gamma,
                LogigramRegulation.NA,
                LogigramRegulation.Apprentissage,
            };

            LogigramRegulation[] actual = Enum.GetValues(typeof(LogigramRegulation))
                .Cast<LogigramRegulation>()
                .ToArray();

            CollectionAssert.AreEquivalent(expected, actual);
            Assert.AreEqual("Alpha", LogigramRegulationLabels.ToContractLabel(LogigramRegulation.Alpha));
            Assert.AreEqual("Beta", LogigramRegulationLabels.ToContractLabel(LogigramRegulation.Beta));
            Assert.AreEqual("Gamma", LogigramRegulationLabels.ToContractLabel(LogigramRegulation.Gamma));
            Assert.AreEqual("N/A", LogigramRegulationLabels.ToContractLabel(LogigramRegulation.NA));
            Assert.AreEqual("Apprentissage", LogigramRegulationLabels.ToContractLabel(LogigramRegulation.Apprentissage));
        }

        /// <summary>State is progress + BatteryId (AD-6); no regulation or BatteryIndex fork.</summary>
        [TestMethod]
        public void LogigramState_RequiresBatteryId_NoRegulationOrBatteryIndex()
        {
            var names = typeof(LogigramState).GetProperties()
                .Select(property => property.Name)
                .ToList();

            CollectionAssert.DoesNotContain(names, "Regulation");
            Assert.IsTrue(names.Contains("Status"));
            Assert.IsTrue(names.Contains("BatteryId"));
            CollectionAssert.DoesNotContain(names, "BatteryIndex");
            Assert.AreEqual(typeof(int), typeof(LogigramState).GetProperty(nameof(LogigramState.BatteryId)).PropertyType);
        }

        /// <summary>Outcome carries regulation and BatteryId; no BatteryIndex fork.</summary>
        [TestMethod]
        public void LogigramOutcome_RequiresBatteryId_NoBatteryIndex()
        {
            var names = typeof(LogigramOutcome).GetProperties()
                .Select(property => property.Name)
                .ToList();

            Assert.IsTrue(names.Contains("Regulation"));
            Assert.IsTrue(names.Contains("BatteryId"));
            CollectionAssert.DoesNotContain(names, "BatteryIndex");
            Assert.AreEqual(typeof(int), typeof(LogigramOutcome).GetProperty(nameof(LogigramOutcome.BatteryId)).PropertyType);
        }

        /// <summary>Evidence carries BatteryId (AD-6); no BatteryIndex fork.</summary>
        [TestMethod]
        public void LogigramEvidence_RequiresBatteryId_NoBatteryIndex()
        {
            var names = typeof(LogigramEvidence).GetProperties()
                .Select(property => property.Name)
                .ToList();

            Assert.IsTrue(names.Contains("NodeOrPathId"));
            Assert.IsTrue(names.Contains("BatteryId"));
            CollectionAssert.DoesNotContain(names, "BatteryIndex");
            Assert.AreEqual(typeof(int), typeof(LogigramEvidence).GetProperty(nameof(LogigramEvidence.BatteryId)).PropertyType);
        }

        /// <summary>Default Outcome must not invent Alpha via non-nullable enum default.</summary>
        [TestMethod]
        public void LogigramOutcome_DefaultRegulation_IsNullNotAlpha()
        {
            var outcome = new LogigramOutcome();
            Assert.IsNull(outcome.Regulation);
            Assert.AreEqual(typeof(LogigramRegulation?), typeof(LogigramOutcome).GetProperty(nameof(LogigramOutcome.Regulation)).PropertyType);
        }

        /// <summary>ToString feeds PsiStudio Messages legend / Outcome viz without inventing regulation.</summary>
        [TestMethod]
        public void LogigramOutcome_ToString_ShowsRegulationOrAbsentAndBatteryId()
        {
            Assert.AreEqual("absent BatteryId=0", new LogigramOutcome().ToString());
            Assert.AreEqual(
                "Gamma BatteryId=42",
                new LogigramOutcome { Regulation = LogigramRegulation.Gamma, BatteryId = 42 }.ToString());
            Assert.AreEqual(
                "N/A BatteryId=1",
                new LogigramOutcome { Regulation = LogigramRegulation.NA, BatteryId = 1 }.ToString());
        }

        /// <summary>Contracts project must stay Psi-free (Additional Assemblies safe).</summary>
        [TestMethod]
        public void Logigram1Csproj_HasNoMicrosoftPsiPackageReference()
        {
            string csprojPath = FindLogigram1Csproj();
            XDocument document = XDocument.Load(csprojPath);
            XNamespace msbuild = document.Root?.Name.Namespace ?? XNamespace.None;

            var packageIncludes = document
                .Descendants(msbuild + "PackageReference")
                .Select(element => (string)element.Attribute("Include") ?? string.Empty)
                .ToList();

            foreach (string include in packageIncludes)
            {
                Assert.IsFalse(
                    include.StartsWith("Microsoft.Psi", StringComparison.OrdinalIgnoreCase),
                    $"Logigram1 contracts must not reference Psi package '{include}'.");
            }
        }

        private static string FindLogigram1Csproj()
        {
            string[] relativeCandidates =
            {
                Path.Combine("Components", "Logigram1", "Logigram1.csproj"),
                Path.Combine("..", "..", "..", "..", "Components", "Logigram1", "Logigram1.csproj"),
                Path.Combine("..", "..", "..", "..", "..", "Components", "Logigram1", "Logigram1.csproj"),
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
                string candidate = Path.Combine(directory.FullName, "Components", "Logigram1", "Logigram1.csproj");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Could not locate Logigram1.csproj for Psi-free audit.");
            return null;
        }
    }
}
