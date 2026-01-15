// See https://aka.ms/new-console-template for more information
using StatePipes.Diagrammer;
using System.Reflection;
string classLibraryPath = string.Empty;
try
{
    for (int i = 0; i < args.Length; i++) if (args[i].ToLower() == "-c") classLibraryPath = args[++i];
    Console.WriteLine($"Target = {classLibraryPath}");
    var programDataDirectory = $@"{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}\StatePipes\{Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly()!.Location)}";
    Directory.CreateDirectory(programDataDirectory);
    Console.WriteLine($"Ouput Path = {programDataDirectory}");
    StateMachinePdfCreator.CreateStateMachineDiagrams(programDataDirectory, classLibraryPath);
}
catch (Exception e)
{
    Console.WriteLine(e.Message.ToString());
    Console.WriteLine("Ensure class library has been succesfully compiled before using.");
}