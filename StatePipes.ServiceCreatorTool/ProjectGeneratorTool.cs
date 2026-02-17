namespace StatePipes.ServiceCreatorTool
{
    internal class ProjectGeneratorTool : BaseToolGenerator
    {
        public ProjectGeneratorTool(string solutionDir, string solutionFileName) : base(solutionDir, solutionFileName) {}
        public void GenerateProject(string projectName)
        {
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            GenerateProjectFiles(helper);
        }
        public static void GenerateProjectFiles(GeneratorHelper helper)
        {
            helper.MoveToRootDirectory();
            helper.Inject("SolutionProjectInjectionPoint1_sln.sample", $"{solutionNameMoniker}.sln", solutionInjection1Moniker);
            helper.Inject("SolutionProjectInjectionPoint2_sln.sample", $"{solutionNameMoniker}.sln", solutionInjection2Moniker);
            helper.MoveToRootDirectory();
            //Solution Level
            helper.SaveTextFile("Tests_runsettings.sample", "Tests.runsettings");
            helper.MoveTo("RunScript");
            helper.SaveTextFile("DockerStart_ps1.sample", "@#$ClassLibraryName@#$.Service.DockerStart.ps1");
            helper.SaveTextFile("DockerStop_ps1.sample", "@#$ClassLibraryName@#$.Service.DockerStop.ps1");
            helper.Inject("ServiceStart_ps1.sample", "Start.ps1", solutionInjection1Moniker);
            helper.Inject("ServiceStop_ps1.sample", "Stop.ps1", solutionInjection1Moniker);
            helper.MoveUp();
            helper.MoveTo("BuildScript");
            helper.SaveTextFile("LocalImageBuild_ps1.sample", "@#$ClassLibraryName@#$.Service.LocalImageBuild.ps1");
            //Class library
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            helper.SaveTextFile("ClassLibrary_csproj.sample", "@#$ClassLibraryName@#$.csproj");
            helper.MoveTo("Docs");
            helper.SaveTextFile("ReadMe_md.sample", "ReadMe.md");
            helper.MoveUp();
            helper.MoveTo("Builders");
            helper.SaveTextFile("DefaultSetup_cs.sample", "DefaultSetup.cs");
            helper.SaveTextFile("DefaultServiceConfiguration_cs.sample", "DefaultServiceConfiguration.cs");
            helper.MoveUp();
            helper.MoveTo("Events");
            helper.SaveTextFile("DummyEvent_cs.sample", "DummyEvent.cs");
            helper.SaveTextFile("CurrentStatusEvent_cs.sample", "CurrentStatusEvent.cs");
            helper.MoveUp();
            helper.MoveTo("ValueObjects");
            helper.SaveTextFile("ProxyMonikers_cs.sample", "ProxyMonikers.cs");
            helper.MoveUp();
            helper.MoveTo("StateMachines");
            helper.MoveTo("@#$StateMachineName@#$");
            helper.SaveTextFile("StateMachine_cs.sample", "@#$StateMachineName@#$.cs");
            helper.MoveTo("States");
            helper.SaveTextFile("ParentState_cs.sample", "ParentState.cs");
            helper.MoveUp();
            helper.MoveTo("Triggers");
            helper.SaveTextFile("DummyTrigger_cs.sample", "DummyTrigger.cs");
            helper.SaveTextFile("SendCurrentStatusCommand_cs.sample", "SendCurrentStatusCommand.cs");
            helper.MoveTo("Internal");
            helper.SaveTextFile("ProxyConnectionStatusTrigger_cs.sample", "ProxyConnectionStatusTrigger.cs");
            // Service
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$.Service");
            helper.SaveTextFile("appsettings_Development_json.sample", "appsettings.Development.json");
            helper.SaveTextFile("appsettings_json.sample", "appsettings.json");
            helper.SaveTextFile("Dockerfile.sample", "Dockerfile");
            helper.SaveTextFile("Service_csproj.sample", "@#$ClassLibraryName@#$.Service.csproj");
            helper.SaveTextFile("Program_cs.sample", "Program.cs");
            helper.MoveTo("Properties");
            helper.SaveTextFile("launchSettings_json.sample", "launchSettings.json");
            // Test
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$.Test");
            helper.SaveTextFile("Test_csproj.sample", "@#$ClassLibraryName@#$.Test.csproj");
            helper.SaveTextFile("DummyDependencyRegistration_cs.sample", "DummyDependencyRegistration.cs");
            helper.SaveTextFile("StateMachineDummyTests_cs.sample", "StateMachineDummyTests.cs");
            helper.SaveTextFile("TestCategories_cs.sample", "TestCategories.cs");
        }
        public static void CreateNewProjects(string solutionDir, string solutionFileName)
        {
            var solutionName = GetSolutionNameNoExtension(solutionFileName);
            var projectName = GetSolutionNameNoPackages(solutionName);
            var defaultAnswer = $"{projectName}.";
            string answer = defaultAnswer;
            if (SelectionDialog.ShowInputDialog(ref answer, $"Enter the name of the project prefix for the service (no extension), ie: {projectName}.Vhw") == DialogResult.OK)
            {
                if (answer == defaultAnswer || string.IsNullOrEmpty(answer) || answer.TrimEnd().EndsWith('.'))
                {
                    Console.WriteLine($"Bad project name: {answer}");
                    return;
                }
                (new ProjectGeneratorTool(solutionDir, solutionFileName)).GenerateProject(answer);
            }
        }
    }
}
