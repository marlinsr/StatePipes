using Opc.Ua;

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
            _server = await PlcServerHost.StartAsync(EndpointUrl, "%LocalApplicationData%/PLCSimulator", telemetry, stoppingToken);

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
}
