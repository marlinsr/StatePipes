using Opc.Ua;
using Opc.Ua.Configuration;

namespace OpcUaServer;

/// <summary>
/// Starts the simulated PLC on a given endpoint with its PKI and trace log under <c>root</c>. The Worker uses it
/// for the standalone server; tests use it to host the server in-process on a port of their own.
/// </summary>
public static class PlcServerHost
{
    public static async Task<PlcServer> StartAsync(string endpointUrl, string root, ITelemetryContext telemetry, CancellationToken ct)
    {
        var config = await BuildServerConfiguration(endpointUrl, root, telemetry, ct);
        var server = new PlcServer(telemetry);
        await server.StartAsync(config, ct);
        return server;
    }

    private static async Task<ApplicationConfiguration> BuildServerConfiguration(string endpointUrl, string root, ITelemetryContext telemetry, CancellationToken ct)
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
                    StorePath = $"{root}/pki/own",
                    SubjectName = "CN=PLCSimulator OPC UA Server, DC=localhost"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = $"{root}/pki/issuer"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = $"{root}/pki/trusted"
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = $"{root}/pki/rejected"
                },
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true,
                RejectSHA1SignedCertificates = false
            },

            TransportConfigurations = [],
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },

            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = [endpointUrl],
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
                OutputFilePath = $"{root}/logs/trace.log",
                DeleteOnLoad = true
            }
        };

        await config.ValidateAsync(ApplicationType.Server, ct);

        var application = new ApplicationInstance(config, telemetry);
        await application.CheckApplicationInstanceCertificatesAsync(false, null, ct);

        return config;
    }
}
