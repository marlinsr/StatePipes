using Npgsql;
using StatePipes.Comms;
using StatePipes.ProcessLevelServices;
using System.Security.Cryptography.X509Certificates;

namespace StatePipes.Workflow.Internal.Postgres
{
    /// <summary>
    /// Connections to the workflow store: TLS with full server verification against the OS trust store (the step-ca root),
    /// and authentication by client certificate only — no password is ever put in the connection string.
    /// </summary>
    internal static class PostgresConnectionFactory
    {
        public static NpgsqlDataSource Create(WorkflowStoreConfig config)
        {
            var connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = config.Host,
                Port = config.Port,
                Database = config.Database,
                Username = config.Username,
                SslMode = SslMode.VerifyFull,
            }.ConnectionString;
            var password = File.ReadAllText(Resolve(config.PasswordFileName)).Trim();
            var clientCertificate = X509CertificateLoader.LoadPkcs12FromFile(Resolve(config.ClientCertFileName), password);
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseSslClientAuthenticationOptionsCallback(options => options.ClientCertificates = [clientCertificate]);
            return builder.Build();
        }

        private static string Resolve(string fileName)
        {
            var path = DirHelper.Find(fileName, DirHelper.FileCategory.Certs);
            return string.IsNullOrEmpty(path) ? throw new FileNotFoundException($"Workflow store certificate file {fileName} not found.", fileName) : path;
        }
    }
}
