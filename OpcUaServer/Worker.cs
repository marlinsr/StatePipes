using Opc.Ua;
using Opc.Ua.Configuration;

namespace OpcUaServer;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private PlcServer? _server;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var config = await BuildServerConfiguration();
            _server = new PlcServer();
            _server.Start(config);

            _logger.LogInformation("OPC UA Server started at {Endpoint}",
                config.ServerConfiguration.BaseAddresses.FirstOrDefault());

            double angle = 0.0;

            while (!stoppingToken.IsCancellationRequested)
            {
                // Simulate pressure as a sine wave (14.7 ± 10 PSI)
                double pressure = 14.7 + 10.0 * Math.Sin(angle);
                double volume = 5.0 + 10 * Math.Cos(angle);
                double mass = 10 * Math.Sin(angle);
                _server.NodeManager?.UpdatePressure(Math.Round(pressure, 4));
                _server.NodeManager?.UpdateMsg($"Running | Pressure: {pressure:F2} PSI");
                _server.NodeManager?.UpdateDensity(Math.Round(volume, 4), Math.Round(mass, 4));

                angle += 0.1;
                if (angle > 2 * Math.PI)
                    angle -= 2 * Math.PI;

                await Task.Delay(500, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OPC UA Server encountered a fatal error");
            throw;
        }
        finally
        {
            _server?.Stop();
            _logger.LogInformation("OPC UA Server stopped");
        }
    }

    private static async Task<ApplicationConfiguration> BuildServerConfiguration()
    {
        var config = new ApplicationConfiguration
        {
            ApplicationName = "PLCSimulator OPC UA Server",
            ApplicationUri = Utils.Format("urn:{0}:PLCSimulator:OpcUaServer", Utils.GetHostName()),
            ProductUri = "urn:PLCSimulator:OpcUaServer",
            ApplicationType = ApplicationType.Server,

            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "%LocalApplicationData%/PLCSimulator/pki/own",
                    SubjectName = "CN=PLCSimulator OPC UA Server, DC=localhost"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "%LocalApplicationData%/PLCSimulator/pki/issuer"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "%LocalApplicationData%/PLCSimulator/pki/trusted"
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "%LocalApplicationData%/PLCSimulator/pki/rejected"
                },
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true,
                RejectSHA1SignedCertificates = false
            },

            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },

            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = { "opc.tcp://localhost:4840/PLCSimulator" },
                MinRequestThreadCount = 5,
                MaxRequestThreadCount = 100,
                MaxQueuedRequestCount = 200,
                SecurityPolicies = new ServerSecurityPolicyCollection
                {
                    new ServerSecurityPolicy
                    {
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                },
                UserTokenPolicies = new UserTokenPolicyCollection
                {
                    new UserTokenPolicy { TokenType = UserTokenType.Anonymous }
                }
            },

            TraceConfiguration = new TraceConfiguration
            {
                OutputFilePath = "%LocalApplicationData%/PLCSimulator/logs/trace.log",
                DeleteOnLoad = true
            }
        };

        await config.Validate(ApplicationType.Server);

        var application = new ApplicationInstance { ApplicationConfiguration = config };
        await application.CheckApplicationInstanceCertificate(false, 2048);

        return config;
    }
}
