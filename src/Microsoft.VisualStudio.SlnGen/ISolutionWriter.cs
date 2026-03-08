// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

namespace Microsoft.VisualStudio.SlnGen
{
    /// <summary>
    /// Writes a Visual Studio solution file in a specific format (.sln or .slnx).
    /// </summary>
    internal interface ISolutionWriter
    {
        /// <summary>
        /// Gets the file extension for this format (e.g. ".sln", ".slnx").
        /// </summary>
        string FileExtension { get; }

        /// <summary>
        /// Gets a value indicating whether this format supports reading an existing
        /// solution to preserve project GUIDs across regenerations.
        /// </summary>
        bool SupportsGuidPersistence { get; }

        /// <summary>
        /// Writes the solution to the specified path.
        /// </summary>
        /// <param name="solution">The <see cref="SlnFile" /> containing the solution data.</param>
        /// <param name="path">The full path to the output file.</param>
        /// <param name="options">The <see cref="SolutionWriteOptions" /> controlling generation behavior.</param>
        void Write(SlnFile solution, string path, SolutionWriteOptions options);
    }
}
