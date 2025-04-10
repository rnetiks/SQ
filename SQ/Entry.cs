using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SPC;

partial class Program
{
    private static readonly Dictionary<string, Func<string[], Task<int>>> _commands = new()
    {
        {
            "help", async args =>
            {
                HelpPage(args.Length > 1 ? args[1] : null);
                return 0;
            }
        },
        { "new", CreateProjectAsync },
        { "release", async args => await ZipProjectAsync(args) },
        { "link", async _ => await LinkProjectAsync() },
        { "unlink", async _ => await UnlinkProjectAsync() }
    };


    private static readonly ConcurrentDictionary<string, string> _configCache = new();


    private const string CONFIG_USE_KOIKATSU = "useKoikatsu";
    private const string CONFIG_OUTPUT_PATH = "outputPath";
    private const string CONFIG_OUTPUT_METHOD = "outputMethod";
    private const string CONFIG_SCP_USERNAME = "scpUsername";
    private const string CONFIG_SCP_PASSWORD = "scpPassword";
    private const string CONFIG_SCP_PORT = "scpPort";
    private const string CONFIG_SCP_ADDRESS = "scpAddress";
    private const string CONFIG_SCP_DIRECTORY = "scpDirectory";
    private const string CONFIG_LINK_TARGET = "linkTarget";


    private static readonly Regex SystemLibraryRegex = new(
        @"^(System\.|Microsoft\.|Unity|mscorlib|netstandard|0Harmony|BepInEx|IllusionLibs|Sirenix)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);


    private static readonly int MaxDegreeOfParallelism = Environment.ProcessorCount;
    private const int BufferSize = 81920;

    public static string GetConfigValue(string key)
    {
        return _configCache.GetOrAdd(key, k =>
        {
            try
            {
                var iniPath = Path.Combine(new FileInfo(Environment.ProcessPath).Directory.FullName, "Config.ini");
                if (!File.Exists(iniPath))
                {
                    using var fs = File.Create(iniPath);
                    fs.Write(Encoding.ASCII.GetBytes($"{CONFIG_USE_KOIKATSU}=false\n"));
                    fs.Write(Encoding.ASCII.GetBytes($"{CONFIG_OUTPUT_PATH}=\n"));
                    fs.Write(Encoding.ASCII.GetBytes($"{CONFIG_OUTPUT_METHOD}=local\n"));
                    fs.Write(Encoding.ASCII.GetBytes($"{CONFIG_SCP_USERNAME}=\n"));
                    fs.Write(Encoding.ASCII.GetBytes($"{CONFIG_SCP_PASSWORD}=\n"));
                    fs.Write(Encoding.ASCII.GetBytes($"{CONFIG_SCP_PORT}=\n"));
                    fs.Write(Encoding.ASCII.GetBytes($"{CONFIG_SCP_ADDRESS}=\n"));
                    fs.Write(Encoding.ASCII.GetBytes($"{CONFIG_SCP_DIRECTORY}=\n"));
                    fs.Write(Encoding.ASCII.GetBytes($"{CONFIG_LINK_TARGET}=\n"));
                    return string.Empty;
                }


                using var reader = new StreamReader(iniPath);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex > 0 &&
                        line.AsSpan(0, separatorIndex).Trim().Equals(key, StringComparison.Ordinal))
                    {
                        return line.Substring(separatorIndex + 1).Trim();
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        });
    }

    static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length < 1)
            {
                HelpPage(null);
                return 1;
            }

            string command = args[0].ToLowerInvariant();
            if (_commands.TryGetValue(command, out var handler))
            {
                return await handler(args);
            }

            Console.WriteLine($"Unknown command: {command}");
            HelpPage(null);
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            return -1;
        }
    }

    private static async Task CleanDirectoryAsync(string directory)
    {
        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
        await Task.WhenAll(
            files.AsParallel()
                .WithDegreeOfParallelism(MaxDegreeOfParallelism)
                .Select(file => Task.Run(() =>
                {
                    try
                    {
                        if (File.Exists(file)) File.Delete(file);
                    }
                    catch
                    {
                        /* Ignore errors during cleanup */
                    }
                }))
        );


        var directories = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
            .Select(dir => new { Path = dir, Depth = dir.Count(c => c == Path.DirectorySeparatorChar) })
            .OrderByDescending(x => x.Depth)
            .Select(x => x.Path)
            .ToArray();

        await Task.WhenAll(
            directories.Select(dir => Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                catch
                {
                    /* Ignore errors during cleanup */
                }
            }))
        );
    }

    private static async Task<int> CreateProjectAsync(string[] args)
    {
        string projectName = args.Length > 1 ? args[1] : "Shared.Project";

        var duplicateFolderCount = GetDuplicateFolderCount(projectName);
        if (duplicateFolderCount > 0)
        {
            projectName += $" ({duplicateFolderCount})";
        }

        projectName = projectName.Trim().Replace("/", "\\");
        if (projectName.EndsWith("\\"))
            projectName = projectName.Substring(0, projectName.Length - 1);


        Directory.CreateDirectory(projectName);

        var escapedName = projectName.Substring(Math.Max(0, projectName.LastIndexOf('\\') + 1));


        var projectGuid = Guid.NewGuid().ToString("B");
        var tasks = new List<Task>
        {
            File.WriteAllTextAsync(
                Path.Combine(projectName, $"{escapedName}.shproj"),
                ReadEmbed("Test.EmbeddedData.shproj.txt")
                    .Replace("{PROJECT_NAME}", escapedName)
                    .Replace("{GUID}", projectGuid)),

            File.WriteAllTextAsync(
                Path.Combine(projectName, $"{escapedName}.projitems"),
                ReadEmbed("Test.EmbeddedData.projitems.txt")
                    .Replace("{PROJECT_NAME}", escapedName)
                    .Replace("{GUID}", projectGuid))
        };

        await Task.WhenAll(tasks);

        var solution = await GetSolutionPathAsync(Environment.CurrentDirectory);
        if (solution != null)
        {
            SolutionParser parser = new SolutionParser(solution);
            parser.Parse();

            var splitPath = projectName.Split('\\');
            var rootFolderGuid = splitPath.Aggregate(string.Empty,
                (current, dir) => current != string.Empty ? parser.AddFolder(dir, current) : parser.AddFolder(dir));

            parser.AddProject(escapedName, Path.Combine(projectName, $"{escapedName}.shproj"),
                parentFolderGuid: rootFolderGuid);

            var configValueKoi = GetConfigValue(CONFIG_USE_KOIKATSU);
            Console.WriteLine($"Using Koikatsu: {configValueKoi}");
            if (configValueKoi == "true")
            {
                var kksTask = CreateProjectTypeAsync(parser, projectName, escapedName, rootFolderGuid, ProjectType.KKS);
                var kkTask = CreateProjectTypeAsync(parser, projectName, escapedName, rootFolderGuid, ProjectType.KK);

                await Task.WhenAll(kksTask, kkTask);
            }

            parser.SaveSolution();
            Console.WriteLine($"Project '{escapedName}' created successfully.");
            return 0;
        }
        else
        {
            Console.WriteLine("No solution file found.");
            return 1;
        }
    }

    private static async Task CreateProjectTypeAsync(SolutionParser parser, string projectName, string escapedName,
        string rootFolderGuid, ProjectType type)
    {
        string suffix = type.ToString();
        string projectDir = Path.Combine(projectName, $"{projectName}.{suffix}");
        Directory.CreateDirectory(projectDir);

        string projectPath = Path.Combine(projectDir, $"{escapedName}.{suffix}.csproj");
        ProjectFileGenerator generator = new ProjectFileGenerator(projectPath);

        SetupProject(generator, $"{escapedName}.{suffix}", type);
        generator.SaveProject();

        parser.AddProject($"{escapedName}.{suffix}",
            Path.Combine($"{escapedName}/{projectName}.{suffix}", $"{escapedName}.{suffix}.csproj"),
            parentFolderGuid: rootFolderGuid);
    }

    private enum ProjectType
    {
        KK,
        KKS
    }

    private static void SetupProject(ProjectFileGenerator generator, string projectName, ProjectType projectType)
    {
        generator.SetOutputType("Library");
        generator.SetAssemblyName(projectName);
        generator.SetRootNamespace(projectName);
        generator.SetProperty("FileAlignment", "512");
        generator.SetProperty("WarningLevel", "4");
        generator.SetProperty("AllowUnsafeBlocks", "false");


        generator.SetProperty("DebugSymbols", "true");
        generator.SetProperty("DebugType", "full");
        generator.SetProperty("Optimize", "false");
        generator.SetProperty("DefineConstants", "DEBUG;TRACE");


        generator.SetProperty("DebugType", "pdbonly");
        generator.SetProperty("Optimize", "true");
        generator.SetProperty("DefineConstants", "TRACE");


        switch (projectType)
        {
            case ProjectType.KK:
                generator.SetNetFramework("3.5");
                AddKKReferences(generator);
                break;
            case ProjectType.KKS:
                generator.SetNetFramework("4.6.1");
                AddKKSReferences(generator);
                break;
        }
    }

    private static void AddKKReferences(ProjectFileGenerator generator)
    {
        generator.AddAssemblyReference("System");


        generator.AddPackageReference("IllusionLibs.BepInEx", "5.4.22");
        generator.AddPackageReference("IllusionLibs.BepInEx.Harmony", "2.9.0");
        generator.AddPackageReference("IllusionLibs.Koikatu.Assembly-CSharp", "2019.4.27.4");
        generator.AddPackageReference("IllusionLibs.Koikatu.Assembly-CSharp-firstpass", "2019.4.27.4");
        generator.AddPackageReference("IllusionLibs.Koikatu.UnityEngine", "5.6.2.4");
        generator.AddPackageReference("IllusionLibs.Koikatu.UnityEngine.UI", "5.6.2.4");
        generator.AddPackageReference("IllusionModdingAPI.KKAPI", "1.42.2");
        generator.AddPackageReference("Microsoft.Unity.Analyzers", "1.18.0");
        generator.AddPackageReference("KoikatuCompatibilityAnalyzer", "1.1.0");
    }

    private static void AddKKSReferences(ProjectFileGenerator generator)
    {
        generator.AddAssemblyReference("System");


        generator.AddPackageReference("IllusionLibs.BepInEx", "5.4.22");
        generator.AddPackageReference("IllusionLibs.BepInEx.Harmony", "2.9.0");
        generator.AddPackageReference("IllusionLibs.KoikatsuSunshine.Assembly-CSharp", "2021.9.17");
        generator.AddPackageReference("IllusionLibs.KoikatsuSunshine.Assembly-CSharp-firstpass", "2021.9.17");
        generator.AddPackageReference("IllusionLibs.KoikatsuSunshine.UnityEngine.CoreModule", "2019.4.9");
        generator.AddPackageReference("IllusionLibs.KoikatsuSunshine.UnityEngine.IMGUIModule", "2019.4.9");
        generator.AddPackageReference("IllusionLibs.KoikatsuSunshine.UnityEngine.InputLegacyModule", "2019.4.9");
        generator.AddPackageReference("IllusionLibs.KoikatsuSunshine.UnityEngine.UI", "2019.4.9");
        generator.AddPackageReference("IllusionLibs.KoikatsuSunshine.UnityEngine.UIModule", "2019.4.9");
        generator.AddPackageReference("IllusionLibs.KoikatsuSunshine.UniRx", "2021.9.17");
        generator.AddPackageReference("IllusionLibs.KoikatsuSunshine.UniTask", "2021.9.17");
        generator.AddPackageReference("IllusionModdingAPI.KKSAPI", "1.42.2");
        generator.AddPackageReference("Microsoft.Unity.Analyzers", "1.18.0");
    }

    private static async Task<int> ZipProjectAsync(string[] args)
    {
        try
        {
            string currentDir = Environment.CurrentDirectory;
            string projectName;
            projectName = args.Length > 1 ? args[1] : Path.GetFileName(currentDir);


            string outputPath = GetConfigValue(CONFIG_OUTPUT_PATH);
            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = currentDir;
            }


            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string zipFileName = $"{projectName}_{timestamp}.zip";
            string zipFilePath = Path.Combine(outputPath, zipFileName);


            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);


            Console.WriteLine($"Creating zip file: {zipFilePath}");
            await CreateZipFromProjectAsync(currentDir, zipFilePath);


            string outputMethod = GetConfigValue(CONFIG_OUTPUT_METHOD);
            if (outputMethod == "scp")
            {
                await UploadViaScpAsync(zipFilePath);
            }

            Console.WriteLine("Release completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating release: {ex.Message}");
            return 1;
        }
    }

    private static async Task CreateZipFromProjectAsync(string projectDir, string zipFilePath)
    {
        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }


        var outputDirs = await GetOutputDirectoriesAsync(projectDir);
        // TODO Search for direct files and use outputDirs as the new folder name in the zipped file

        if (outputDirs.Count == 0)
        {
            throw new InvalidOperationException("No output directories found. Build the project first.");
        }


        using (var zipStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                   BufferSize, FileOptions.Asynchronous))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var filesToProcess = new ConcurrentBag<(string FilePath, string EntryName)>();


            await Task.WhenAll(outputDirs.Select(async outputDir =>
            {
                if (!Directory.Exists(outputDir))
                    return;


                var dllFiles = Directory.GetFiles(outputDir, "*.dll");
                var pdbFiles = Directory.GetFiles(outputDir, "*.pdb");

                foreach (var file in dllFiles.Concat(pdbFiles))
                {
                    string fileName = Path.GetFileName(file);
                    if (SystemLibraryRegex.IsMatch(fileName))
                        continue;

                    string entryName = Path.Combine(
                        Path.GetFileName(Path.GetDirectoryName(outputDir)),
                        fileName);

                    filesToProcess.Add((file, entryName));
                }
            }));


            foreach (var (filePath, entryName) in filesToProcess)
            {
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
    
                using var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    BufferSize, true);
                using var targetStream = entry.Open();
                await sourceStream.CopyToAsync(targetStream, BufferSize);
            }
        }
    }

    private static async Task UploadViaScpAsync(string filePath)
    {
        string username = GetConfigValue(CONFIG_SCP_USERNAME);
        string password = GetConfigValue(CONFIG_SCP_PASSWORD);
        string portStr = GetConfigValue(CONFIG_SCP_PORT);
        string host = GetConfigValue(CONFIG_SCP_ADDRESS);
        string remoteDir = GetConfigValue(CONFIG_SCP_DIRECTORY);

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(host))
        {
            Console.WriteLine("Missing SCP configuration. Please set the required values in Config.ini.");
            return;
        }

        int port = 22;
        if (!string.IsNullOrEmpty(portStr) && int.TryParse(portStr, out int parsedPort))
        {
            port = parsedPort;
        }

        Console.WriteLine($"Uploading {filePath} to {host}...");

        try
        {
            using var fileStream =
                new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);


            long fileSize = fileStream.Length;
            long bytesRead = 0;
            var buffer = new byte[BufferSize];
            int read;
            
            // TODO
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during SCP upload: {ex.Message}");
        }
    }

    private static async Task<int> LinkProjectAsync()
    {
        try
        {
            string targetDir = GetConfigValue(CONFIG_LINK_TARGET);
            if (string.IsNullOrEmpty(targetDir))
            {
                Console.WriteLine("Error: Target directory not specified. Please set 'linkTarget' in Config.ini.");
                return 1;
            }

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string projectDir = Environment.CurrentDirectory;


            var outputDirs = await GetOutputDirectoriesAsync(projectDir);

            if (outputDirs.Count == 0)
            {
                Console.WriteLine("No output directories found. Build the project first.");
                return 1;
            }


            var results = new ConcurrentBag<(bool Success, string Message, string SourceFile, string SymlinkPath)>();

            await Parallel.ForEachAsync(outputDirs,
                new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism },
                async (outputDir, token) =>
                {
                    if (!Directory.Exists(outputDir))
                        return;


                    try
                    {
                        var symbolic = new Symbolic(outputDir, targetDir, includeSubfolders: false);
                        symbolic.IncludePattern($"{Path.GetFileName(projectDir)}*.dll");

                        var result = symbolic.Execute();
                        if (result.Success)
                        {
                            results.Add((true, "Link created successfully", result.SourceFile, result.SymlinkPath));

                            if (result.PdbIncluded)
                            {
                                results.Add((true, "PDB link created successfully", result.PdbSourceFile,
                                    result.PdbSymlinkPath));
                            }
                        }
                        else
                        {
                            results.Add((false, $"Failed: {result.ErrorMessage}", null, null));
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add((false, $"Exception: {ex.Message}", null, null));
                    }
                });


            int successCount = 0;
            int failCount = 0;

            foreach (var (success, message, sourceFile, symlinkPath) in results)
            {
                if (success)
                {
                    Console.WriteLine($"Created link: {sourceFile} -> {symlinkPath}");
                    successCount++;
                }
                else
                {
                    Console.WriteLine($"Error: {message}");
                    failCount++;
                }
            }

            Console.WriteLine($"Linking completed. Created {successCount} links, failed {failCount}.");
            return failCount > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating links: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> UnlinkProjectAsync()
    {
        try
        {
            string targetDir = GetConfigValue(CONFIG_LINK_TARGET);
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                Console.WriteLine("Target directory not found.");
                return 1;
            }

            string projectDir = Environment.CurrentDirectory;
            string projectName = Path.GetFileName(projectDir);


            var files = Directory.GetFiles(targetDir);
            int removedCount = 0;
            int failedCount = 0;


            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism },
                async (file, token) =>
                {
                    if (!IsSymbolicLink(file))
                        return;


                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith(projectName, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(file);
                            Interlocked.Increment(ref removedCount);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to remove link {file}: {ex.Message}");
                            Interlocked.Increment(ref failedCount);
                        }
                    }
                });

            Console.WriteLine($"Unlinking completed. Removed {removedCount} links, failed to remove {failedCount}.");
            return failedCount > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing links: {ex.Message}");
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSymbolicLink(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }


    private static async Task<List<string>> GetOutputDirectoriesAsync(string projectDir)
    {
        var result = new List<string>();
        var subDirs = Directory.GetDirectories(projectDir);


        var dirTasks = subDirs.Select(subDir =>
        {
            var projectFiles = Directory.GetFiles(subDir, "*.csproj");

            return Task.FromResult((from projectFile in projectFiles
                select Path.GetDirectoryName(projectFile)
                into baseDir
                from config in new[] { "Debug", "Release" }
                select Path.Combine(baseDir, "bin", config)
                into outputDir
                where Directory.Exists(outputDir)
                select outputDir).ToList());
        });

        var allDirs = await Task.WhenAll(dirTasks);
        return allDirs.SelectMany(dirs => dirs).ToList();
    }

    public static string ReadEmbed(string embed)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(embed);
        if (stream != null)
        {
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        return "";
    }

    private static int GetDuplicateFolderCount(string identifier)
    {
        return Directory.GetDirectories(Environment.CurrentDirectory)
            .Count(e => Path.GetDirectoryName(e)?.StartsWith(identifier) == true);
    }

    private static async Task<string?> GetSolutionPathAsync(string path)
    {
        var solutionFiles = Directory.GetFiles(path, "*.sln");
        if (solutionFiles.Length > 0)
        {
            return solutionFiles[0];
        }


        var directoryInfo = Directory.GetParent(path);
        if (directoryInfo != null)
        {
            return await GetSolutionPathAsync(directoryInfo.FullName);
        }

        return null;
    }
}