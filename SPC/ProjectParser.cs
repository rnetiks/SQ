using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SPC
{
    /// <summary>
    /// Provides functionality to create and manipulate Visual Studio C# project files (.csproj)
    /// </summary>
    public class ProjectFileGenerator
    {
        private XDocument _projectDocument;
        private XElement _rootElement;
        private readonly string _projectPath;
        private readonly List<ProjectReference> _projectReferences = new List<ProjectReference>();
        private readonly List<PackageReference> _packageReferences = new List<PackageReference>();
        private readonly List<AssemblyReference> _assemblyReferences = new List<AssemblyReference>();
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();
        private readonly List<ProjectItem> _items = new List<ProjectItem>();

        /// <summary>
        /// Gets the project references
        /// </summary>
        public IReadOnlyList<ProjectReference> ProjectReferences => _projectReferences.AsReadOnly();

        /// <summary>
        /// Gets the package references (NuGet)
        /// </summary>
        public IReadOnlyList<PackageReference> PackageReferences => _packageReferences.AsReadOnly();

        /// <summary>
        /// Gets the assembly references
        /// </summary>
        public IReadOnlyList<AssemblyReference> AssemblyReferences => _assemblyReferences.AsReadOnly();

        /// <summary>
        /// Gets the project properties
        /// </summary>
        public IReadOnlyDictionary<string, string> Properties => _properties;

        /// <summary>
        /// Gets the project items (files, etc.)
        /// </summary>
        public IReadOnlyList<ProjectItem> Items => _items.AsReadOnly();

        /// <summary>
        /// Initializes a new instance of the ProjectFileGenerator class for creating a new project file
        /// </summary>
        /// <param name="projectPath">The path where the project file will be saved</param>
        /// <param name="sdkType">The SDK type for the project (default: Microsoft.NET.Sdk)</param>
        public ProjectFileGenerator(string projectPath, string sdkType = "Microsoft.NET.Sdk")
        {
            if (string.IsNullOrEmpty(projectPath))
                throw new ArgumentNullException(nameof(projectPath));

            if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                projectPath += ".csproj";

            _projectPath = projectPath;


            _projectDocument = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("Project",
                    new XAttribute("Sdk", sdkType))
            );

            _rootElement = _projectDocument.Root;


            var propertyGroup = new XElement("PropertyGroup");
            _rootElement.Add(propertyGroup);


            _properties["TargetFramework"] = "net8.0";


            _properties["OutputType"] = "Library";
        }

        /// <summary>
        /// Initializes a new instance of the ProjectFileGenerator class for loading an existing project file
        /// </summary>
        /// <param name="projectPath">The path to the existing project file</param>
        /// <param name="createIfNotExists">Whether to create a new file if the specified file doesn't exist</param>
        /// <param name="sdkType">The SDK type to use if creating a new file</param>
        public static ProjectFileGenerator Load(string projectPath, bool createIfNotExists = false,
            string sdkType = "Microsoft.NET.Sdk")
        {
            if (string.IsNullOrEmpty(projectPath))
                throw new ArgumentNullException(nameof(projectPath));

            if (!File.Exists(projectPath))
            {
                if (createIfNotExists)
                    return new ProjectFileGenerator(projectPath, sdkType);
                else
                    throw new FileNotFoundException("Project file not found", projectPath);
            }

            var generator = new ProjectFileGenerator(projectPath);
            generator._projectDocument = XDocument.Load(projectPath);
            generator._rootElement = generator._projectDocument.Root;


            generator.ParseProperties();
            generator.ParseReferences();
            generator.ParseItems();

            return generator;
        }

        /// <summary>
        /// Parse properties from an existing project file
        /// </summary>
        private void ParseProperties()
        {
            var propertyGroups = _rootElement.Elements("PropertyGroup");
            foreach (var propertyGroup in propertyGroups)
            {
                foreach (var property in propertyGroup.Elements())
                {
                    _properties[property.Name.LocalName] = property.Value;
                }
            }
        }

        /// <summary>
        /// Parse references from an existing project file
        /// </summary>
        private void ParseReferences()
        {
            var itemGroups = _rootElement.Elements("ItemGroup");
            foreach (var itemGroup in itemGroups)
            {
                foreach (var packageRef in itemGroup.Elements("PackageReference"))
                {
                    var include = packageRef.Attribute("Include")?.Value;
                    var version = packageRef.Attribute("Version")?.Value;

                    if (!string.IsNullOrEmpty(include) && !string.IsNullOrEmpty(version))
                    {
                        _packageReferences.Add(new PackageReference
                        {
                            Name = include,
                            Version = version,
                            PrivateAssets = packageRef.Attribute("PrivateAssets")?.Value
                        });
                    }
                }


                foreach (var projectRef in itemGroup.Elements("ProjectReference"))
                {
                    var include = projectRef.Attribute("Include")?.Value;

                    if (!string.IsNullOrEmpty(include))
                    {
                        _projectReferences.Add(new ProjectReference
                        {
                            Path = include
                        });
                    }
                }


                foreach (var reference in itemGroup.Elements("Reference"))
                {
                    var include = reference.Attribute("Include")?.Value;

                    if (!string.IsNullOrEmpty(include))
                    {
                        var hintPath = reference.Element("HintPath")?.Value;
                        _assemblyReferences.Add(new AssemblyReference
                        {
                            Name = include,
                            HintPath = hintPath
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Parse items from an existing project file
        /// </summary>
        private void ParseItems()
        {
            var itemGroups = _rootElement.Elements("ItemGroup");
            foreach (var itemGroup in itemGroups)
            {
                foreach (var item in itemGroup.Elements())
                {
                    if (item.Name.LocalName == "PackageReference" ||
                        item.Name.LocalName == "ProjectReference" ||
                        item.Name.LocalName == "Reference")
                        continue;

                    var include = item.Attribute("Include")?.Value;

                    if (!string.IsNullOrEmpty(include))
                    {
                        var projectItem = new ProjectItem
                        {
                            ItemType = item.Name.LocalName,
                            Include = include
                        };


                        foreach (var metadata in item.Elements())
                        {
                            projectItem.Metadata[metadata.Name.LocalName] = metadata.Value;
                        }

                        _items.Add(projectItem);
                    }
                }
            }
        }

        #region Property Methods

        /// <summary>
        /// Sets the target framework for the project
        /// </summary>
        /// <param name="framework">The target framework (e.g., "net8.0", "net6.0", "netstandard2.0")</param>
        public void SetTargetFramework(string framework)
        {
            if (string.IsNullOrEmpty(framework))
                throw new ArgumentNullException(nameof(framework));

            _properties["TargetFramework"] = framework;
        }

        /// <summary>
        /// Sets multiple target frameworks for the project
        /// </summary>
        /// <param name="frameworks">The target frameworks (e.g., "net8.0;net6.0;netstandard2.0")</param>
        public void SetTargetFrameworks(params string[] frameworks)
        {
            if (frameworks == null || frameworks.Length == 0)
                throw new ArgumentNullException(nameof(frameworks));

            _properties["TargetFrameworks"] = string.Join(";", frameworks);


            _properties.Remove("TargetFramework");
        }

        /// <summary>
        /// Sets the output type of the project
        /// </summary>
        /// <param name="outputType">The output type (e.g., "Library", "Exe", "WinExe")</param>
        public void SetOutputType(string outputType)
        {
            if (string.IsNullOrEmpty(outputType))
                throw new ArgumentNullException(nameof(outputType));

            _properties["OutputType"] = outputType;
        }

        /// <summary>
        /// Sets the assembly name of the project
        /// </summary>
        /// <param name="assemblyName">The assembly name</param>
        public void SetAssemblyName(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
                throw new ArgumentNullException(nameof(assemblyName));

            _properties["AssemblyName"] = assemblyName;
        }

        /// <summary>
        /// Sets the root namespace of the project
        /// </summary>
        /// <param name="rootNamespace">The root namespace</param>
        public void SetRootNamespace(string rootNamespace)
        {
            if (string.IsNullOrEmpty(rootNamespace))
                throw new ArgumentNullException(nameof(rootNamespace));

            _properties["RootNamespace"] = rootNamespace;
        }

        /// <summary>
        /// Sets a custom property for the project
        /// </summary>
        /// <param name="name">The property name</param>
        /// <param name="value">The property value</param>
        public void SetProperty(string name, string value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            _properties[name] = value;
        }

        /// <summary>
        /// Removes a property from the project
        /// </summary>
        /// <param name="name">The property name</param>
        /// <returns>True if the property was removed, false if it didn't exist</returns>
        public bool RemoveProperty(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            return _properties.Remove(name);
        }

        #endregion

        #region Reference Methods

        /// <summary>
        /// Adds a NuGet package reference to the project
        /// </summary>
        /// <param name="packageName">The package name</param>
        /// <param name="version">The package version</param>
        /// <param name="privateAssets">Optional private assets specification</param>
        public void AddPackageReference(string packageName, string version, string privateAssets = null)
        {
            if (string.IsNullOrEmpty(packageName))
                throw new ArgumentNullException(nameof(packageName));

            if (string.IsNullOrEmpty(version))
                throw new ArgumentNullException(nameof(version));


            if (_packageReferences.Any(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase)))
            {
                var existingPackage =
                    _packageReferences.First(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
                existingPackage.Version = version;
                existingPackage.PrivateAssets = privateAssets;
            }
            else
            {
                _packageReferences.Add(new PackageReference
                {
                    Name = packageName,
                    Version = version,
                    PrivateAssets = privateAssets
                });
            }
        }

        /// <summary>
        /// Removes a NuGet package reference from the project
        /// </summary>
        /// <param name="packageName">The package name</param>
        /// <returns>True if the package was removed, false if it didn't exist</returns>
        public bool RemovePackageReference(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
                throw new ArgumentNullException(nameof(packageName));

            var package =
                _packageReferences.FirstOrDefault(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
            if (package != null)
            {
                return _packageReferences.Remove(package);
            }

            return false;
        }

        /// <summary>
        /// Adds a project reference to the project
        /// </summary>
        /// <param name="projectPath">The relative path to the referenced project</param>
        public void AddProjectReference(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
                throw new ArgumentNullException(nameof(projectPath));


            if (!_projectReferences.Any(p => p.Path.Equals(projectPath, StringComparison.OrdinalIgnoreCase)))
            {
                _projectReferences.Add(new ProjectReference
                {
                    Path = projectPath
                });
            }
        }

        /// <summary>
        /// Removes a project reference from the project
        /// </summary>
        /// <param name="projectPath">The relative path to the referenced project</param>
        /// <returns>True if the project reference was removed, false if it didn't exist</returns>
        public bool RemoveProjectReference(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
                throw new ArgumentNullException(nameof(projectPath));

            var reference =
                _projectReferences.FirstOrDefault(p => p.Path.Equals(projectPath, StringComparison.OrdinalIgnoreCase));
            if (reference != null)
            {
                return _projectReferences.Remove(reference);
            }

            return false;
        }

        /// <summary>
        /// Adds an assembly reference to the project
        /// </summary>
        /// <param name="assemblyName">The assembly name</param>
        /// <param name="hintPath">Optional hint path for the assembly</param>
        public void AddAssemblyReference(string assemblyName, string hintPath = null)
        {
            if (string.IsNullOrEmpty(assemblyName))
                throw new ArgumentNullException(nameof(assemblyName));


            if (_assemblyReferences.Any(a => a.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)))
            {
                var existingAssembly =
                    _assemblyReferences.First(a => a.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
                existingAssembly.HintPath = hintPath;
            }
            else
            {
                _assemblyReferences.Add(new AssemblyReference
                {
                    Name = assemblyName,
                    HintPath = hintPath
                });
            }
        }

        /// <summary>
        /// Removes an assembly reference from the project
        /// </summary>
        /// <param name="assemblyName">The assembly name</param>
        /// <returns>True if the assembly reference was removed, false if it didn't exist</returns>
        public bool RemoveAssemblyReference(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
                throw new ArgumentNullException(nameof(assemblyName));

            var reference =
                _assemblyReferences.FirstOrDefault(a =>
                    a.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
            if (reference != null)
            {
                return _assemblyReferences.Remove(reference);
            }

            return false;
        }

        #endregion

        #region Item Methods

        /// <summary>
        /// Adds a project item to the project
        /// </summary>
        /// <param name="itemType">The item type (e.g., "Compile", "Content", "None")</param>
        /// <param name="include">The include path</param>
        /// <param name="metadata">Optional metadata for the item</param>
        public void AddItem(string itemType, string include, Dictionary<string, string> metadata = null)
        {
            if (string.IsNullOrEmpty(itemType))
                throw new ArgumentNullException(nameof(itemType));

            if (string.IsNullOrEmpty(include))
                throw new ArgumentNullException(nameof(include));


            var existingItem = _items.FirstOrDefault(i =>
                i.ItemType.Equals(itemType, StringComparison.OrdinalIgnoreCase) &&
                i.Include.Equals(include, StringComparison.OrdinalIgnoreCase));

            if (existingItem != null)
            {
                if (metadata != null)
                {
                    foreach (var kvp in metadata)
                    {
                        existingItem.Metadata[kvp.Key] = kvp.Value;
                    }
                }
            }
            else
            {
                var item = new ProjectItem
                {
                    ItemType = itemType,
                    Include = include,
                    Metadata = metadata != null
                        ? new Dictionary<string, string>(metadata)
                        : new Dictionary<string, string>()
                };

                _items.Add(item);
            }
        }

        /// <summary>
        /// Removes a project item from the project
        /// </summary>
        /// <param name="itemType">The item type</param>
        /// <param name="include">The include path</param>
        /// <returns>True if the item was removed, false if it didn't exist</returns>
        public bool RemoveItem(string itemType, string include)
        {
            if (string.IsNullOrEmpty(itemType))
                throw new ArgumentNullException(nameof(itemType));

            if (string.IsNullOrEmpty(include))
                throw new ArgumentNullException(nameof(include));

            var item = _items.FirstOrDefault(i =>
                i.ItemType.Equals(itemType, StringComparison.OrdinalIgnoreCase) &&
                i.Include.Equals(include, StringComparison.OrdinalIgnoreCase));

            if (item != null)
            {
                return _items.Remove(item);
            }

            return false;
        }

        #endregion

        #region Framework-specific Methods

        /// <summary>
        /// Sets the project to use .NET Framework
        /// </summary>
        /// <param name="version">The .NET Framework version (e.g., "v4.7.2")</param>
        public void SetNetFramework(string version)
        {
            if (string.IsNullOrEmpty(version))
                throw new ArgumentNullException(nameof(version));

            if (!version.StartsWith("v"))
                version = "v" + version;

            _properties["TargetFrameworkVersion"] = version;
            _properties.Remove("TargetFramework");
            _properties.Remove("TargetFrameworks");
        }

        /// <summary>
        /// Sets the project to use ASP.NET Core
        /// </summary>
        /// <param name="version">The ASP.NET Core version (e.g., "8.0")</param>
        public void SetAspNetCore(string version)
        {
            if (string.IsNullOrEmpty(version))
                throw new ArgumentNullException(nameof(version));

            SetTargetFramework($"net{version}");
            AddPackageReference("Microsoft.AspNetCore.App", version);
        }

        /// <summary>
        /// Sets the project to use Windows Forms
        /// </summary>
        /// <param name="version">The .NET version (e.g., "8.0")</param>
        public void SetWindowsForms(string version)
        {
            if (string.IsNullOrEmpty(version))
                throw new ArgumentNullException(nameof(version));

            SetTargetFramework($"net{version}-windows");
            SetProperty("UseWindowsForms", "true");
            SetOutputType("WinExe");
        }

        /// <summary>
        /// Sets the project to use WPF
        /// </summary>
        /// <param name="version">The .NET version (e.g., "8.0")</param>
        public void SetWpf(string version)
        {
            if (string.IsNullOrEmpty(version))
                throw new ArgumentNullException(nameof(version));

            SetTargetFramework($"net{version}-windows");
            SetProperty("UseWPF", "true");
            SetOutputType("WinExe");
        }

        /// <summary>
        /// Sets the project to use Blazor WebAssembly
        /// </summary>
        /// <param name="version">The .NET version (e.g., "8.0")</param>
        public void SetBlazorWebAssembly(string version)
        {
            if (string.IsNullOrEmpty(version))
                throw new ArgumentNullException(nameof(version));

            SetTargetFramework($"net{version}");
            AddPackageReference("Microsoft.AspNetCore.Components.WebAssembly", version);
            AddPackageReference("Microsoft.AspNetCore.Components.WebAssembly.DevServer", version,
                "runtime; build; native");
        }

        /// <summary>
        /// Sets the project to use Blazor Server
        /// </summary>
        /// <param name="version">The .NET version (e.g., "8.0")</param>
        public void SetBlazorServer(string version)
        {
            if (string.IsNullOrEmpty(version))
                throw new ArgumentNullException(nameof(version));

            SetTargetFramework($"net{version}");
            AddPackageReference("Microsoft.AspNetCore.App", version);
        }

        /// <summary>
        /// Sets the project to use Xamarin/MAUI
        /// </summary>
        /// <param name="version">The .NET MAUI version (e.g., "8.0")</param>
        public void SetMaui(string version)
        {
            if (string.IsNullOrEmpty(version))
                throw new ArgumentNullException(nameof(version));

            SetTargetFramework($"net{version}-android;net{version}-ios;net{version}-maccatalyst");
            SetProperty("UseMaui", "true");
            AddPackageReference("Microsoft.Maui.Controls", version);
        }

        #endregion

        /// <summary>
        /// Saves the project file
        /// </summary>
        /// <param name="outputPath">Optional output path for the file (defaults to the original path)</param>
        public void SaveProject(string outputPath = null)
        {
            if (string.IsNullOrEmpty(outputPath))
                outputPath = _projectPath;


            _rootElement.RemoveAll();


            var propertyGroup = new XElement("PropertyGroup");
            foreach (var property in _properties)
            {
                propertyGroup.Add(new XElement(property.Key, property.Value));
            }

            _rootElement.Add(propertyGroup);


            if (_packageReferences.Count > 0)
            {
                var packageItemGroup = new XElement("ItemGroup");
                foreach (var package in _packageReferences)
                {
                    var packageElement = new XElement("PackageReference",
                        new XAttribute("Include", package.Name),
                        new XAttribute("Version", package.Version));

                    if (!string.IsNullOrEmpty(package.PrivateAssets))
                    {
                        packageElement.Add(new XAttribute("PrivateAssets", package.PrivateAssets));
                    }

                    packageItemGroup.Add(packageElement);
                }

                _rootElement.Add(packageItemGroup);
            }


            if (_projectReferences.Count > 0)
            {
                var projectRefItemGroup = new XElement("ItemGroup");
                foreach (var projectRef in _projectReferences)
                {
                    projectRefItemGroup.Add(new XElement("ProjectReference",
                        new XAttribute("Include", projectRef.Path)));
                }

                _rootElement.Add(projectRefItemGroup);
            }


            if (_assemblyReferences.Count > 0)
            {
                var refItemGroup = new XElement("ItemGroup");
                foreach (var reference in _assemblyReferences)
                {
                    var refElement = new XElement("Reference",
                        new XAttribute("Include", reference.Name));

                    if (!string.IsNullOrEmpty(reference.HintPath))
                    {
                        refElement.Add(new XElement("HintPath", reference.HintPath));
                    }

                    refItemGroup.Add(refElement);
                }

                _rootElement.Add(refItemGroup);
            }


            if (_items.Count > 0)
            {
                var itemsGroup = new XElement("ItemGroup");
                foreach (var item in _items)
                {
                    var itemElement = new XElement(item.ItemType,
                        new XAttribute("Include", item.Include));

                    foreach (var metadata in item.Metadata)
                    {
                        itemElement.Add(new XElement(metadata.Key, metadata.Value));
                    }

                    itemsGroup.Add(itemElement);
                }

                _rootElement.Add(itemsGroup);
            }


            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }


            _projectDocument.Save(outputPath);
        }

        /// <summary>
        /// Creates a new console application project
        /// </summary>
        /// <param name="projectPath">The path where the project file will be saved</param>
        /// <param name="targetFramework">The target framework (e.g., "net8.0")</param>
        /// <returns>The created ProjectFileGenerator instance</returns>
        public static ProjectFileGenerator CreateConsoleApp(string projectPath, string targetFramework = "net8.0")
        {
            var generator = new ProjectFileGenerator(projectPath);
            generator.SetTargetFramework(targetFramework);
            generator.SetOutputType("Exe");
            return generator;
        }

        /// <summary>
        /// Creates a new class library project
        /// </summary>
        /// <param name="projectPath">The path where the project file will be saved</param>
        /// <param name="targetFramework">The target framework (e.g., "net8.0")</param>
        /// <returns>The created ProjectFileGenerator instance</returns>
        public static ProjectFileGenerator CreateClassLibrary(string projectPath, string targetFramework = "net8.0")
        {
            var generator = new ProjectFileGenerator(projectPath);
            generator.SetTargetFramework(targetFramework);
            generator.SetOutputType("Library");
            return generator;
        }

        /// <summary>
        /// Creates a new ASP.NET Core Web API project
        /// </summary>
        /// <param name="projectPath">The path where the project file will be saved</param>
        /// <param name="targetFramework">The target framework version number (e.g., "8.0")</param>
        /// <returns>The created ProjectFileGenerator instance</returns>
        public static ProjectFileGenerator CreateWebApi(string projectPath, string targetFramework = "8.0")
        {
            var generator = new ProjectFileGenerator(projectPath, "Microsoft.NET.Sdk.Web");
            generator.SetTargetFramework($"net{targetFramework}");
            generator.SetOutputType("Exe");
            return generator;
        }

        /// <summary>
        /// Creates a new ASP.NET Core MVC project
        /// </summary>
        /// <param name="projectPath">The path where the project file will be saved</param>
        /// <param name="targetFramework">The target framework version number (e.g., "8.0")</param>
        /// <returns>The created ProjectFileGenerator instance</returns>
        public static ProjectFileGenerator CreateMvcApp(string projectPath, string targetFramework = "8.0")
        {
            var generator = new ProjectFileGenerator(projectPath, "Microsoft.NET.Sdk.Web");
            generator.SetTargetFramework($"net{targetFramework}");
            generator.SetOutputType("Exe");
            return generator;
        }

        /// <summary>
        /// Creates a new Windows Forms project
        /// </summary>
        /// <param name="projectPath">The path where the project file will be saved</param>
        /// <param name="targetFramework">The target framework version number (e.g., "8.0")</param>
        /// <returns>The created ProjectFileGenerator instance</returns>
        public static ProjectFileGenerator CreateWindowsFormsApp(string projectPath, string targetFramework = "8.0")
        {
            var generator = new ProjectFileGenerator(projectPath);
            generator.SetWindowsForms(targetFramework);
            return generator;
        }

        /// <summary>
        /// Creates a new WPF project
        /// </summary>
        /// <param name="projectPath">The path where the project file will be saved</param>
        /// <param name="targetFramework">The target framework version number (e.g., "8.0")</param>
        /// <returns>The created ProjectFileGenerator instance</returns>
        public static ProjectFileGenerator CreateWpfApp(string projectPath, string targetFramework = "8.0")
        {
            var generator = new ProjectFileGenerator(projectPath);
            generator.SetWpf(targetFramework);
            return generator;
        }
    }

    /// <summary>
    /// Represents a NuGet package reference in a project
    /// </summary>
    public class PackageReference
    {
        /// <summary>
        /// Gets or sets the package name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the package version
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the private assets specification
        /// </summary>
        public string PrivateAssets { get; set; }
    }

    /// <summary>
    /// Represents a project reference in a project
    /// </summary>
    public class ProjectReference
    {
        /// <summary>
        /// Gets or sets the relative path to the referenced project
        /// </summary>
        public string Path { get; set; }
    }

    /// <summary>
    /// Represents an assembly reference in a project
    /// </summary>
    public class AssemblyReference
    {
        /// <summary>
        /// Gets or sets the assembly name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the hint path for the assembly
        /// </summary>
        public string HintPath { get; set; }
    }

    /// <summary>
    /// Represents a project item in a project
    /// </summary>
    public class ProjectItem
    {
        /// <summary>
        /// Gets or sets the item type
        /// </summary>
        public string ItemType { get; set; }

        /// <summary>
        /// Gets or sets the include path
        /// </summary>
        public string Include { get; set; }

        /// <summary>
        /// Gets or sets the metadata for the item
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }
}