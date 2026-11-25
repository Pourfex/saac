// <copyright file="DelayedStreamCopyResult.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace DelayedStreamCopy
{
    using System;

    /// <summary>
    /// Outcome of a successful <see cref="DelayedStreamCopier.Copy"/>.
    /// </summary>
    public sealed class DelayedStreamCopyResult
    {
        /// <summary>
        /// Gets or sets the number of messages written.
        /// </summary>
        public long MessageCount { get; set; }

        /// <summary>
        /// Gets or sets the first source originating time.
        /// </summary>
        public DateTime FirstSourceOriginatingTime { get; set; }

        /// <summary>
        /// Gets or sets the last source originating time.
        /// </summary>
        public DateTime LastSourceOriginatingTime { get; set; }

        /// <summary>
        /// Gets or sets the first written (shifted) originating time.
        /// </summary>
        public DateTime FirstOutputOriginatingTime { get; set; }

        /// <summary>
        /// Gets or sets the last written (shifted) originating time.
        /// </summary>
        public DateTime LastOutputOriginatingTime { get; set; }

        /// <summary>
        /// Gets or sets the output store root directory (contains <c>PostProcess.0000</c>).
        /// </summary>
        public string OutputStoreRoot { get; set; }
    }
}
