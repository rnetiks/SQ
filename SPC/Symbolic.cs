using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SPC
{
    /// <summary>
    /// Creates symbolic links to the most recent DLL files from a Visual Studio project
    /// to a target folder, useful for game development and plugin systems.
    /// </summary>
    public class Symbolic
    {
        private readonly string _projectPath;
        private readonly string _targetFolder;
        private readonly bool _includeSubfolders;
        private readonly string _specificConfiguration;
        private readonly string _specificPlatform;
        private readonly List<string> _excludePatterns = new List<string>();
        private readonly List<string> _includePatterns = new List<string>();
        
        /// <summary>
        /// Initializes a new instance of the BindGame class.
        /// </summary>
        /// <param name="projectPath">The path to the project file (.csproj) or the project directory</param>
        /// <param name="targetFolder">The target folder where to create the symbolic link</param>
        public Symbolic(string projectPath, string targetFolder)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                throw new ArgumentNullException(nameof(projectPath));
                
            if (string.IsNullOrWhiteSpace(targetFolder))
                throw new ArgumentNullException(nameof(targetFolder));
                
            _projectPath = projectPath;
            _targetFolder = targetFolder;
            _includeSubfolders = true;
            _specificConfiguration = null;
            _specificPlatform = null;
        }
        
        /// <summary>
        /// Initializes a new instance of the BindGame class with advanced options.
        /// </summary>
        /// <param name="projectPath">The path to the project file (.csproj) or the project directory</param>
        /// <param name="targetFolder">The target folder where to create the symbolic link</param>
        /// <param name="includeSubfolders">Whether to include subfolders when searching for DLLs</param>
        /// <param name="configuration">Specific build configuration to use (e.g., "Debug", "Release")</param>
        /// <param name="platform">Specific platform to use (e.g., "AnyCPU", "x64")</param>
        public Symbolic(
            string projectPath, 
            string targetFolder, 
            bool includeSubfolders = true,
            string configuration = null,
            string platform = null)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                throw new ArgumentNullException(nameof(projectPath));
                
            if (string.IsNullOrWhiteSpace(targetFolder))
                throw new ArgumentNullException(nameof(targetFolder));
                
            _projectPath = projectPath;
            _targetFolder = targetFolder;
            _includeSubfolders = includeSubfolders;
            _specificConfiguration = configuration;
            _specificPlatform = platform;
        }
        
        /// <summary>
        /// Adds a pattern to exclude from DLL matching.
        /// </summary>
        /// <param name="pattern">A wildcard pattern (*, ?) to exclude</param>
        /// <returns>The current BindGame instance for fluent chaining</returns>
        public Symbolic ExcludePattern(string pattern)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
                _excludePatterns.Add(pattern);
                
            return this;
        }
        
        /// <summary>
        /// Adds a pattern to include in DLL matching.
        /// </summary>
        /// <param name="pattern">A wildcard pattern (*, ?) to include</param>
        /// <returns>The current BindGame instance for fluent chaining</returns>
        public Symbolic IncludePattern(string pattern)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
                _includePatterns.Add(pattern);
                
            return this;
        }
        
        /// <summary>
        /// Executes the binding operation to find the most recent DLL and create a symbolic link.
        /// Also includes the corresponding PDB file if available.
        /// </summary>
        /// <returns>Information about the binding operation</returns>
        public BindResult Execute()
        {
            try
            {
               
                EnsureTargetFolderExists();
                
               
                string normalizedProjectPath = GetNormalizedProjectPath();
                
               
                FileInfo mostRecentDll = FindMostRecentDll(normalizedProjectPath);
                
                if (mostRecentDll == null)
                {
                    return new BindResult
                    {
                        Success = false,
                        ErrorMessage = "No matching DLL files found"
                    };
                }
                
               
                string targetDllPath = Path.Combine(_targetFolder, mostRecentDll.Name);
                CreateSymbolicLink(mostRecentDll.FullName, targetDllPath);
                
               
                string pdbFilePath = Path.ChangeExtension(mostRecentDll.FullName, ".pdb");
                bool pdbLinked = false;
                string pdbTargetPath = null;
                
                if (File.Exists(pdbFilePath))
                {
                   
                    pdbTargetPath = Path.Combine(_targetFolder, Path.GetFileName(pdbFilePath));
                    CreateSymbolicLink(pdbFilePath, pdbTargetPath);
                    pdbLinked = true;
                }
                
                return new BindResult
                {
                    Success = true,
                    SourceFile = mostRecentDll.FullName,
                    SymlinkPath = targetDllPath,
                    LastModified = mostRecentDll.LastWriteTime,
                    PdbSourceFile = pdbLinked ? pdbFilePath : null,
                    PdbSymlinkPath = pdbLinked ? pdbTargetPath : null,
                    PdbIncluded = pdbLinked
                };
            }
            catch (Exception ex)
            {
                return new BindResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Exception = ex
                };
            }
        }
        
        /// <summary>
        /// Ensures the target folder exists.
        /// </summary>
        private void EnsureTargetFolderExists()
        {
            if (!Directory.Exists(_targetFolder))
            {
                Directory.CreateDirectory(_targetFolder);
            }
        }
        
        /// <summary>
        /// Gets the normalized project path, handling both .csproj files and directories.
        /// </summary>
        /// <returns>The normalized project path</returns>
        private string GetNormalizedProjectPath()
        {
            if (File.Exists(_projectPath) && _projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
               
                return Path.GetDirectoryName(_projectPath);
            }

            if (Directory.Exists(_projectPath))
            {
               
                return _projectPath;
            }
            throw new FileNotFoundException("Project path not found", _projectPath);
        }
        
        /// <summary>
        /// Finds the most recent DLL file in the project's output folders.
        /// </summary>
        /// <param name="projectDir">The project directory</param>
        /// <returns>The most recent DLL file, or null if none found</returns>
        private FileInfo FindMostRecentDll(string projectDir)
        {
            List<string> outputFolders = GetPotentialOutputFolders(projectDir);
            
           
            List<FileInfo> dllFiles = new List<FileInfo>();
            
            foreach (string folder in outputFolders)
            {
                if (!Directory.Exists(folder))
                    continue;
                    
                SearchOption searchOption = _includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                
                var filesInFolder = Directory.GetFiles(folder, "*.dll", searchOption)
                    .Select(f => new FileInfo(f));
                
                dllFiles.AddRange(filesInFolder);
            }
            
           
            if (_includePatterns.Count > 0)
            {
                dllFiles = dllFiles.Where(file => 
                    _includePatterns.Any(pattern => IsWildcardMatch(file.Name, pattern)))
                    .ToList();
            }
            
           
            if (_excludePatterns.Count > 0)
            {
                dllFiles = dllFiles.Where(file => 
                    !_excludePatterns.Any(pattern => IsWildcardMatch(file.Name, pattern)))
                    .ToList();
            }
            
           
            return dllFiles.OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
        }
        
        /// <summary>
        /// Checks if a string matches a wildcard pattern.
        /// </summary>
        /// <param name="input">The input string</param>
        /// <param name="pattern">The wildcard pattern</param>
        /// <returns>True if the input matches the pattern</returns>
        private bool IsWildcardMatch(string input, string pattern)
        {
           
            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
                
            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
        }
        
        /// <summary>
        /// Gets potential output folders for the project.
        /// </summary>
        /// <param name="projectDir">The project directory</param>
        /// <returns>A list of potential output folders</returns>
        private List<string> GetPotentialOutputFolders(string projectDir)
        {
            List<string> result = new List<string>();
            
           
            string[] projectFiles = Directory.GetFiles(projectDir, "*.csproj");
            foreach (string projectFile in projectFiles)
            {
                try
                {
                    List<string> pathsFromProject = ExtractOutputPathsFromProject(projectFile);
                    result.AddRange(pathsFromProject);
                }
                catch
                {
                   // Ignore
                }
            }
            
           
            if (result.Count == 0)
            {
               
                string[] configurations = _specificConfiguration != null 
                    ? new[] { _specificConfiguration } 
                    : new[] { "Debug", "Release" };
                    
                string[] platforms = _specificPlatform != null
                    ? new[] { _specificPlatform }
                    : new[] { "AnyCPU", "x64", "x86", "Any CPU", "" };
                    
               
                foreach (string config in configurations)
                {
                    foreach (string platform in platforms)
                    {
                        if (string.IsNullOrEmpty(platform))
                        {
                            result.Add(Path.Combine(projectDir, "bin", config));
                        }
                        else
                        {
                            result.Add(Path.Combine(projectDir, "bin", config));
                            result.Add(Path.Combine(projectDir, "bin", platform, config));
                            result.Add(Path.Combine(projectDir, "bin", config, platform));
                        }
                    }
                }
                
               
                result.Add(Path.Combine(projectDir, "bin"));
            }
            
            return result.Distinct().Where(Directory.Exists).ToList();
        }
        
        /// <summary>
        /// Extracts output paths from a .csproj file.
        /// </summary>
        /// <param name="projectFilePath">The path to the .csproj file</param>
        /// <returns>A list of output paths</returns>
        public List<string> ExtractOutputPathsFromProject(string projectFilePath)
        {
            List<string> result = new List<string>();
            XDocument doc = XDocument.Load(projectFilePath);
            var ns = doc.Root.GetDefaultNamespace();
            
           
            var outputPathElements = doc.Descendants(ns + "OutputPath")
                .Concat(doc.Descendants("OutputPath"));
                
            foreach (var element in outputPathElements)
            {
                string path = element.Value.Trim();
                if (!string.IsNullOrEmpty(path))
                {
                   
                    if (!Path.IsPathRooted(path))
                    {
                        path = Path.Combine(Path.GetDirectoryName(projectFilePath), path);
                    }
                    
                   
                    path = Path.GetFullPath(path);
                    result.Add(path);
                }
            }
            
           
            var propertyGroups = doc.Descendants(ns + "PropertyGroup")
                .Concat(doc.Descendants("PropertyGroup"));
                
            foreach (var group in propertyGroups)
            {
               
                var conditionAttr = group.Attribute("Condition");
                if (conditionAttr != null)
                {
                    string condition = conditionAttr.Value;
                    
                   
                    if (_specificConfiguration != null && !condition.Contains(_specificConfiguration))
                        continue;
                        
                    if (_specificPlatform != null && !condition.Contains(_specificPlatform))
                        continue;
                }
                
               
                var outputPathElement = group.Element(ns + "OutputPath") ?? group.Element("OutputPath");
                if (outputPathElement != null)
                {
                    string path = outputPathElement.Value.Trim();
                    if (!string.IsNullOrEmpty(path))
                    {
                       
                        if (!Path.IsPathRooted(path))
                        {
                            path = Path.Combine(Path.GetDirectoryName(projectFilePath), path);
                        }
                        
                       
                        path = Path.GetFullPath(path);
                        result.Add(path);
                    }
                }
            }
            
            return result.Distinct().ToList();
        }
        
        /// <summary>
        /// Creates a symbolic link at the target path pointing to the source path.
        /// </summary>
        /// <param name="sourcePath">The source DLL file path</param>
        /// <param name="targetPath">The target symbolic link path</param>
        private void CreateSymbolicLink(string sourcePath, string targetPath)
        {
           
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            
            File.CreateSymbolicLink(targetPath, sourcePath);
        }
        
        /// <summary>
        /// Static convenience method to quickly bind a project.
        /// </summary>
        /// <param name="projectPath">The path to the project file or directory</param>
        /// <param name="targetFolder">The target folder for the symbolic link</param>
        /// <returns>The result of the binding operation</returns>
        public static BindResult Bind(string projectPath, string targetFolder)
        {
            return new Symbolic(projectPath, targetFolder).Execute();
        }
        
        public static void BindFile(string inputFile, string outputFile)
        {
            File.CreateSymbolicLink(inputFile, outputFile);
        }
    }
    
    /// <summary>
    /// Represents the result of a binding operation.
    /// </summary>
    public class BindResult
    {
        /// <summary>
        /// Gets or sets whether the operation was successful.
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// Gets or sets the source DLL file path.
        /// </summary>
        public string SourceFile { get; set; }
        
        /// <summary>
        /// Gets or sets the symbolic link path.
        /// </summary>
        public string SymlinkPath { get; set; }
        
        /// <summary>
        /// Gets or sets the last modified time of the DLL.
        /// </summary>
        public DateTime LastModified { get; set; }
        
        /// <summary>
        /// Gets or sets the error message if the operation failed.
        /// </summary>
        public string ErrorMessage { get; set; }
        
        /// <summary>
        /// Gets or sets the exception if the operation failed.
        /// </summary>
        public Exception Exception { get; set; }
        
        /// <summary>
        /// Gets or sets whether a PDB file was included in the binding.
        /// </summary>
        public bool PdbIncluded { get; set; }
        
        /// <summary>
        /// Gets or sets the source PDB file path.
        /// </summary>
        public string PdbSourceFile { get; set; }
        
        /// <summary>
        /// Gets or sets the PDB symbolic link path.
        /// </summary>
        public string PdbSymlinkPath { get; set; }
    }
}