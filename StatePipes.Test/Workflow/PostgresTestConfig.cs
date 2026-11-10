using StatePipes.Comms;

namespace StatePipes.Test.Workflow
{
    /// <summary>Connection settings for the Docker PostgreSQL from DockerInfrastructureStart.ps1; null when it is not set up.</summary>
    internal static class PostgresTestConfig
    {
        public static string StepCaDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SmallStep", "step-ca");
        public static string ClientCert => Path.Combine(StepCaDirectory, "statepipes.postgres-db.client.p12");

        public static WorkflowStoreConfig? TryCreate() =>
            File.Exists(ClientCert)
                ? new WorkflowStoreConfig("postgres-db", "statepipes", "statepipes", ClientCert,
                    passwordFileName: Path.Combine(StepCaDirectory, "CommonPassword.txt"))
                : null;

        public static WorkflowStoreConfig RequireOrInconclusive() =>
            TryCreate() ?? throw new AssertInconclusiveException($"PostgreSQL infrastructure not set up ({ClientCert} missing); run DockerInfrastructureStart.ps1.");
    }
}
