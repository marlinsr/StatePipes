using StatePipes.ProcessLevelServices.Internal;
using System.Diagnostics;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Runs the two halves of the bridge side by side. The StatePipes side comes up immediately and stays up;
    /// the OPC UA side connects, discovers the address space and hands it to the StatePipes side, and starts
    /// over whenever the session is lost so a restarted or reconfigured server is picked up again. While the
    /// server is unreachable, Get commands are still answered, with a Bad status and no value.
    /// </summary>
    internal class ServiceWorker : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var version = FileVersionInfo.GetVersionInfo(WorkerHelper.FileName()).ProductVersion;
            Log?.LogInfo($"Starting as service {WorkerHelper.ExeName()} [version {version}]...");
            BridgeSettings settings;
            try { settings = BridgeSettings.Load(); }
            catch (Exception ex)
            {
                Log?.LogError($"Invalid configuration: {ex.Message}");
                throw;
            }
            await using OpcUaClient opcUa = new(settings);
            using OpcUaBridgeService bridge = new(settings.BusConfig, opcUa);
            await Task.WhenAll(bridge.RunAsync(stoppingToken), MaintainOpcUaSessionAsync(settings, opcUa, bridge, stoppingToken));
        }

        private static async Task MaintainOpcUaSessionAsync(BridgeSettings settings, OpcUaClient opcUa, OpcUaBridgeService bridge, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await opcUa.ConnectAsync(ct);
                    bridge.SetCatalog(await opcUa.DiscoverAsync(ct));
                    while (opcUa.IsConnected) await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    Log?.LogError($"Lost connection to OPC UA server {settings.OpcUaEndpoint}");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Log?.LogError($"OPC UA server {settings.OpcUaEndpoint}: {ex.Message}");
                }
                await opcUa.DisconnectAsync();
                try { await Task.Delay(settings.ReconnectDelay, ct); } catch (OperationCanceledException) { }
            }
        }
    }
}
