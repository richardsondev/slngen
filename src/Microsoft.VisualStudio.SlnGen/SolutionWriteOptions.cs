// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

namespace Microsoft.VisualStudio.SlnGen
{
    /// <summary>
    /// Options that control how a solution file is written.
    /// </summary>
    internal sealed class SolutionWriteOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether solution folders should mirror the directory structure.
        /// </summary>
        public bool UseFolders { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether single-item folders should be collapsed into their parent.
        /// </summary>
        public bool CollapseFolders { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether projects should always be included in the build
        /// even when they have no matching configuration. Only used by the classic .sln format.
        /// </summary>
        public bool AlwaysBuild { get; set; } = true;

        /// <summary>
        /// Gets or sets the logger for diagnostic output.
        /// </summary>
        public ISlnGenLogger Logger { get; set; }
    }
}
