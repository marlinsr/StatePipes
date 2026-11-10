namespace StatePipes.ServiceCreatorTool
{
    /// <summary>
    /// -w Add Workflow: a workflow marker with a first state and a final state. The first workflow in a service also gets a
    /// WorkflowStoreConfig in DefaultServiceConfiguration.cs and the PostgreSQL client-certificate copy in the service's
    /// post-build step; both insertions are idempotent.
    /// </summary>
    internal class WorkflowGeneratorTool(string solutionDir, string solutionFileName) : BaseToolGenerator(solutionDir, solutionFileName)
    {
        private const string SetupAnchor = "Type setup = typeof(DefaultSetup)!;";
        private const string LegacyArgsTail = "new ServiceArgs(null));";
        private const string PostgresCertFile = "statepipes.postgres-db.client.p12";
        public const string PostgresCertCopy =
            @"if exist $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 copy $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 $(OutDir)\certs\statepipes.postgres-db.client.p12";

        public string GenerateWorkflow(string projectDir, string projectName, string targetDirectory, string workflowName)
        {
            _pathProvider.AddPaths(projectDir, projectName, targetDirectory);
            var monikers = CreateMonikers(SolutionNameNoExtension, projectName);
            monikers.AddMoniker("@#$StateMachineName@#$", workflowName);
            var helper = new GeneratorHelper(new DirectoryHelper(_pathProvider.GetPath(PathName.Solution)), monikers);
            helper.MoveToRootDirectory();
            helper.MoveTo("@#$ClassLibraryName@#$");
            helper.MoveTo("StateMachines");
            helper.MoveTo("@#$StateMachineName@#$");
            helper.SaveTextFile("Workflow_cs.sample", "@#$StateMachineName@#$.cs");
            helper.MoveTo("States");
            helper.SaveTextFile("WorkflowFinishedState_cs.sample", "FinishedState.cs");
            return helper.SaveTextFile("WorkflowTopLevelState_cs.sample", "TopLevelState.cs");
        }

        /// <summary>
        /// Adds the store configuration once, before the setup line, and passes it to ServiceConfiguration. New services do
        /// not get it from the template, so services without workflows keep building against older StatePipes packages.
        /// Null when the file does not have the generated shape.
        /// </summary>
        public static string? InsertWorkflowStore(string contents)
        {
            if (contents.Contains("new WorkflowStoreConfig(")) return contents;
            var configured = GeneratorHelper.ReadSample("DefaultServiceConfiguration_AddWorkflowStore.sample").Trim();
            var anchor = contents.IndexOf(SetupAnchor, StringComparison.Ordinal);
            if (anchor < 0 || !contents.Contains(LegacyArgsTail)) return null;
            var newLine = contents.Contains("\r\n") ? "\r\n" : "\n";
            return contents.Insert(anchor, configured + newLine + "            ")
                .Replace(LegacyArgsTail, "new ServiceArgs(null), workflowStore);");
        }

        /// <summary>Appends the guarded certificate copy to the PostBuild Exec once; null when there is no PostBuild Exec.</summary>
        public static string? InsertPostgresCertCopy(string contents)
        {
            if (contents.Contains(PostgresCertFile)) return contents;
            var target = contents.IndexOf("<Target Name=\"PostBuild\"", StringComparison.Ordinal);
            if (target < 0) return null;
            var exec = contents.IndexOf("<Exec Command=\"", target, StringComparison.Ordinal);
            if (exec < 0) return null;
            var end = contents.IndexOf("\" />", exec, StringComparison.Ordinal);
            if (end < 0) return null;
            return contents.Insert(end, "&#xD;&#xA;" + PostgresCertCopy);
        }

        public static ToolResult CreateNewWorkflow(string solutionDir, string solutionFileName, string projectFileName, string targetDirectory, IToolInput input)
        {
            if (!IsServiceProject(projectFileName)) return ToolResult.Failed;
            var projectName = GetProjectNameNoExtension(projectFileName);
            var projDir = Path.Combine(solutionDir, projectName);
            var workflowName = input.Name("--workflow-name", "Enter the name for the new workflow");
            if (workflowName == null || AlreadyExists(solutionDir, Path.Combine(projDir, "StateMachines", workflowName, $"{workflowName}.cs"))) return ToolResult.Failed;

            var configPath = Path.Combine(projDir, "Builders", "DefaultServiceConfiguration.cs");
            var configured = File.Exists(configPath) ? InsertWorkflowStore(File.ReadAllText(configPath)) : null;
            if (configured == null)
            {
                Console.WriteLine($"Could not add WorkflowStoreConfig to {Path.GetRelativePath(solutionDir, configPath)}: '{SetupAnchor}' and '{LegacyArgsTail}' were not found.");
                return ToolResult.Failed;
            }
            var serviceCsproj = Path.Combine(solutionDir, projectName + ".Service", projectName + ".Service.csproj");
            var csproj = File.Exists(serviceCsproj) ? InsertPostgresCertCopy(File.ReadAllText(serviceCsproj)) : null;
            if (csproj == null)
            {
                Console.WriteLine($"Could not add the PostgreSQL certificate copy to {Path.GetRelativePath(solutionDir, serviceCsproj)}: no PostBuild Exec found.");
                return ToolResult.Failed;
            }

            var fileToOpen = new WorkflowGeneratorTool(solutionDir, solutionFileName).GenerateWorkflow(projDir, projectName, targetDirectory, workflowName);
            File.WriteAllText(configPath, configured);
            File.WriteAllText(serviceCsproj, csproj);
            return ToolResult.Ok(fileToOpen);
        }
    }
}
