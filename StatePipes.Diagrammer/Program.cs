// See https://aka.ms/new-console-template for more information
using StatePipes.Diagrammer;
using EnvDTE80;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
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
        if(!BuildSolution()) return;
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
static bool SendSaveAndBuild(DTE2 dte)
{
    bool buildSucceeded = true;
    CancellationTokenSource cts = new();
    dte.Events.BuildEvents.OnBuildDone += (scope, action) =>
    {
        if (buildSucceeded) Console.WriteLine("Build succeeded.");
        else Console.WriteLine("Build failed.");
        cts.Cancel();
    };
    dte.Events.BuildEvents.OnBuildProjConfigDone += (project, config, platform, solutionConfig, success) => { if (!success) buildSucceeded = false; };
    dte.ExecuteCommand("File.SaveAll");
    dte.ExecuteCommand("Build.BuildSolution");
    Console.WriteLine("Build command sent successfully.");
    // Wait for build to complete or timeout after 2 minutes
    try { Task.Delay(120000, cts.Token).Wait(); } catch { }
    return buildSucceeded;
}
static int GetParentPid()
{
    try
    {
        int currentPid = Environment.ProcessId;
        // Query WMI for the parent process ID associated with our current PID
        string query = $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {currentPid}";
        using ManagementObjectSearcher searcher = new(query);
        using var results = searcher.Get();
        foreach (var obj in results) return Convert.ToInt32(obj["ParentProcessId"]);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error retrieving parent PID: {ex.Message}");
    }
    return -1; // Return -1 if not found or error occurred
}
static bool BuildSolution()
{
    int devEnvPID = GetParentPid();
    if (devEnvPID <= 0) return false;
    var dte = ExternalDTE.GetDTE2(devEnvPID);
    if (dte == null) return false;
    try
    {
        return SendSaveAndBuild(dte);
    }
    catch (COMException ex)
    {
        Console.WriteLine("Could not find a running Visual Studio instance: " + ex.Message);
        return false;
    }
}