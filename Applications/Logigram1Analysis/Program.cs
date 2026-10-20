// <copyright file="Program.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace Logigram1Analysis
{
    using System;

    /// <summary>
    /// Entry point for the Logigram 1 offline Casper Psi store console.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            return OfflineCasperStoreConsole.Run(args);
        }
    }
}
