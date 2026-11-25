// <copyright file="DelayedStreamCopyException.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace DelayedStreamCopy
{
    using System;

    /// <summary>
    /// Failure that must reach the user as a non-zero exit with a clear message (missing/empty stream, unsafe output path, ...).
    /// </summary>
    public sealed class DelayedStreamCopyException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DelayedStreamCopyException"/> class.
        /// </summary>
        /// <param name="message">User-facing message.</param>
        public DelayedStreamCopyException(string message)
            : base(message)
        {
        }
    }
}
