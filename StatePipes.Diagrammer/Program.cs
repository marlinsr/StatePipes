// See https://aka.ms/new-console-template for more information
using StatePipes.Diagrammer;
using System.Diagnostics;
using System.Reflection;
string classLibraryPath = string.Empty;
string solutionDir = string.Empty;
string solutionFileName = string.Empty;
string projectName = string.Empty;

try
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].Equals("-c", StringComparison.CurrentCultureIgnoreCase)) classLibraryPath = args[++i];
        if (args[i].Equals("-r", StringComparison.CurrentCultureIgnoreCase)) solutionDir = args[++i];
        if (args[i].Equals("-s", StringComparison.CurrentCultureIgnoreCase)) solutionFileName = args[++i];
        if (args[i].Equals("-p", StringComparison.CurrentCultureIgnoreCase)) projectName = args[++i];
    }
    if (projectName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) projectName = Path.GetFileNameWithoutExtension(projectName);
    System.Console.WriteLine($"Class Library Path = {classLibraryPath}");
    System.Console.WriteLine($"Solution Directory = {solutionDir}");
    System.Console.WriteLine($"Solution File Name = {solutionFileName}");   
    System.Console.WriteLine($"Project Name = {projectName}");
    if (!string.IsNullOrEmpty(solutionDir) && !string.IsNullOrEmpty(solutionFileName)) 
        if(!BuildSolution(solutionDir, solutionFileName)) return;
    if (!string.IsNullOrEmpty(solutionDir) && !string.IsNullOrEmpty(projectName)) classLibraryPath = RepointToServiceDirectory(solutionDir, projectName, classLibraryPath) ?? classLibraryPath;
    Console.WriteLine($"Target = {classLibraryPath}");
    var programDataDirectory = $@"{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}\StatePipes\{Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly()!.Location)}";
    Directory.CreateDirectory(programDataDirectory);
    Console.WriteLine($"Ouput Path = {programDataDirectory}");
    StateMachinePdfCreator.CreateStateMachineDiagrams(programDataDirectory, classLibraryPath);
}
catch (Exception e)
{
    Console.WriteLine(e.Message.ToString());
}

static string? RepointToServiceDirectory(string solutionDir, string projectName, string targetDirectory)
{
    const string serviceSuffix = ".Service";
    if (projectName.EndsWith(serviceSuffix, StringComparison.OrdinalIgnoreCase)) return targetDirectory;
    var configuration = RemovePrefixDirectory(RemovePrefixDirectory(RemovePrefixDirectory(targetDirectory, Path.Combine(solutionDir, projectName)), "obj"), "bin");
    return Path.Combine(solutionDir, $"{projectName}{serviceSuffix}", "bin", configuration);
}
static string RemovePrefixDirectory(string fullPath, string prefixPath)
{
    var normalizedFullPath = Path.GetFullPath(fullPath);
    var normalizedPrefixPath = Path.GetFullPath(prefixPath);
    if (normalizedFullPath.StartsWith(normalizedPrefixPath, StringComparison.OrdinalIgnoreCase))
    {
        var relativePath = normalizedFullPath[normalizedPrefixPath.Length..];
        //Trim any leading directory separators from the result
        return relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    return fullPath;
}
static bool BuildSolution(string solutionDir, string solutionFileName)
{
    var solutionPath = Path.Combine(solutionDir, solutionFileName);
    Console.WriteLine($"Building solution: {solutionPath}");
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{solutionPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    process.Start();
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        Console.WriteLine("Build failed!");
        Console.WriteLine(output);
        Console.WriteLine(error);
        return false;
    }
    Console.WriteLine("Build succeeded.");
    return true;
}