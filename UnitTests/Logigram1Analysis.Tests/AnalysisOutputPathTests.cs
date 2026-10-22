// <copyright file="AnalysisOutputPathTests.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis.Tests
{
    using System;
    using System.IO;
    using Logigram1Analysis;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// OUTPUT_HOME B resolve / validate helpers.
    /// </summary>
    [TestClass]
    public class AnalysisOutputPathTests
    {
        /// <summary>Empty override → sibling <c>&lt;name&gt;.Logigram1Analysis.pds</c>.</summary>
        [TestMethod]
        public void Resolve_WithoutOverride_UsesAutoSibling()
        {
            string input = Path.Combine(Path.GetTempPath(), "Casper5.pds");
            string resolved = AnalysisOutputPath.Resolve(input, null);
            Assert.AreEqual(
                Path.Combine(Path.GetTempPath(), "Casper5.Logigram1Analysis.pds"),
                resolved);
        }

        /// <summary>Non-empty absolute override wins.</summary>
        [TestMethod]
        public void Resolve_WithOverride_UsesOverrideFullPath()
        {
            string input = Path.Combine(Path.GetTempPath(), "Casper5.pds");
            string overridePath = Path.Combine(Path.GetTempPath(), "Custom", "Out.pds");
            string resolved = AnalysisOutputPath.Resolve(input, overridePath);
            Assert.AreEqual(Path.GetFullPath(overridePath), resolved);
        }

        /// <summary>Relative OutputPath resolves against the input <c>.pds</c> directory, not cwd.</summary>
        [TestMethod]
        public void Resolve_WithRelativeOverride_ResolvesAgainstInputDirectory()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1-relpath-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string input = Path.Combine(folder, "Casper5.pds");
            File.WriteAllText(input, "x");
            string previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Path.GetTempPath());
                string resolved = AnalysisOutputPath.Resolve(input, "CustomOut.pds");
                Assert.AreEqual(Path.Combine(folder, "CustomOut.pds"), resolved);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>Same path as input is rejected.</summary>
        [TestMethod]
        public void TryValidate_SameAsInput_Fails()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1-outpath-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string pds = Path.Combine(folder, "Same.pds");
            File.WriteAllText(pds, "x");
            try
            {
                Assert.IsFalse(AnalysisOutputPath.TryValidate(pds, pds, out string error));
                StringAssert.Contains(error, "overwrite");
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>Non-<c>.pds</c> OutputPath is rejected.</summary>
        [TestMethod]
        public void TryValidate_NonPdsExtension_Fails()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1-outpath-ext-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string input = Path.Combine(folder, "In.pds");
            File.WriteAllText(input, "x");
            string output = Path.Combine(folder, "Out.txt");
            try
            {
                Assert.IsFalse(AnalysisOutputPath.TryValidate(input, output, out string error));
                StringAssert.Contains(error, ".pds");
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>
        /// OutputPath whose store root collides with an existing Casper folder beside the input is rejected.
        /// </summary>
        [TestMethod]
        public void TryValidate_StoreRootCollidesWithExistingFolder_Fails()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1-outpath-collide-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string input = Path.Combine(folder, "Other.pds");
            File.WriteAllText(input, "x");
            Directory.CreateDirectory(Path.Combine(folder, "Server"));
            string output = Path.Combine(folder, "Server.pds");
            try
            {
                Assert.IsFalse(AnalysisOutputPath.TryValidate(input, output, out string error));
                StringAssert.Contains(error, "collide");
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>Writable sibling parent passes.</summary>
        [TestMethod]
        public void TryValidate_WritableSibling_Succeeds()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Logigram1-outpath-ok-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string input = Path.Combine(folder, "In.pds");
            File.WriteAllText(input, "x");
            string output = Path.Combine(folder, "In.Logigram1Analysis.pds");
            try
            {
                Assert.IsTrue(AnalysisOutputPath.TryValidate(input, output, out string error), error);
                Assert.IsNull(error);
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
