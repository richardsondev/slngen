// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.VisualStudio.SlnGen
{
    /// <summary>
    /// Writes a Visual Studio solution in the classic .sln text format.
    /// </summary>
    internal sealed class SlnSolutionWriter : ISolutionWriter
    {
        /// <inheritdoc />
        public string FileExtension => ".sln";

        /// <inheritdoc />
        public bool SupportsGuidPersistence => true;

        /// <inheritdoc />
        public void Write(SlnFile solution, string path, SolutionWriteOptions options)
        {
            string directoryName = Path.GetDirectoryName(path);

            if (!directoryName.IsNullOrWhiteSpace())
            {
                Directory.CreateDirectory(directoryName!);
            }

            using FileStream fileStream = File.Create(path);

            using StreamWriter writer = new StreamWriter(fileStream, Encoding.UTF8);

            WriteCore(solution, path, writer, options);
        }

        /// <summary>
        /// Writes the solution content to the specified <see cref="TextWriter" />.
        /// </summary>
        /// <param name="solution">The <see cref="SlnFile" /> containing the solution data.</param>
        /// <param name="rootPath">The root path used to compute relative project paths.</param>
        /// <param name="writer">The <see cref="TextWriter" /> to write the solution content to.</param>
        /// <param name="options">The <see cref="SolutionWriteOptions" /> controlling generation behavior.</param>
        internal static void WriteCore(SlnFile solution, string rootPath, TextWriter writer, SolutionWriteOptions options)
        {
            writer.WriteLine(SlnFile.Header, solution.FileFormatVersion);

            if (solution.VisualStudioVersion != null)
            {
                writer.WriteLine($"# Visual Studio Version {solution.VisualStudioVersion.Major}");
                writer.WriteLine($"VisualStudioVersion = {solution.VisualStudioVersion}");
                writer.WriteLine($"MinimumVisualStudioVersion = {solution.MinimumVisualStudioVersion}");
            }

            List<SlnProject> sortedProjects = solution.GetSortedProjects();

            foreach (SlnProject project in sortedProjects)
            {
                string solutionPath = project.FullPath.ToRelativePath(rootPath).ToSolutionPath();

                if (solution.ExistingProjectGuids != null && solution.ExistingProjectGuids.TryGetValue(solutionPath, out Guid existingProjectGuid))
                {
                    project.ProjectGuid = existingProjectGuid;
                }

                writer.WriteLine($@"Project(""{project.ProjectTypeGuid.ToSolutionString()}"") = ""{project.Name}"", ""{solutionPath}"", ""{project.ProjectGuid.ToSolutionString()}""");
                writer.WriteLine("EndProject");
            }

            SlnHierarchy hierarchy = solution.BuildHierarchy(sortedProjects, options.UseFolders, options.CollapseFolders);

            if (hierarchy != null)
            {
                bool logDriveWarning = false;
                string rootPathDrive = Path.GetPathRoot(Path.GetFullPath(rootPath));

                foreach (SlnFolder folder in hierarchy.Folders)
                {
                    bool useSeparateDrive = false;
                    bool hasFullPath = !string.IsNullOrEmpty(folder.FullPath);

                    if (hasFullPath)
                    {
                        string folderPathDrive = Path.GetPathRoot(Path.GetFullPath(folder.FullPath));

                        if (!string.IsNullOrEmpty(rootPathDrive) &&
                            rootPathDrive.Length == folderPathDrive.Length &&
                            !string.Equals(rootPathDrive, folderPathDrive, StringComparison.OrdinalIgnoreCase))
                        {
                            useSeparateDrive = true;

                            if (!logDriveWarning)
                            {
                                options.Logger?.LogWarning($"Detected folder on a different drive from the root solution path {rootPath}. This folder should not be committed to source control since it does not contain a simple, relative path and is not guaranteed to work across machines.");
                                logDriveWarning = true;
                            }
                        }
                    }

                    string projectSolutionPath = (options.UseFolders && !useSeparateDrive && hasFullPath ? folder.FullPath.ToRelativePath(rootPath) : folder.FullPath).ToSolutionPath();

                    if (solution.ExistingProjectGuids != null && solution.ExistingProjectGuids.TryGetValue(projectSolutionPath, out Guid projectGuid))
                    {
                        folder.FolderGuid = projectGuid;
                    }

                    if (folder != hierarchy.RootFolder)
                    {
                        writer.WriteLine($@"Project(""{folder.ProjectTypeGuidString}"") = ""{folder.Name}"", ""{projectSolutionPath}"", ""{folder.FolderGuid.ToSolutionString()}""");

                        if (folder.SolutionItems.Count > 0)
                        {
                            WriteSolutionItemsProjectSection(rootPath, writer, folder.SolutionItems);
                        }

                        writer.WriteLine("EndProject");
                    }
                    else if (folder.SolutionItems.Count > 0)
                    {
                        writer.WriteLine($@"Project(""{SlnFolder.FolderProjectTypeGuidString}"") = ""Solution Items"", ""Solution Items"", ""{{B283EBC2-E01F-412D-9339-FD56EF114549}}"" ");
                        WriteSolutionItemsProjectSection(rootPath, writer, folder.SolutionItems);
                        writer.WriteLine("EndProject");
                    }
                }
            }
            else
            {
                foreach (var solutionItems in solution.SolutionItemEntries)
                {
                    if (solutionItems.Value.SolutionItems.Any())
                    {
                        writer.WriteLine($@"Project(""{SlnFolder.FolderProjectTypeGuidString}"") = ""{solutionItems.Key}"", ""{solutionItems.Key}"", ""{solutionItems.Value.FolderGuid.ToSolutionString()}"" ");
                        WriteSolutionItemsProjectSection(rootPath, writer, solutionItems.Value.SolutionItems);
                        writer.WriteLine("EndProject");
                    }
                }

                var solutionItemsWithParents = solution.SolutionItemEntries.Where(x => x.Value.ParentFolderGuid.HasValue).ToArray();

                if (solutionItemsWithParents.Length > 0)
                {
                    writer.WriteLine(@"	GlobalSection(NestedProjects) = preSolution");

                    foreach (KeyValuePair<string, SlnItem> solutionItem in solutionItemsWithParents)
                    {
                        writer.WriteLine($@"		{solutionItem.Value.FolderGuid.ToSolutionString()} = {solutionItem.Value.ParentFolderGuid.Value.ToSolutionString()}");
                    }

                    writer.WriteLine("	EndGlobalSection");
                }
            }

            writer.WriteLine("Global");

            writer.WriteLine("	GlobalSection(SolutionConfigurationPlatforms) = preSolution");

            HashSet<string> solutionPlatforms = solution.GetSolutionPlatforms();
            HashSet<string> solutionConfigurations = solution.GetSolutionConfigurations();

            foreach (string configuration in solutionConfigurations)
            {
                foreach (string platform in solutionPlatforms)
                {
                    if (!string.IsNullOrWhiteSpace(configuration) && !string.IsNullOrWhiteSpace(platform))
                    {
                        writer.WriteLine($"		{configuration}|{platform} = {configuration}|{platform}");
                    }
                }
            }

            writer.WriteLine("	EndGlobalSection");

            writer.WriteLine("	GlobalSection(ProjectConfigurationPlatforms) = postSolution");

            bool hasSharedProject = false;

            foreach (SlnProject project in sortedProjects)
            {
                if (project.IsSharedProject)
                {
                    hasSharedProject = true;
                    continue;
                }

                string projectGuid = project.ProjectGuid.ToSolutionString();

                foreach (string configuration in solutionConfigurations)
                {
                    bool foundConfiguration = SlnFile.TryGetProjectSolutionConfiguration(configuration, project, options.AlwaysBuild, out string projectSolutionConfiguration);

                    foreach (string platform in solutionPlatforms)
                    {
                        bool foundPlatform = SlnFile.TryGetProjectSolutionPlatform(platform, project, out string projectSolutionPlatform, out string projectBuildPlatform);

                        writer.WriteLine($@"		{projectGuid}.{configuration}|{platform}.ActiveCfg = {projectSolutionConfiguration}|{projectSolutionPlatform}");

                        if (foundPlatform && foundConfiguration && project.IsBuildable)
                        {
                            writer.WriteLine($@"		{projectGuid}.{configuration}|{platform}.Build.0 = {projectSolutionConfiguration}|{projectBuildPlatform}");
                        }

                        if (project.IsDeployable)
                        {
                            writer.WriteLine($@"		{projectGuid}.{configuration}|{platform}.Deploy.0 = {projectSolutionConfiguration}|{projectSolutionPlatform}");
                        }
                    }
                }
            }

            writer.WriteLine("	EndGlobalSection");

            writer.WriteLine("	GlobalSection(SolutionProperties) = preSolution");
            writer.WriteLine("		HideSolutionNode = FALSE");
            writer.WriteLine("	EndGlobalSection");

            if (hierarchy != null)
            {
                var foldersWithParents = hierarchy.Folders.Where(i => i.Parent != null).ToArray();

                if (foldersWithParents.Length > 0)
                {
                    writer.WriteLine(@"	GlobalSection(NestedProjects) = preSolution");

                    foreach (SlnFolder folder in foldersWithParents)
                    {
                        foreach (SlnProject project in folder.Projects)
                        {
                            writer.WriteLine($@"		{project.ProjectGuid.ToSolutionString()} = {folder.FolderGuid.ToSolutionString()}");
                        }

                        if (folder.Parent != hierarchy.RootFolder)
                        {
                            writer.WriteLine($@"		{folder.FolderGuid.ToSolutionString()} = {folder.Parent.FolderGuid.ToSolutionString()}");
                        }
                    }

                    writer.WriteLine("	EndGlobalSection");
                }
            }

            writer.WriteLine("	GlobalSection(ExtensibilityGlobals) = postSolution");
            writer.WriteLine($"		SolutionGuid = {solution.SolutionGuid.ToSolutionString()}");
            writer.WriteLine("	EndGlobalSection");

            if (hasSharedProject)
            {
                writer.WriteLine("	GlobalSection(SharedMSBuildProjectFiles) = preSolution");

                foreach (SlnProject project in sortedProjects)
                {
                    foreach (string sharedProjectItem in project.SharedProjectItems)
                    {
                        writer.WriteLine($"		{sharedProjectItem.ToRelativePath(rootPath).ToSolutionPath()}*{project.ProjectGuid.ToSolutionString(uppercase: false).ToLowerInvariant()}*SharedItemsImports = {GetSharedProjectOptions(project)}");
                    }
                }

                writer.WriteLine("	EndGlobalSection");
            }

            writer.WriteLine("EndGlobal");
        }

        private static void WriteSolutionItemsProjectSection(string rootPath, TextWriter writer, IEnumerable<string> solutionItems)
        {
            writer.WriteLine("	ProjectSection(SolutionItems) = preProject");

            foreach (string solutionItem in solutionItems
                         .Select(i => i.ToRelativePath(rootPath).ToSolutionPath())
                         .Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                writer.WriteLine($"		{solutionItem} = {solutionItem}");
            }

            writer.WriteLine("	EndProjectSection");
        }

        private static string GetSharedProjectOptions(SlnProject project)
        {
            if (project.FullPath.EndsWith(ProjectFileExtensions.VcxItems))
            {
                return "9";
            }

            if (project.FullPath.EndsWith(ProjectFileExtensions.Shproj))
            {
                return "13";
            }

            return "4";
        }
    }
}
