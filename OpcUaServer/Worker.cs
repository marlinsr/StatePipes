using Opc.Ua;
using Opc.Ua.Configuration;

namespace OpcUaServer;

public class Worker : BackgroundService
{
    private const string EndpointUrl = "opc.tcp://localhost:4840/PLCSimulator";
    private readonly ILogger<Worker> _logger;
    private PlcServer? _server;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var telemetry = DefaultTelemetry.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        try
        {
            var config = await BuildServerConfiguration(telemetry, stoppingToken);
            _server = new PlcServer(telemetry);
            await _server.StartAsync(config, stoppingToken);

            _logger.LogInformation("OPC UA Server started at {Endpoint}", EndpointUrl);

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
                _server.NodeManager?.UpdateTank(Math.Round(pressure, 4), Math.Round(volume, 4), Math.Round(mass, 4));

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
            if (_server != null) await _server.StopAsync(CancellationToken.None);
            _logger.LogInformation("OPC UA Server stopped");
        }
    }

    private static async Task<ApplicationConfiguration> BuildServerConfiguration(ITelemetryContext telemetry, CancellationToken ct)
    {
        var config = new ApplicationConfiguration(telemetry)
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

            TransportConfigurations = [],
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },

            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = [EndpointUrl],
                MinRequestThreadCount = 5,
                MaxRequestThreadCount = 100,
                MaxQueuedRequestCount = 200,
                // No discovery server registration: the default LDS URL is opc.tcp://localhost:4840, this server's own port
                MaxRegistrationInterval = 0,
                SecurityPolicies =
                [
                    new ServerSecurityPolicy
                    {
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ],
                UserTokenPolicies =
                [
                    new UserTokenPolicy { TokenType = UserTokenType.Anonymous }
                ]
            },

            TraceConfiguration = new TraceConfiguration
            {
                OutputFilePath = "%LocalApplicationData%/PLCSimulator/logs/trace.log",
                DeleteOnLoad = true
            }
        };

        await config.ValidateAsync(ApplicationType.Server, ct);

        var application = new ApplicationInstance(config, telemetry);
        await application.CheckApplicationInstanceCertificatesAsync(false, null, ct);

        return config;
    }
}
