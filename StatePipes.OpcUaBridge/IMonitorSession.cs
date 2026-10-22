using Opc.Ua;
using StatePipes.OpcUaBridge.Messages;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The outcome of applying a monitor to the OPC UA server. Revised values are what the server granted and
    /// are 0 unless the monitor is Active.
    /// </summary>
    internal sealed record MonitorApplyResult(MonitorState State, StatusCode StatusCode, double RevisedSamplingInterval, uint RevisedQueueSize, double RevisedPublishingInterval)
    {
        public static MonitorApplyResult Pending { get; } = new(MonitorState.Pending, StatusCodes.BadNotConnected, 0, 0, 0);
        public static MonitorApplyResult Failed(StatusCode statusCode) => new(MonitorState.Failed, statusCode, 0, 0, 0);
    }

    /// <summary>
    /// The monitored items of one OPC UA session. A new session starts empty, so everything is applied again
    /// from the registry after every reconnect.
    /// </summary>
    internal interface IMonitorSession : IAsyncDisposable
    {
        /// <summary>Creates the monitor, or changes it in place, moving it if its node or publishing interval changed.</summary>
        Task<MonitorApplyResult> ApplyAsync(MonitorDefinition definition, CancellationToken ct);

        /// <summary>Creates many monitors with one request per publishing interval, as a rebuild does.</summary>
        Task<IReadOnlyDictionary<MonitorKey, MonitorApplyResult>> ApplyAllAsync(IReadOnlyList<MonitorDefinition> definitions, CancellationToken ct);

        Task RemoveAsync(MonitorKey key, CancellationToken ct);

        /// <summary>Keeps the monitored item but stops it reporting.</summary>
        Task DisableAsync(MonitorKey key, CancellationToken ct);
    }
}
