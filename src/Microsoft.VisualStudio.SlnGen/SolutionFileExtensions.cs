// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

using System;

namespace Microsoft.VisualStudio.SlnGen
{
    /// <summary>
    /// Represents a class that contains Visual Studio solution file extensions and format names.
    /// </summary>
    internal static class SolutionFileExtensions
    {
        /// <summary>
        /// Classic Visual Studio solution files (.sln).
        /// </summary>
        public const string Sln = ".sln";

        /// <summary>
        /// XML-based Visual Studio solution files (.slnx).
        /// Requires Visual Studio 17.13+ or .NET 9+ SDK to open.
        /// </summary>
        public const string Slnx = ".slnx";

        /// <summary>
        /// The format name for classic .sln files, as used by the --format CLI option and SlnGenFormat MSBuild property.
        /// </summary>
        public const string SlnFormatName = "sln";

        /// <summary>
        /// The format name for .slnx files, as used by the --format CLI option and SlnGenFormat MSBuild property.
        /// </summary>
        public const string SlnxFormatName = "slnx";

        /// <summary>
        /// Determines whether the specified path refers to a .slnx solution file.
        /// </summary>
        /// <param name="path">The file path to check.</param>
        /// <returns>true if the path ends with .slnx; otherwise, false.</returns>
        public static bool IsSlnxFile(string path)
        {
            return path != null && path.EndsWith(Slnx, StringComparison.OrdinalIgnoreCase);
        }
    }
}
