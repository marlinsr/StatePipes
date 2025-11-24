using RabbitMQ.Client;
using StatePipes.Common.Internal;
using StatePipes.ProcessLevelServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Comms.Internal
{
    internal static class StatePipesConnectionFactory
    {        
        public const int HeartbeatIntervalMilliseconds = 1000;
        public static IConnection CreateConnection(BusConfig busConfig, string? hashedPassword, CancellationToken cancelToken = default)
        {
            try
            {
                var uri = new Uri(busConfig.BrokerUri);
                string pw = File.ReadAllText(DirHelper.Find(busConfig.ClientCertPasswordPath, DirHelper.FileCategory.Certs)).Trim();
                if (hashedPassword != null && (string.IsNullOrEmpty(hashedPassword) || !PasswordHasher.VerifyPassword(hashedPassword, pw)))
                    throw new Exception("Invalid Hashed Password");
                var clientCert = X509CertificateLoader.LoadPkcs12FromFile(DirHelper.Find(busConfig.ClientCertPath, DirHelper.FileCategory.Certs), pw);
                var factory = new ConnectionFactory
                {
                    Uri = uri,
                    Port = 5671,
                    RequestedHeartbeat = TimeSpan.FromMilliseconds(HeartbeatIntervalMilliseconds),
                    AuthMechanisms = new IAuthMechanismFactory[] { new ExternalMechanismFactory() },
                    Ssl = new SslOption
                    {
                        Enabled = true,
                        ServerName = uri.Host,
                        Certs = new(new X509Certificate[] { clientCert }),
                        Version = SslProtocols.Tls13
                    },
                    AutomaticRecoveryEnabled = false,
                    NetworkRecoveryInterval = TimeSpan.FromMilliseconds(2 * HeartbeatIntervalMilliseconds),
                    TopologyRecoveryEnabled = false                 
                };
                return factory.CreateConnectionAsync(cancelToken).Result;
            }
            catch (Exception ex)
            {
                Log?.LogError($"Failed to create connection to {busConfig.BrokerUri} for {busConfig.ExchangeNamePrefix}{busConfig.ExchangeNamePostfix}: {ex.Message}");
                throw;
            }
        }
    }
}
