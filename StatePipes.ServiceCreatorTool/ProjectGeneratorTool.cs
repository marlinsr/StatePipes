namespace StatePipes.ServiceCreatorTool
{
    internal class ProjectGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
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
            _ = helper.SaveTextFile("Tests_runsettings.sample", "Tests.runsettings");
            helper.MoveTo("RunScript");
            _ = helper.SaveTextFile("DockerStart_ps1.sample", "@#$ClassLibraryName@#$.Service.DockerStart.ps1");
            _ = helper.SaveTextFile("DockerStop_ps1.sample", "@#$ClassLibraryName@#$.Service.DockerStop.ps1");
            helper.Inject("ServiceStart_ps1.sample", "Start.ps1", solutionInjection1Moniker);
            helper.Inject("ServiceStop_ps1.sample", "Stop.ps1", solutionInjection1Moniker);
            helper.MoveUp();
            helper.MoveTo("BuildScript");
            _ = helper.SaveTextFile("LocalImageBuild_ps1.sample", "@#$ClassLibraryName@#$.Service.LocalImageBuild.ps1");
            //Class library
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            _ = helper.SaveTextFile("ClassLibrary_csproj.sample", "@#$ClassLibraryName@#$.csproj");
            helper.MoveTo("Docs");
            _ = helper.SaveTextFile("ReadMe_md.sample", "ReadMe.md");
            helper.MoveUp();
            helper.MoveTo("Builders");
            _ = helper.SaveTextFile("DefaultSetup_cs.sample", "DefaultSetup.cs");
            _ = helper.SaveTextFile("DefaultServiceConfiguration_cs.sample", "DefaultServiceConfiguration.cs");
            helper.MoveUp();
            helper.MoveTo("Events");
            _ = helper.SaveTextFile("DummyEvent_cs.sample", "DummyEvent.cs");
            _ = helper.SaveTextFile("CurrentStatusEvent_cs.sample", "CurrentStatusEvent.cs");
            helper.MoveUp();
            helper.MoveTo("ValueObjects");
            _ = helper.SaveTextFile("ProxyMonikers_cs.sample", "ProxyMonikers.cs");
            helper.MoveUp();
            helper.MoveTo("StateMachines");
            helper.MoveTo("@#$StateMachineName@#$");
            _ = helper.SaveTextFile("StateMachine_cs.sample", "@#$StateMachineName@#$.cs");
            helper.MoveTo("States");
            _ = helper.SaveTextFile("ParentState_cs.sample", "ParentState.cs");
            helper.MoveUp();
            helper.MoveTo("Triggers");
            _ = helper.SaveTextFile("DummyTrigger_cs.sample", "DummyTrigger.cs");
            _ = helper.SaveTextFile("SendCurrentStatusCommand_cs.sample", "SendCurrentStatusCommand.cs");
            helper.MoveTo("Internal");
            _ = helper.SaveTextFile("ProxyConnectionStatusTrigger_cs.sample", "ProxyConnectionStatusTrigger.cs");
            // Service
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$.Service");
            _ = helper.SaveTextFile("appsettings_Development_json.sample", "appsettings.Development.json");
            _ = helper.SaveTextFile("appsettings_json.sample", "appsettings.json");
            _ = helper.SaveTextFile("Dockerfile.sample", "Dockerfile");
            _ = helper.SaveTextFile("Service_csproj.sample", "@#$ClassLibraryName@#$.Service.csproj");
            _ = helper.SaveTextFile("Program_cs.sample", "Program.cs");
            helper.MoveTo("Properties");
            _ = helper.SaveTextFile("launchSettings_json.sample", "launchSettings.json");
            // Test
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$.Test");
            _ = helper.SaveTextFile("Test_csproj.sample", "@#$ClassLibraryName@#$.Test.csproj");
            _ = helper.SaveTextFile("DummyDependencyRegistration_cs.sample", "DummyDependencyRegistration.cs");
            _ = helper.SaveTextFile("StateMachineDummyTests_cs.sample", "StateMachineDummyTests.cs");
            _ = helper.SaveTextFile("TestCategories_cs.sample", "TestCategories.cs");
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
