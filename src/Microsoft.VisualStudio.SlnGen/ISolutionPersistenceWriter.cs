// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

namespace Microsoft.VisualStudio.SlnGen
{
    /// <summary>
    /// Defines a writer that serializes a <see cref="SlnFile" /> solution model using
    /// the Microsoft.VisualStudio.SolutionPersistence library.
    /// <para>
    /// Currently implemented by <see cref="SlnxSolutionWriter" /> for .slnx output.
    /// A future implementation could handle .sln output as well, enabling full
    /// SolutionPersistence-based serialization for both formats.
    /// </para>
    /// </summary>
    internal interface ISolutionPersistenceWriter
    {
        /// <summary>
        /// Writes the specified solution to a file at the given path.
        /// </summary>
        /// <param name="solution">The solution model to write.</param>
        /// <param name="path">The full path to the output file.</param>
        /// <param name="useFolders">Whether to create hierarchical solution folders.</param>
        /// <param name="collapseFolders">Whether to collapse single-item folders into their parent.</param>
        /// <param name="logger">An optional logger for warnings.</param>
        void Write(SlnFile solution, string path, bool useFolders, bool collapseFolders, ISlnGenLogger logger = null);
    }
}
