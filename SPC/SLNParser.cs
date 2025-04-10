using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SPC
{
    /// <summary>
    /// Provides functionality to parse and extract information from Visual Studio solution files (.sln)
    /// </summary>
    public class SolutionParser
    {
        private string _solutionContent;
        private readonly string _solutionPath;
        private readonly List<SolutionProject> _projects = new List<SolutionProject>();
        private readonly List<SolutionFolder> _folders = new List<SolutionFolder>();
        private readonly Dictionary<string, string> _nestedProjects = new Dictionary<string, string>();
        private readonly List<SolutionConfiguration> _configurations = new List<SolutionConfiguration>();

        /// <summary>
        /// Gets all projects in the solution
        /// </summary>
        public IReadOnlyList<SolutionProject> Projects => _projects.AsReadOnly();

        /// <summary>
        /// Gets all folders in the solution
        /// </summary>
        public IReadOnlyList<SolutionFolder> Folders => _folders.AsReadOnly();

        /// <summary>
        /// Gets all configurations in the solution
        /// </summary>
        public IReadOnlyList<SolutionConfiguration> Configurations => _configurations.AsReadOnly();

        /// <summary>
        /// Initializes a new instance of the SolutionParser class
        /// </summary>
        /// <param name="solutionPath">The path to the solution file</param>
        public SolutionParser(string solutionPath)
        {
            if (string.IsNullOrEmpty(solutionPath))
                throw new ArgumentNullException(nameof(solutionPath));

            if (!File.Exists(solutionPath))
                throw new FileNotFoundException("Solution file not found", solutionPath);

            if (!solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("File is not a Visual Studio solution file", nameof(solutionPath));

            _solutionPath = solutionPath;
        }

        /// <summary>
        /// Parses the solution file and extracts all relevant information
        /// </summary>
        public void Parse()
        {
            _solutionContent = File.ReadAllText(_solutionPath);


            _projects.Clear();
            _folders.Clear();
            _nestedProjects.Clear();
            _configurations.Clear();


            ParseProjectsAndFolders();


            ParseNestedProjects();


            ParseConfigurations();


            BuildFolderHierarchy();
        }

        /// <summary>
        /// Parses project and solution folder definitions from the solution file
        /// </summary>
        private void ParseProjectsAndFolders()
        {
            var projectRegex =
                new Regex(
                    @"Project\(\""(?<typeGuid>[^\""]*)\""\)\s+=\s+\""(?<name>[^\""]*)\"",\s+\""(?<path>[^\""]*)\"",\s+\""(?<guid>[^\""]*)\""\s*(?<sections>EndProject)?",
                    RegexOptions.Compiled | RegexOptions.Multiline);

            var matches = projectRegex.Matches(_solutionContent);

            foreach (Match match in matches)
            {
                var typeGuid = match.Groups["typeGuid"].Value;
                var name = match.Groups["name"].Value;
                var path = match.Groups["path"].Value;
                var guid = match.Groups["guid"].Value;


                if (typeGuid.Equals("{2150E333-8FDC-42A3-9474-1A3956D46DE8}", StringComparison.OrdinalIgnoreCase))
                {
                    _folders.Add(new SolutionFolder
                    {
                        Name = name,
                        Guid = guid
                    });
                }
                else
                {
                    _projects.Add(new SolutionProject
                    {
                        Name = name,
                        Path = path,
                        Guid = guid,
                        TypeGuid = typeGuid
                    });
                }
            }
        }

        /// <summary>
        /// Parses nested project relationships from the solution file
        /// </summary>
        private void ParseNestedProjects()
        {
            var nestedSectionRegex =
                new Regex(@"GlobalSection\(NestedProjects\)\s+=\s+preSolution(.*?)EndGlobalSection",
                    RegexOptions.Compiled | RegexOptions.Singleline);
            var nestedMatch = nestedSectionRegex.Match(_solutionContent);

            if (nestedMatch.Success)
            {
                var nestedContent = nestedMatch.Groups[1].Value;


                var nestedItemRegex = new Regex(@"(?<childGuid>\{[0-9A-F-]+\})\s+=\s+(?<parentGuid>\{[0-9A-F-]+\})",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);
                var nestedMatches = nestedItemRegex.Matches(nestedContent);

                foreach (Match itemMatch in nestedMatches)
                {
                    var childGuid = itemMatch.Groups["childGuid"].Value;
                    var parentGuid = itemMatch.Groups["parentGuid"].Value;

                    _nestedProjects[childGuid] = parentGuid;
                }
            }
        }

        /// <summary>
        /// Parses solution configurations from the solution file
        /// </summary>
        private void ParseConfigurations()
        {
            var configSectionRegex =
                new Regex(@"GlobalSection\(SolutionConfigurationPlatforms\)\s+=\s+preSolution(.*?)EndGlobalSection",
                    RegexOptions.Compiled | RegexOptions.Singleline);
            var configMatch = configSectionRegex.Match(_solutionContent);

            if (configMatch.Success)
            {
                var configContent = configMatch.Groups[1].Value;


                var configItemRegex = new Regex(@"(?<config>[^=]+)\s+=\s+(?<config2>.+)", RegexOptions.Compiled);
                var configMatches = configItemRegex.Matches(configContent);

                foreach (Match itemMatch in configMatches)
                {
                    var configStr = itemMatch.Groups["config"].Value.Trim();
                    var parts = configStr.Split('|');

                    if (parts.Length == 2)
                    {
                        _configurations.Add(new SolutionConfiguration
                        {
                            Configuration = parts[0].Trim(),
                            Platform = parts[1].Trim()
                        });
                    }
                }
            }


            var projectConfigSectionRegex =
                new Regex(@"GlobalSection\(ProjectConfigurationPlatforms\)\s+=\s+postSolution(.*?)EndGlobalSection",
                    RegexOptions.Compiled | RegexOptions.Singleline);
            var projectConfigMatch = projectConfigSectionRegex.Match(_solutionContent);

            if (projectConfigMatch.Success)
            {
                var projectConfigContent = projectConfigMatch.Groups[1].Value;


                var projectConfigItemRegex =
                    new Regex(@"(?<projectGuid>\{[0-9A-F-]+\})\.(?<config>[^=]+)\s+=\s+(?<value>.+)",
                        RegexOptions.Compiled | RegexOptions.IgnoreCase);
                var projectConfigMatches = projectConfigItemRegex.Matches(projectConfigContent);

                foreach (Match itemMatch in projectConfigMatches)
                {
                    var projectGuid = itemMatch.Groups["projectGuid"].Value;
                    var config = itemMatch.Groups["config"].Value.Trim();
                    var value = itemMatch.Groups["value"].Value.Trim();

                    var project = _projects.FirstOrDefault(p =>
                        p.Guid.Equals(projectGuid, StringComparison.OrdinalIgnoreCase));
                    if (project != null)
                    {
                        if (project.ProjectConfigurations == null)
                            project.ProjectConfigurations = new Dictionary<string, string>();

                        project.ProjectConfigurations[config] = value;
                    }
                }
            }
        }

        /// <summary>
        /// Builds the folder hierarchy based on nested project relationships
        /// </summary>
        private void BuildFolderHierarchy()
        {
            foreach (var project in _projects)
            {
                if (_nestedProjects.TryGetValue(project.Guid, out var parentGuid))
                {
                    var parentFolder =
                        _folders.FirstOrDefault(f => f.Guid.Equals(parentGuid, StringComparison.OrdinalIgnoreCase));
                    if (parentFolder != null)
                    {
                        project.ParentFolder = parentFolder;
                        if (parentFolder.Projects == null)
                            parentFolder.Projects = new List<SolutionProject>();

                        parentFolder.Projects.Add(project);
                    }
                }
            }


            foreach (var folder in _folders)
            {
                if (_nestedProjects.TryGetValue(folder.Guid, out var parentGuid))
                {
                    var parentFolder =
                        _folders.FirstOrDefault(f => f.Guid.Equals(parentGuid, StringComparison.OrdinalIgnoreCase));
                    if (parentFolder != null)
                    {
                        folder.ParentFolder = parentFolder;
                        if (parentFolder.SubFolders == null)
                            parentFolder.SubFolders = new List<SolutionFolder>();

                        parentFolder.SubFolders.Add(folder);
                    }
                }
            }
        }

        /// <summary>
        /// Gets a project by its GUID
        /// </summary>
        /// <param name="guid">The GUID of the project</param>
        /// <returns>The project, or null if not found</returns>
        public SolutionProject GetProjectByGuid(string guid)
        {
            return _projects.FirstOrDefault(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a project by its name
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <returns>The project, or null if not found</returns>
        public SolutionProject GetProjectByName(string name)
        {
            return _projects.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a folder by its GUID
        /// </summary>
        /// <param name="guid">The GUID of the folder</param>
        /// <returns>The folder, or null if not found</returns>
        public SolutionFolder GetFolderByGuid(string guid)
        {
            return _folders.FirstOrDefault(f => f.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a folder by its name
        /// </summary>
        /// <param name="name">The name of the folder</param>
        /// <returns>The folder, or null if not found</returns>
        public SolutionFolder GetFolderByName(string name)
        {
            return _folders.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the parent folder of a project or a folder
        /// </summary>
        /// <param name="guid">The GUID of the project or folder</param>
        /// <returns>The parent folder, or null if not found or if the item is at the root level</returns>
        public SolutionFolder GetParentFolder(string guid)
        {
            if (_nestedProjects.TryGetValue(guid, out var parentGuid))
            {
                return GetFolderByGuid(parentGuid);
            }

            return null;
        }

        /// <summary>
        /// Gets all items (projects and sub-folders) in a folder
        /// </summary>
        /// <param name="folderGuid">The GUID of the folder</param>
        /// <returns>A list of GUIDs of the items in the folder</returns>
        public List<string> GetItemsInFolder(string folderGuid)
        {
            var result = new List<string>();

            foreach (var kvp in _nestedProjects)
            {
                if (kvp.Value.Equals(folderGuid, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(kvp.Key);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the full path to a project file, relative to the solution directory
        /// </summary>
        /// <param name="projectGuid">The GUID of the project</param>
        /// <returns>The full path to the project file, or null if the project is not found</returns>
        public string GetProjectFilePath(string projectGuid)
        {
            var project = GetProjectByGuid(projectGuid);
            if (project == null)
                return null;

            var solutionDir = Path.GetDirectoryName(_solutionPath);
            return Path.Combine(solutionDir, project.Path);
        }

        /// <summary>
        /// Gets the solution directory path
        /// </summary>
        /// <returns>The full path to the solution directory</returns>
        public string GetSolutionDirectory()
        {
            return Path.GetDirectoryName(_solutionPath);
        }

        /// <summary>
        /// Gets the GUID of a file in the solution
        /// </summary>
        /// <param name="filePath">The relative path to the file from the solution directory</param>
        /// <returns>The GUID of the project containing the file, or null if not found</returns>
        /// <remarks>This method does not analyze project files, so it can only determine the project that might contain the file based on the file path</remarks>
        public string GetFileGuid(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            var solutionDir = Path.GetDirectoryName(_solutionPath);
            var normalizedPath = Path.GetFullPath(Path.Combine(solutionDir, filePath));


            foreach (var project in _projects)
            {
                var projectPath = Path.GetFullPath(Path.Combine(solutionDir, project.Path));
                var projectDir = Path.GetDirectoryName(projectPath);

                if (normalizedPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                {
                    return project.Guid;
                }
            }

            return null;
        }

        /// <summary>
        /// Prints the folder structure of the solution to the console
        /// </summary>
        public void PrintFolderStructure()
        {
            var rootFolders = _folders.Where(f => f.ParentFolder == null).ToList();


            var rootProjects = _projects.Where(p => p.ParentFolder == null).ToList();


            foreach (var folder in rootFolders)
            {
                PrintFolder(folder, 0);
            }


            foreach (var project in rootProjects)
            {
                Console.WriteLine($"- {project.Name} ({project.Guid})");
            }
        }

        /// <summary>
        /// Helper method to recursively print a folder and its contents
        /// </summary>
        private void PrintFolder(SolutionFolder folder, int indentLevel)
        {
            var indent = new string(' ', indentLevel * 2);
            Console.WriteLine($"{indent}+ {folder.Name} ({folder.Guid})");


            if (folder.SubFolders != null)
            {
                foreach (var subFolder in folder.SubFolders)
                {
                    PrintFolder(subFolder, indentLevel + 1);
                }
            }


            if (folder.Projects != null)
            {
                foreach (var project in folder.Projects)
                {
                    Console.WriteLine($"{indent}  - {project.Name} ({project.Guid})");
                }
            }
        }

        #region Solution Modification Methods

        /// <summary>
        /// Adds a new project to the solution
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <param name="relativePath">The relative path to the project file from the solution directory</param>
        /// <param name="projectTypeGuid">The project type GUID (optional, defaults to C# project type)</param>
        /// <param name="parentFolderGuid">The GUID of the parent folder (optional)</param>
        /// <returns>The GUID of the newly added project</returns>
        public string AddProject(string name, string relativePath, string projectTypeGuid = null,
            string parentFolderGuid = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentNullException(nameof(relativePath));


            if (string.IsNullOrEmpty(projectTypeGuid))
                projectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";


            var projectGuid = "{" + Guid.NewGuid().ToString().ToUpper() + "}";


            var project = new SolutionProject
            {
                Name = name,
                Path = relativePath,
                Guid = projectGuid,
                TypeGuid = projectTypeGuid,
                ProjectConfigurations = new Dictionary<string, string>()
            };

            _projects.Add(project);


            if (!string.IsNullOrEmpty(parentFolderGuid))
            {
                var parentFolder = GetFolderByGuid(parentFolderGuid);
                if (parentFolder != null)
                {
                    project.ParentFolder = parentFolder;
                    _nestedProjects[projectGuid] = parentFolderGuid;

                    if (parentFolder.Projects == null)
                        parentFolder.Projects = new List<SolutionProject>();

                    parentFolder.Projects.Add(project);
                }
            }


            AddDefaultProjectConfigurations(projectGuid);

            return projectGuid;
        }

        /// <summary>
        /// Adds a new solution folder to the solution
        /// </summary>
        /// <param name="name">The name of the folder</param>
        /// <param name="parentFolderGuid">The GUID of the parent folder (optional)</param>
        /// <returns>The GUID of the newly added folder</returns>
        public string AddFolder(string name, string parentFolderGuid = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));


            var folderGuid = "{" + Guid.NewGuid().ToString().ToUpper() + "}";


            var folder = new SolutionFolder
            {
                Name = name,
                Guid = folderGuid,
                SubFolders = new List<SolutionFolder>(),
                Projects = new List<SolutionProject>()
            };

            _folders.Add(folder);


            if (!string.IsNullOrEmpty(parentFolderGuid))
            {
                var parentFolder = GetFolderByGuid(parentFolderGuid);
                if (parentFolder != null)
                {
                    folder.ParentFolder = parentFolder;
                    _nestedProjects[folderGuid] = parentFolderGuid;

                    if (parentFolder.SubFolders == null)
                        parentFolder.SubFolders = new List<SolutionFolder>();

                    parentFolder.SubFolders.Add(folder);
                }
            }

            return folderGuid;
        }

        /// <summary>
        /// Adds nesting relationship between a project/folder and a parent folder
        /// </summary>
        /// <param name="childGuid">The GUID of the child project or folder</param>
        /// <param name="parentFolderGuid">The GUID of the parent folder</param>
        /// <returns>True if the nesting was successful, false otherwise</returns>
        public bool AddNesting(string childGuid, string parentFolderGuid)
        {
            if (string.IsNullOrEmpty(childGuid) || string.IsNullOrEmpty(parentFolderGuid))
                return false;


            var parentFolder = GetFolderByGuid(parentFolderGuid);
            if (parentFolder == null)
                return false;


            var childProject = GetProjectByGuid(childGuid);
            if (childProject != null)
            {
                if (childProject.ParentFolder != null)
                {
                    childProject.ParentFolder.Projects.Remove(childProject);
                }


                childProject.ParentFolder = parentFolder;
                if (parentFolder.Projects == null)
                    parentFolder.Projects = new List<SolutionProject>();

                parentFolder.Projects.Add(childProject);
                _nestedProjects[childGuid] = parentFolderGuid;
                return true;
            }


            var childFolder = GetFolderByGuid(childGuid);
            if (childFolder != null)
            {
                if (childFolder.ParentFolder != null)
                {
                    childFolder.ParentFolder.SubFolders.Remove(childFolder);
                }


                childFolder.ParentFolder = parentFolder;
                if (parentFolder.SubFolders == null)
                    parentFolder.SubFolders = new List<SolutionFolder>();

                parentFolder.SubFolders.Add(childFolder);
                _nestedProjects[childGuid] = parentFolderGuid;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds default Debug and Release configurations for a project
        /// </summary>
        /// <param name="projectGuid">The GUID of the project</param>
        private void AddDefaultProjectConfigurations(string projectGuid)
        {
            var project = GetProjectByGuid(projectGuid);
            if (project == null)
                return;

            if (project.ProjectConfigurations == null)
                project.ProjectConfigurations = new Dictionary<string, string>();


            project.ProjectConfigurations["Debug|AnyCPU.ActiveCfg"] = "Debug|AnyCPU";
            project.ProjectConfigurations["Debug|AnyCPU.Build.0"] = "Debug|AnyCPU";


            project.ProjectConfigurations["Release|AnyCPU.ActiveCfg"] = "Release|AnyCPU";
            project.ProjectConfigurations["Release|AnyCPU.Build.0"] = "Release|AnyCPU";


            EnsureSolutionConfiguration("Debug", "AnyCPU");
            EnsureSolutionConfiguration("Release", "AnyCPU");
        }

        /// <summary>
        /// Ensures that a specific solution configuration exists
        /// </summary>
        /// <param name="configuration">The configuration name</param>
        /// <param name="platform">The platform name</param>
        private void EnsureSolutionConfiguration(string configuration, string platform)
        {
            if (!_configurations.Any(c =>
                    c.Configuration.Equals(configuration, StringComparison.OrdinalIgnoreCase) &&
                    c.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase)))
            {
                _configurations.Add(new SolutionConfiguration
                {
                    Configuration = configuration,
                    Platform = platform
                });
            }
        }

        /// <summary>
        /// Saves the solution to a file
        /// </summary>
        /// <param name="outputPath">The path where to save the solution file (optional, defaults to the original path)</param>
        public void SaveSolution(string outputPath = null)
        {
            if (string.IsNullOrEmpty(outputPath))
                outputPath = _solutionPath;


            var content = new System.Text.StringBuilder();


            content.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            content.AppendLine("# Visual Studio Version 17");
            content.AppendLine("VisualStudioVersion = 17.0.31903.59");
            content.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");


            foreach (var project in _projects)
            {
                content.AppendLine(
                    $"Project(\"{project.TypeGuid}\") = \"{project.Name}\", \"{project.Path}\", \"{project.Guid}\"");
                content.AppendLine("EndProject");
            }


            foreach (var folder in _folders)
            {
                content.AppendLine(
                    $"Project(\"{{2150E333-8FDC-42A3-9474-1A3956D46DE8}}\") = \"{folder.Name}\", \"{folder.Name}\", \"{folder.Guid}\"");
                content.AppendLine("EndProject");
            }


            content.AppendLine("Global");


            content.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
            foreach (var config in _configurations)
            {
                content.AppendLine(
                    $"\t\t{config.Configuration}|{config.Platform} = {config.Configuration}|{config.Platform}");
            }

            content.AppendLine("\tEndGlobalSection");


            content.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
            foreach (var project in _projects)
            {
                if (project.ProjectConfigurations != null)
                {
                    foreach (var config in project.ProjectConfigurations)
                    {
                        content.AppendLine($"\t\t{project.Guid}.{config.Key} = {config.Value}");
                    }
                }
            }

            content.AppendLine("\tEndGlobalSection");


            content.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
            content.AppendLine("\t\tHideSolutionNode = FALSE");
            content.AppendLine("\tEndGlobalSection");


            if (_nestedProjects.Count > 0)
            {
                content.AppendLine("\tGlobalSection(NestedProjects) = preSolution");
                foreach (var relation in _nestedProjects)
                {
                    content.AppendLine($"\t\t{relation.Key} = {relation.Value}");
                }

                content.AppendLine("\tEndGlobalSection");
            }


            content.AppendLine("\tGlobalSection(ExtensibilityGlobals) = postSolution");
            content.AppendLine($"\t\tSolutionGuid = {{{Guid.NewGuid().ToString().ToUpper()}}}");
            content.AppendLine("\tEndGlobalSection");


            content.AppendLine("EndGlobal");


            File.WriteAllText(outputPath, content.ToString());
        }

        #endregion
    }

    /// <summary>
    /// Represents a project in a Visual Studio solution
    /// </summary>
    public class SolutionProject
    {
        /// <summary>
        /// Gets or sets the name of the project
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the relative path to the project file
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets the GUID of the project
        /// </summary>
        public string Guid { get; set; }

        /// <summary>
        /// Gets or sets the project type GUID
        /// </summary>
        public string TypeGuid { get; set; }

        /// <summary>
        /// Gets or sets the parent folder of the project
        /// </summary>
        public SolutionFolder ParentFolder { get; set; }

        /// <summary>
        /// Gets or sets the project configurations
        /// </summary>
        public Dictionary<string, string> ProjectConfigurations { get; set; }
    }

    /// <summary>
    /// Represents a folder in a Visual Studio solution
    /// </summary>
    public class SolutionFolder
    {
        /// <summary>
        /// Gets or sets the name of the folder
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the GUID of the folder
        /// </summary>
        public string Guid { get; set; }

        /// <summary>
        /// Gets or sets the parent folder of this folder
        /// </summary>
        public SolutionFolder ParentFolder { get; set; }

        /// <summary>
        /// Gets or sets the sub-folders of this folder
        /// </summary>
        public List<SolutionFolder> SubFolders { get; set; }

        /// <summary>
        /// Gets or sets the projects contained in this folder
        /// </summary>
        public List<SolutionProject> Projects { get; set; }
    }

    /// <summary>
    /// Represents a configuration in a Visual Studio solution
    /// </summary>
    public class SolutionConfiguration
    {
        /// <summary>
        /// Gets or sets the configuration name (e.g., Debug, Release)
        /// </summary>
        public string Configuration { get; set; }

        /// <summary>
        /// Gets or sets the platform name (e.g., Any CPU, x86, x64)
        /// </summary>
        public string Platform { get; set; }
    }
}