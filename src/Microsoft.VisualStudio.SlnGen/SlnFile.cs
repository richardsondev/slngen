// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

using Microsoft.Build.Evaluation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.VisualStudio.SlnGen
{
    /// <summary>
    /// Represents a Visual Studio solution file.
    /// </summary>
    public sealed class SlnFile
    {
        /// <summary>
        /// The solution header.
        /// </summary>
        internal const string Header = "Microsoft Visual Studio Solution File, Format Version {0}";

        /// <summary>
        /// The beginning of the line that ends a global section.
        /// </summary>
        private const string GlobalSectionEnd = "\tEndGlobalSection";

        /// <summary>
        /// The beginning of the line that starts the extensibility global section.
        /// </summary>
        private const string GlobalSectionStartExtensibilityGlobals = "\tGlobalSection(ExtensibilityGlobals)";

        /// <summary>
        /// The beginning of the line that ends project information.
        /// </summary>
        private const string ProjectSectionEnd = "EndProject";

        /// <summary>
        /// The beginning of the line that contains project information.
        /// </summary>
        private const string ProjectSectionStart = "Project(\"";

        /// <summary>
        /// The beginning of the line that contains the solution GUID.
        /// </summary>
        private const string SectionSettingSolutionGuid = "\t\tSolutionGuid = ";

        /// <summary>
        /// A regular expression used to parse the project section.
        /// </summary>
        private static readonly Regex GuidRegex = new (@"(?<Guid>\{[0-9a-fA-F\-]+\})");

        /// <summary>
        /// The separator to split project information by.
        /// </summary>
        private static readonly string[] ProjectSectionSeparator = { "\", \"" };

        /// <summary>
        /// The file format version.
        /// </summary>
        private readonly string _fileFormatVersion;

        /// <summary>
        /// Gets the projects.
        /// </summary>
        private readonly List<SlnProject> _projects = new ();

        /// <summary>
        /// A list of absolute paths to include as Solution Items.
        /// </summary>
        private readonly Dictionary<string, SlnItem> _solutionItems = new ();

        /// <summary>
        /// Initializes a new instance of the <see cref="SlnFile" /> class.
        /// </summary>
        /// <param name="fileFormatVersion">The file format version.</param>
        public SlnFile(string fileFormatVersion)
        {
            _fileFormatVersion = fileFormatVersion;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SlnFile" /> class.
        /// </summary>
        public SlnFile()
            : this("12.00")
        {
        }

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyCollection{String}" /> of Configuration values to use.
        /// </summary>
        public IReadOnlyCollection<string> Configurations { get; set; }

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyDictionary{TKey,TValue}" /> containing any existing project GUIDs to re-use.
        /// </summary>
        public IReadOnlyDictionary<string, Guid> ExistingProjectGuids { get; set; }

        /// <summary>
        /// Gets or sets an optional minimum Visual Studio version for the solution file.
        /// </summary>
        public string MinimumVisualStudioVersion { get; set; } = "10.0.40219.1";

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyCollection{String}" /> of Platform values to use.
        /// </summary>
        public IReadOnlyCollection<string> Platforms { get; set; }

        /// <summary>
        /// Gets or sets a <see cref="Guid" /> for the solution file.
        /// </summary>
        public Guid SolutionGuid { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Gets a list of solution items.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> SolutionItems => _solutionItems.ToDictionary(
            k => k.Key,
            v => (IReadOnlyCollection<string>)v.Value.SolutionItems.AsReadOnly());

        /// <summary>
        /// Gets or sets an optional Visual Studio version for the solution file.
        /// </summary>
        public Version VisualStudioVersion { get; set; }

        /// <summary>
        /// Gets the file format version string.
        /// </summary>
        internal string FileFormatVersion => _fileFormatVersion;

        /// <summary>
        /// Gets the solution item entries keyed by folder name.
        /// </summary>
        internal IReadOnlyDictionary<string, SlnItem> SolutionItemEntries => _solutionItems;

        /// <summary>
        /// Generates a solution file.
        /// </summary>
        /// <param name="arguments">The current <see cref="ProgramArguments" />.</param>
        /// <param name="projects">A <see cref="IEnumerable{String}" /> containing the entry projects.</param>
        /// <param name="logger">A <see cref="ISlnGenLogger" /> to use for logging.</param>
        /// <returns>A <see cref="Tuple{String, Int32, Int32, Guid}" /> with the full path to the solution file, the count of custom project type GUIDs used, the count of solution items, and the solution GUID.</returns>
        public static (string solutionFileFullPath, int customProjectTypeGuidCount, int solutionItemCount, Guid solutionGuid) GenerateSolutionFile(ProgramArguments arguments, IEnumerable<Project> projects, ISlnGenLogger logger)
        {
            List<Project> projectList = projects.ToList();

            Project firstProject = projectList.First();

            IReadOnlyDictionary<string, Guid> customProjectTypeGuids = SlnProject.GetCustomProjectTypeGuids(firstProject);

            IReadOnlyCollection<string> solutionItems = SlnProject.GetSolutionItems(projectList, logger).ToList();

            string solutionFileFullPath = arguments.SolutionFileFullPath?.LastOrDefault();

            if (solutionFileFullPath.IsNullOrWhiteSpace())
            {
                string solutionDirectoryFullPath = arguments.SolutionDirectoryFullPath?.LastOrDefault();

                if (solutionDirectoryFullPath.IsNullOrWhiteSpace())
                {
                    solutionDirectoryFullPath = firstProject.DirectoryPath;
                }

                var firstProjectName = firstProject.GetPropertyValueOrDefault(MSBuildPropertyNames.SlnGenProjectName, Path.GetFileName(firstProject.FullPath));

                string slnGenUseSlnxPropertyValue = firstProject.GetPropertyValueOrDefault(MSBuildPropertyNames.SlnGenUseSlnx, "false");
                bool useSlnx = arguments.EnableSlnx(slnGenUseSlnxPropertyValue);
                string solutionFileName = Path.ChangeExtension(firstProjectName, useSlnx ? "slnx" : "sln");

                solutionFileFullPath = Path.Combine(solutionDirectoryFullPath!, solutionFileName);
            }

            logger.LogMessageHigh($"Generating Visual Studio solution \"{Path.GetFullPath(solutionFileFullPath)}\" ...");

            if (customProjectTypeGuids.Count > 0)
            {
                logger.LogMessageLow("Custom Project Type GUIDs:");
                foreach (KeyValuePair<string, Guid> item in customProjectTypeGuids)
                {
                    logger.LogMessageLow("  {0} = {1}", item.Key, item.Value);
                }
            }

            SlnFile solution = new SlnFile
            {
                Platforms = arguments.GetPlatforms(),
                Configurations = arguments.GetConfigurations(),
            };

            if (arguments.VisualStudioVersion.HasValue)
            {
                if (arguments.VisualStudioVersion.Version != null && Version.TryParse(arguments.VisualStudioVersion.Version, out Version version))
                {
                    solution.VisualStudioVersion = version;
                }

                if (solution.VisualStudioVersion == null)
                {
                    string devEnvFullPath = arguments.GetDevEnvFullPath(Program.CurrentDevelopmentEnvironment.VisualStudio);

                    if (!devEnvFullPath.IsNullOrWhiteSpace() && File.Exists(devEnvFullPath))
                    {
                        FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(devEnvFullPath);

                        solution.VisualStudioVersion = new Version(fileVersionInfo.ProductMajorPart, fileVersionInfo.ProductMinorPart, fileVersionInfo.ProductBuildPart, fileVersionInfo.FilePrivatePart);
                    }
                }
            }

            if (TryParseExistingSolution(solutionFileFullPath, out Guid solutionGuid, out IReadOnlyDictionary<string, Guid> projectGuidsByPath))
            {
                logger.LogMessageNormal("Updating existing solution file and reusing Visual Studio cache");

                solution.SolutionGuid = solutionGuid;
                solution.ExistingProjectGuids = projectGuidsByPath;

                arguments.LoadProjectsInVisualStudio = new[] { bool.TrueString };
            }

            bool isBuildable = true;
            if (arguments.GetGlobalProperties().TryGetValue(MSBuildPropertyNames.SlnGenIsBuildable, out string isBuildableString))
            {
                isBuildable = bool.TrueString.Equals(isBuildableString, StringComparison.OrdinalIgnoreCase);
            }

            solution.AddProjects(projectList, customProjectTypeGuids, arguments.IgnoreMainProject ? null : firstProject.FullPath, isBuildable);

            solution.AddSolutionItems(solutionItems);

            string slnGenFoldersPropertyValue = firstProject.GetPropertyValueOrDefault(MSBuildPropertyNames.SlnGenFolders, "false");
            var enableFolders = arguments.EnableFolders(slnGenFoldersPropertyValue);

            ISolutionWriter writer = CreateWriter(solutionFileFullPath);

            if (!logger.HasLoggedErrors)
            {
                writer.Write(
                    solution,
                    solutionFileFullPath,
                    new SolutionWriteOptions
                    {
                        UseFolders = enableFolders,
                        CollapseFolders = arguments.EnableCollapseFolders(),
                        AlwaysBuild = arguments.EnableAlwaysBuild(),
                        Logger = logger,
                    });
            }

            return (solutionFileFullPath, customProjectTypeGuids.Count, solutionItems.Count, solution.SolutionGuid);
        }

        /// <summary>
        /// Attempts to read the existing GUID from a solution file if one exists.
        /// </summary>
        /// <param name="path">The path to a solution file.</param>
        /// <param name="solutionGuid">Receives the <see cref="Guid" /> of the existing solution file if one is found, otherwise default(Guid).</param>
        /// <param name="projectGuidsByPath">Receives the project GUIDs by their full paths.</param>
        /// <returns>true if the solution GUID was found, otherwise false.</returns>
        public static bool TryParseExistingSolution(string path, out Guid solutionGuid, out IReadOnlyDictionary<string, Guid> projectGuidsByPath)
        {
            solutionGuid = default;
            projectGuidsByPath = default;

            // .slnx files don't use project GUIDs, so skip parsing for them
            if (path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool foundSolutionGuid = false;

            Dictionary<string, Guid> projectGuids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            FileInfo fileInfo = new FileInfo(path);

            if (!fileInfo.Exists || fileInfo.Directory == null)
            {
                return false;
            }

            using FileStream stream = File.OpenRead(path);
            using StreamReader reader = new StreamReader(stream, Encoding.GetEncoding(0), detectEncodingFromByteOrderMarks: true);

            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith(ProjectSectionStart))
                {
                    string[] projectDetails = line.Split(ProjectSectionSeparator, StringSplitOptions.RemoveEmptyEntries);

                    if (projectDetails.Length == 3)
                    {
                        Match projectGuidMatch = GuidRegex.Match(projectDetails[2]);

                        if (!projectGuidMatch.Groups["Guid"].Success)
                        {
                            continue;
                        }

                        string projectGuidString = projectGuidMatch.Groups["Guid"].Value;

                        Match projectTypeGuidMatch = GuidRegex.Match(projectDetails[0]);

                        if (!projectTypeGuidMatch.Groups["Guid"].Success)
                        {
                            continue;
                        }

                        if (!Guid.TryParse(projectGuidString, out Guid projectGuid) || !Guid.TryParse(projectTypeGuidMatch.Groups["Guid"].Value, out Guid projectTypeGuid))
                        {
                            continue;
                        }

                        string projectPath = projectDetails[1].Trim().Trim('\"');

                        projectGuids[projectPath] = projectGuid;
                    }

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith(ProjectSectionEnd))
                        {
                            break;
                        }
                    }
                }

                if (line != null && line.StartsWith(GlobalSectionStartExtensibilityGlobals))
                {
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith(SectionSettingSolutionGuid))
                        {
                            string solutionGuidString = line.Substring(SectionSettingSolutionGuid.Length);

                            foundSolutionGuid = Guid.TryParse(solutionGuidString, out solutionGuid);
                        }

                        if (line.StartsWith(GlobalSectionEnd))
                        {
                            break;
                        }
                    }
                }
            }

            projectGuidsByPath = projectGuids;

            return foundSolutionGuid;
        }

        /// <summary>
        /// Adds the specified projects.
        /// </summary>
        /// <param name="projects">An <see cref="IEnumerable{SlnProject}"/> containing projects to add to the solution.</param>
        public void AddProjects(IEnumerable<SlnProject> projects)
        {
            _projects.AddRange(projects);
        }

        /// <summary>
        /// Adds the specified projects to the solution file.
        /// </summary>
        /// <param name="projects">An <see cref="IEnumerable{T}" /> of projects to add.</param>
        /// <param name="customProjectTypeGuids">An <see cref="IReadOnlyDictionary{TKey,TValue}" /> containing any custom project type GUIDs to use.</param>
        /// <param name="mainProjectFullPath">Optional full path to the main project.</param>
        /// <param name="isBuildable">Indicates whether the projects are buildable.</param>
        public void AddProjects(IEnumerable<Project> projects, IReadOnlyDictionary<string, Guid> customProjectTypeGuids, string mainProjectFullPath = null, bool isBuildable = true)
        {
            _projects.AddRange(
                projects
                    .Distinct(new EqualityComparer<Project>((x, y) => string.Equals(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase), i => i.FullPath.GetHashCode()))
                    .Select(i => SlnProject.FromProject(i, customProjectTypeGuids, string.Equals(i.FullPath, mainProjectFullPath, StringComparison.OrdinalIgnoreCase), isBuildable))
                    .Where(i => i != null));
        }

        /// <summary>
        /// Adds the specified solution items.
        /// </summary>
        /// <param name="items">An <see cref="IEnumerable{String}"/> containing items to add to the solution.</param>
        public void AddSolutionItems(IEnumerable<string> items)
        {
            AddSolutionItems("Solution Items", items);
        }

        /// <summary>
        /// Adds the specified solution items under the specified path.
        /// </summary>
        /// <param name="folderPath">The path the solution items will be added in.</param>
        /// <param name="items">An <see cref="IEnumerable{String}"/> containing items to add to the solution.</param>
        public void AddSolutionItems(string folderPath, IEnumerable<string> items)
        {
            AddSolutionItems(folderPath, new Guid("B283EBC2-E01F-412D-9339-FD56EF114549"), items);
        }

        /// <summary>
        /// Adds the specified solution items under the specified path.
        /// </summary>
        /// <param name="folderPath">The path the solution items will be added in.</param>
        /// <param name="folderGuid">The unique GUID for the folder.</param>
        /// <param name="items">An <see cref="IEnumerable{String}"/> containing items to add to the solution.</param>
        public void AddSolutionItems(string folderPath, Guid folderGuid, IEnumerable<string> items)
        {
            AddSolutionItems(null, folderPath, folderGuid, items);
        }

        /// <summary>
        /// Adds the specified solution items under the specified path.
        /// </summary>
        /// <param name="parentFolderGuid">The unique GUID for the parent folder.</param>
        /// <param name="folderPath">The path the solution items will be added in.</param>
        /// <param name="folderGuid">The unique GUID for the folder.</param>
        /// <param name="items">An <see cref="IEnumerable{String}"/> containing items to add to the solution.</param>
        public void AddSolutionItems(Guid? parentFolderGuid, string folderPath, Guid folderGuid, IEnumerable<string> items)
        {
            _solutionItems.Add(folderPath, new SlnItem(parentFolderGuid, folderGuid, items));
        }

        /// <summary>
        /// Saves the Visual Studio solution to a file.
        /// </summary>
        /// <param name="path">The full path to the file to write to.</param>
        /// <param name="useFolders">Specifies if folders should be created.</param>
        /// <param name="logger">A <see cref="ISlnGenLogger" /> to use for logging.</param>
        /// <param name="collapseFolders">An optional value indicating whether or not folders containing a single item should be collapsed into their parent folder.</param>
        /// <param name="alwaysBuild">An optional value indicating whether or not to always include the project in the build even if it has no matching configuration.</param>
        public void Save(string path, bool useFolders, ISlnGenLogger logger = null, bool collapseFolders = false, bool alwaysBuild = true)
        {
            new SlnSolutionWriter().Write(
                this,
                path,
                new SolutionWriteOptions
                {
                    UseFolders = useFolders,
                    CollapseFolders = collapseFolders,
                    AlwaysBuild = alwaysBuild,
                    Logger = logger,
                });
        }

        /// <summary>
        /// Saves the Visual Studio solution as a .slnx (XML-based) file.
        /// </summary>
        /// <param name="path">The full path to the .slnx file to write to.</param>
        /// <param name="useFolders">Specifies if folders should be created.</param>
        /// <param name="logger">A <see cref="ISlnGenLogger" /> to use for logging.</param>
        /// <param name="collapseFolders">An optional value indicating whether or not folders containing a single item should be collapsed into their parent folder.</param>
        public void SaveSlnx(string path, bool useFolders, ISlnGenLogger logger = null, bool collapseFolders = false)
        {
            new SlnxSolutionWriter().Write(
                this,
                path,
                new SolutionWriteOptions
                {
                    UseFolders = useFolders,
                    CollapseFolders = collapseFolders,
                    Logger = logger,
                });
        }

        /// <summary>
        /// Creates an <see cref="ISolutionWriter" /> appropriate for the specified output path.
        /// </summary>
        /// <param name="path">The solution file path (used to determine the format from the extension).</param>
        /// <returns>An <see cref="ISolutionWriter" /> instance.</returns>
        internal static ISolutionWriter CreateWriter(string path)
        {
            if (path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                return new SlnxSolutionWriter();
            }

            return new SlnSolutionWriter();
        }

        internal static IEnumerable<string> GetValidSolutionPlatforms(IEnumerable<string> platforms)
        {
            List<string> values = platforms
                .Select(i => i.ToSolutionPlatform())
                .Select(platform =>
                {
                    return platform.ToLowerInvariant() switch
                    {
                        "any cpu" => platform,
                        "x64" => platform,
                        "x86" => platform,
                        "amd64" => "x64",
                        "win32" => "x86",
                        "arm" => platform,
                        "arm64" => platform,
                        _ => null
                    };
                })
                .Where(i => i != null)
                .OrderBy(i => i)
                .ToList();

            return values.Any() ? values : new List<string> { "Any CPU" };
        }

        internal static bool TryGetProjectSolutionConfiguration(string solutionConfiguration, SlnProject project, bool alwaysBuild, out string projectSolutionConfiguration)
        {
            foreach (string projectConfiguration in project.Configurations)
            {
                if (string.Equals(projectConfiguration, solutionConfiguration, StringComparison.OrdinalIgnoreCase))
                {
                    projectSolutionConfiguration = solutionConfiguration;

                    return true;
                }
            }

            projectSolutionConfiguration = project.Configurations.First();

            return alwaysBuild;
        }

        internal static bool TryGetProjectSolutionPlatform(string solutionPlatform, SlnProject project, out string projectSolutionPlatform, out string projectBuildPlatform)
        {
            projectSolutionPlatform = null;
            projectBuildPlatform = null;

            bool containsWin32 = false;
            bool containsX64 = false;
            bool containsAmd64 = false;
            bool containsX86 = false;
            bool containsAnyCPU = false;
            bool containsArm = false;
            bool containsArm64 = false;

            foreach (string projectPlatform in project.Platforms)
            {
                if (string.Equals(projectPlatform, solutionPlatform, StringComparison.OrdinalIgnoreCase) || string.Equals(projectPlatform.ToSolutionPlatform(), solutionPlatform, StringComparison.OrdinalIgnoreCase))
                {
                    projectSolutionPlatform = solutionPlatform;

                    projectBuildPlatform = solutionPlatform;

                    return true;
                }

                switch (projectPlatform.ToLowerInvariant())
                {
                    case "anycpu":
                    case "any cpu":
                        containsAnyCPU = true;
                        break;

                    case "x64":
                        containsX64 = true;
                        break;

                    case "x86":
                        containsX86 = true;
                        break;

                    case "amd64":
                        containsAmd64 = true;
                        break;

                    case "win32":
                        containsWin32 = true;
                        break;

                    case "arm":
                        containsArm = true;
                        break;

                    case "arm64":
                        containsArm64 = true;
                        break;
                }
            }

            if (string.Equals(solutionPlatform, "Any CPU", StringComparison.OrdinalIgnoreCase))
            {
                if (containsX64)
                {
                    projectSolutionPlatform = projectBuildPlatform = "x64";

                    return true;
                }

                if (containsX86)
                {
                    projectSolutionPlatform = projectBuildPlatform = "x86";

                    return true;
                }

                if (containsAmd64)
                {
                    projectSolutionPlatform = projectBuildPlatform = "amd64";

                    return true;
                }

                if (containsWin32)
                {
                    projectSolutionPlatform = projectBuildPlatform = "Win32";

                    return true;
                }

                if (containsArm)
                {
                    projectSolutionPlatform = projectBuildPlatform = "ARM";

                    return true;
                }

                if (containsArm64)
                {
                    projectSolutionPlatform = projectBuildPlatform = "ARM64";

                    return true;
                }
            }

            if (string.Equals(solutionPlatform, "x86", StringComparison.OrdinalIgnoreCase))
            {
                if (containsWin32)
                {
                    projectSolutionPlatform = projectBuildPlatform = "Win32";

                    return true;
                }

                if (containsAnyCPU)
                {
                    projectSolutionPlatform = projectBuildPlatform = "Any CPU";

                    return true;
                }
            }

            if (string.Equals(solutionPlatform, "x64", StringComparison.OrdinalIgnoreCase))
            {
                if (containsAmd64)
                {
                    projectSolutionPlatform = projectBuildPlatform = "amd64";

                    return true;
                }

                if (containsAnyCPU)
                {
                    projectSolutionPlatform = projectBuildPlatform = "Any CPU";

                    return true;
                }
            }

            projectSolutionPlatform = project.Platforms.First().ToSolutionPlatform();

            return false;
        }

        /// <summary>
        /// Returns the projects sorted by main project first, then by path.
        /// </summary>
        internal List<SlnProject> GetSortedProjects()
        {
            return _projects.OrderBy(i => i.IsMainProject ? 0 : 1).ThenBy(i => i.FullPath).ToList();
        }

        /// <summary>
        /// Gets the resolved set of solution configurations.
        /// </summary>
        internal HashSet<string> GetSolutionConfigurations()
        {
            return Configurations != null && Configurations.Any()
                ? new HashSet<string>(Configurations, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(_projects.SelectMany(i => i.Configurations).Where(i => !i.IsNullOrWhiteSpace()), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the resolved set of solution platforms (normalized to valid VS platform names).
        /// </summary>
        internal HashSet<string> GetSolutionPlatforms()
        {
            return Platforms != null && Platforms.Any()
                ? new HashSet<string>(GetValidSolutionPlatforms(Platforms), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(GetValidSolutionPlatforms(_projects.SelectMany(i => i.Platforms)), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the folder hierarchy for the solution, or returns null if no folders are needed.
        /// </summary>
        internal SlnHierarchy BuildHierarchy(IReadOnlyList<SlnProject> sortedProjects, bool useFolders, bool collapseFolders)
        {
            if (useFolders && sortedProjects.Any(i => !i.IsMainProject))
            {
                return SlnHierarchy.CreateFromProjectDirectories(sortedProjects, SolutionItems, collapseFolders);
            }

            if (sortedProjects.Any(i => !string.IsNullOrWhiteSpace(i.SolutionFolder)))
            {
                return SlnHierarchy.CreateFromProjectSolutionFolder(sortedProjects, SolutionItems);
            }

            return null;
        }
    }
}