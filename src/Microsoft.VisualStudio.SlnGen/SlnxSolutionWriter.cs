// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Microsoft.VisualStudio.SlnGen
{
    /// <summary>
    /// Writes a Visual Studio solution in the XML-based .slnx format.
    /// </summary>
    internal sealed class SlnxSolutionWriter : ISolutionWriter
    {
        /// <inheritdoc />
        public string FileExtension => ".slnx";

        /// <inheritdoc />
        public bool SupportsGuidPersistence => false;

        /// <inheritdoc />
        public void Write(SlnFile solution, string path, SolutionWriteOptions options)
        {
            string directoryName = Path.GetDirectoryName(path);

            if (!directoryName.IsNullOrWhiteSpace())
            {
                Directory.CreateDirectory(directoryName!);
            }

            SolutionModel solutionModel = new SolutionModel();

            string rootPath = Path.GetFullPath(path);

            HashSet<string> solutionConfigurations = solution.GetSolutionConfigurations();

            foreach (string configuration in solutionConfigurations)
            {
                solutionModel.AddBuildType(configuration);
            }

            HashSet<string> solutionPlatforms = solution.GetSolutionPlatforms();

            foreach (string platform in solutionPlatforms)
            {
                solutionModel.AddPlatform(platform);
            }

            List<SlnProject> sortedProjects = solution.GetSortedProjects();

            SlnHierarchy hierarchy = solution.BuildHierarchy(sortedProjects, options.UseFolders, options.CollapseFolders);

            Dictionary<SlnFolder, SolutionFolderModel> folderMap = BuildFolderMap(solution, solutionModel, hierarchy, rootPath);

            Dictionary<SlnProject, SolutionFolderModel> projectFolderLookup = BuildProjectFolderLookup(folderMap);

            AddProjects(solutionModel, sortedProjects, projectFolderLookup, rootPath);

            SolutionSerializers.SlnXml.SaveAsync(path, solutionModel, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static Dictionary<SlnFolder, SolutionFolderModel> BuildFolderMap(
            SlnFile solution,
            SolutionModel solutionModel,
            SlnHierarchy hierarchy,
            string rootPath)
        {
            Dictionary<SlnFolder, SolutionFolderModel> folderMap = new Dictionary<SlnFolder, SolutionFolderModel>();

            if (hierarchy != null)
            {
                foreach (SlnFolder folder in hierarchy.Folders)
                {
                    if (folder == hierarchy.RootFolder)
                    {
                        if (folder.SolutionItems.Count > 0)
                        {
                            SolutionFolderModel solutionItemsFolder = solutionModel.AddFolder("/Solution Items/");

                            foreach (string item in folder.SolutionItems)
                            {
                                string relativePath = item.ToRelativePath(rootPath).Replace('\\', '/');

                                if (!string.IsNullOrWhiteSpace(relativePath))
                                {
                                    solutionItemsFolder.AddFile(relativePath);
                                }
                            }
                        }

                        continue;
                    }

                    // Build the full folder path by walking up the hierarchy to the root.
                    // This is necessary because hierarchy.Folders uses post-order traversal
                    // (children before parents), so parent folders may not be in folderMap yet.
                    string folderPath = BuildFolderPath(folder, hierarchy);
                    SolutionFolderModel slnxFolder = solutionModel.AddFolder(folderPath);

                    folderMap[folder] = slnxFolder;

                    foreach (string item in folder.SolutionItems)
                    {
                        string relativePath = item.ToRelativePath(rootPath).Replace('\\', '/');

                        if (!string.IsNullOrWhiteSpace(relativePath))
                        {
                            slnxFolder.AddFile(relativePath);
                        }
                    }
                }
            }
            else
            {
                foreach (var solutionItems in solution.SolutionItemEntries)
                {
                    if (solutionItems.Value.SolutionItems.Any())
                    {
                        SolutionFolderModel itemsFolder = solutionModel.AddFolder("/" + solutionItems.Key + "/");

                        foreach (string item in solutionItems.Value.SolutionItems)
                        {
                            string relativePath = item.ToRelativePath(rootPath).Replace('\\', '/');

                            if (!string.IsNullOrWhiteSpace(relativePath))
                            {
                                itemsFolder.AddFile(relativePath);
                            }
                        }
                    }
                }
            }

            return folderMap;
        }

        private static Dictionary<SlnProject, SolutionFolderModel> BuildProjectFolderLookup(
            Dictionary<SlnFolder, SolutionFolderModel> folderMap)
        {
            Dictionary<SlnProject, SolutionFolderModel> lookup = new Dictionary<SlnProject, SolutionFolderModel>();

            foreach (var kvp in folderMap)
            {
                foreach (SlnProject proj in kvp.Key.Projects)
                {
                    lookup[proj] = kvp.Value;
                }
            }

            return lookup;
        }

        private static void AddProjects(
            SolutionModel solutionModel,
            List<SlnProject> sortedProjects,
            Dictionary<SlnProject, SolutionFolderModel> projectFolderLookup,
            string rootPath)
        {
            // Pre-detect duplicate project names per target folder so we can disambiguate.
            // SolutionModel.AddProject derives the project name from the filename and throws
            // if two projects with the same name exist in the same folder.
            Dictionary<string, List<SlnProject>> projectsByFolderAndName = new Dictionary<string, List<SlnProject>>(StringComparer.OrdinalIgnoreCase);

            foreach (SlnProject project in sortedProjects)
            {
                if (project.IsSharedProject)
                {
                    continue;
                }

                string projectName = Path.GetFileNameWithoutExtension(project.FullPath);
                projectFolderLookup.TryGetValue(project, out SolutionFolderModel targetFolder);

                string folderKey = targetFolder?.Path ?? "<root>";
                string key = folderKey + "|" + projectName;

                if (!projectsByFolderAndName.TryGetValue(key, out List<SlnProject> group))
                {
                    group = new List<SlnProject>();
                    projectsByFolderAndName[key] = group;
                }

                group.Add(project);
            }

            HashSet<SlnProject> duplicateNameProjects = new HashSet<SlnProject>();

            foreach (var group in projectsByFolderAndName.Values)
            {
                if (group.Count > 1)
                {
                    foreach (SlnProject p in group)
                    {
                        duplicateNameProjects.Add(p);
                    }
                }
            }

            foreach (SlnProject project in sortedProjects)
            {
                if (project.IsSharedProject)
                {
                    continue;
                }

                string projectRelativePath = project.FullPath.ToRelativePath(rootPath);

                projectFolderLookup.TryGetValue(project, out SolutionFolderModel projectFolder);

                // For projects that share a filename within the same folder, create a
                // disambiguating sub-folder based on the project's ancestor directories so
                // the SolutionModel does not reject them as duplicates.
                if (duplicateNameProjects.Contains(project))
                {
                    string projectDir = Path.GetDirectoryName(project.FullPath);
                    string parentDir = Path.GetFileName(projectDir);
                    string grandParentDir = Path.GetFileName(Path.GetDirectoryName(projectDir));

                    // Use grandparent/parent to handle cases where duplicate projects
                    // also share the same immediate parent directory name.
                    string disambiguator = !string.IsNullOrWhiteSpace(grandParentDir)
                        ? grandParentDir + "/" + parentDir
                        : parentDir;

                    if (!string.IsNullOrWhiteSpace(disambiguator))
                    {
                        string subFolderPath = projectFolder != null
                            ? projectFolder.Path + disambiguator + "/"
                            : "/" + disambiguator + "/";
                        projectFolder = solutionModel.AddFolder(subFolderPath);
                    }
                }

                SolutionProjectModel slnxProject = solutionModel.AddProject(projectRelativePath, null, projectFolder);

                if (!string.IsNullOrWhiteSpace(project.Name) &&
                    !string.Equals(project.Name, Path.GetFileNameWithoutExtension(project.FullPath), StringComparison.OrdinalIgnoreCase))
                {
                    slnxProject.DisplayName = project.Name;
                }
            }
        }

        /// <summary>
        /// Builds the full folder path by walking up the hierarchy to the root.
        /// Returns a path like "/src/Api/" for a folder named "Api" whose parent is "src".
        /// </summary>
        private static string BuildFolderPath(SlnFolder folder, SlnHierarchy hierarchy)
        {
            Stack<string> parts = new Stack<string>();
            SlnFolder current = folder;

            while (current != null && current != hierarchy.RootFolder)
            {
                parts.Push(current.Name);
                current = current.Parent;
            }

            return "/" + string.Join("/", parts) + "/";
        }
    }
}
