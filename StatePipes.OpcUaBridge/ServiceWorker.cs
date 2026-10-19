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
    ///
    /// <para>Monitors are loaded from their store before anything connects and rebuilt on every new session.</para>
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
            var registry = MonitorRegistry.Load(new MonitorStore(settings.MonitorStorePath), settings.BusConfig, settings.MonitorSuspendedExpiry, TimeProvider.System);
            Log?.LogInfo($"Loaded {registry.GetAll().Count} monitors from {settings.MonitorStorePath}");
            using OpcUaBridgeService bridge = new(settings.BusConfig, opcUa, registry);
            await Task.WhenAll(bridge.RunAsync(stoppingToken), MaintainOpcUaSessionAsync(settings, opcUa, bridge, stoppingToken));
        }

        internal static async Task MaintainOpcUaSessionAsync(BridgeSettings settings, OpcUaClient opcUa, OpcUaBridgeService bridge, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await opcUa.ConnectAsync(ct);
                    var nodes = await opcUa.DiscoverAsync(ct);
                    bridge.SetCatalog(nodes.Items, nodes.Methods);
                    bridge.Monitors.SessionStarted(opcUa.CreateMonitorManager(bridge.Monitors.OnNotification), nodes.Items);
                    while (opcUa.IsConnected) await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    Log?.LogError($"Lost connection to OPC UA server {settings.OpcUaEndpoint}");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Log?.LogError($"OPC UA server {settings.OpcUaEndpoint}: {ex.Message}");
                }
                bridge.Monitors.SessionLost();
                await opcUa.DisconnectAsync();
                try { await Task.Delay(settings.ReconnectDelay, ct); } catch (OperationCanceledException) { }
            }
        }
    }
}
