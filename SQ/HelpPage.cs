using System;
using System.IO;

partial class Program
{
    const string reset = "\u001b[0m";
    const string yellow = "\u001b[33m";
    const string green = "\u001b[32m";
    const string cyan = "\u001b[36m";
    const string magenta = "\u001b[35m";
    const string darkMagenta = "\u001b[95m";
    const string darkYellow = "\u001b[33;2m";
    
    private static void HelpPage(string? page)
    {
        // Console.Clear();
        string processName = Path.GetFileName(Environment.ProcessPath) ?? "SPC.exe";

        page = page?.ToLower().Trim();
        switch (page)
        {
            case "new":
                Console.WriteLine($"Usage: {yellow}{processName}{reset} new <{magenta}FolderName{reset}>");
                Console.WriteLine();
                Console.WriteLine("Description:");
                Console.WriteLine("\tCreates a new shared project with the specified name.");
                Console.WriteLine();
                Console.WriteLine("Arguments:");
                Console.WriteLine($"\t<{magenta}FolderName{reset}>     - Name for the project folder. If omitted format");
                Console.WriteLine($"\t                 {darkYellow}\"Shared.Project<Id>\"{reset} will be automatically generated where <Id>");
                Console.WriteLine("\t                 is a unique identifier");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine($"\t{yellow}{processName}{reset} new {darkYellow}PluginA{reset}");
                Console.WriteLine($"\t{yellow}{processName}{reset} new {darkYellow}\"New Plugin Project\"{reset}");
                Console.WriteLine($"\t{yellow}{processName}{reset} new");
                break;
            case "release":
                Console.WriteLine($"Usage: {processName} release <FolderName:String> [<ZipPath:String>]");
                Console.WriteLine();
                Console.WriteLine("Description:");
                Console.WriteLine("\tPrepares a project for release by zipping the compiled files . This command is");
                Console.WriteLine(
                    "\tcurrently optimized for Koikatsu projects, but may support other targets in the future.");
                Console.WriteLine();
                Console.WriteLine("Arguments:");
                Console.WriteLine("\t<FolderName>     - Path to the project folder or file that should be prepared for release.");
                Console.WriteLine("\t[<ZipPath>]      - Optional. Custom path where the zip file should be created.");
                Console.WriteLine("\t                    If omitted, the zip file will be created in the projects parent directory");
                Console.WriteLine("\t                    with the project name as the file name.");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine($"\t{processName} release MyProject");
                Console.WriteLine($"\t{processName} release \"My Project\" \"C:\\Releases\\My Project Release.zip\"");
                break;
            case "link":
                Console.WriteLine($"Usage: {processName} link <ProjectPath:String> [<Configuration:String>]");
                Console.WriteLine();
                Console.WriteLine("Description:");
                Console.WriteLine(
                    "\tCreates a symbolic link between your project and the target installation directory");
                Console.WriteLine(
                    "\tspecified in your configuration. This allows for easy testing of your project in the");
                Console.WriteLine("\ttarget environment without manual copying of files");
                Console.WriteLine();
                Console.WriteLine("Arguments:");
                Console.WriteLine("\t<ProjectPath>           - Path to the project folder that should be linked");
                Console.WriteLine("\t[<Configuration>]       - Optional. Specifies which configuration to use for linking.");
                Console.WriteLine("\t                   If omitted, the default configuration will be used as found.");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine($"\t{processName} link MyProject");
                Console.WriteLine($"\t{processName} link \"My Project\" debug");
                Console.WriteLine("\t{processName} link MyProject release");
                break;
            case "unlink":
                Console.WriteLine($"Usage: {processName} unlink <ProjectPath:String>");
                Console.WriteLine();
                Console.WriteLine("Description:");
                Console.WriteLine(
                    "\tRemoves symbolic links previously created with the 'link' command. This disconnects");
                Console.WriteLine(
                    "\tyour project from the target installation directory without affecting the original");
                Console.WriteLine("\tproject files.");
                Console.WriteLine();
                Console.WriteLine("Arguments:");
                Console.WriteLine("\t<ProjectPath>           - Path to the project folder whose links should be removed");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine($"\t{processName} unlink MyProject");
                Console.WriteLine($"\t{processName} unlink \"My Project\"");
                break;
            default:
                Console.WriteLine($"{yellow}{processName}{reset} new <FolderName>");
                Console.WriteLine(
                    "\tCreates a new shared project with specified name or auto generated name if omitted");
                Console.WriteLine();
                Console.WriteLine($"{yellow}{processName}{reset} release <FolderName> [<ZipPath>]");
                Console.WriteLine(
                    "\tPrepares a project for release by creating a distribution package [Koikatsu Optimized]");
                Console.WriteLine();
                Console.WriteLine($"{yellow}{processName}{reset} link <ProjectPath> [<Configuration>]");
                Console.WriteLine("\tCreates symbolic links in target directories based on configuration settings");
                Console.WriteLine();
                Console.WriteLine($"{yellow}{processName}{reset} unlink <ProjectPath>");
                Console.WriteLine("\tRemoves previously created symbolic links while preserving project files");
                break;
        }
    }
}