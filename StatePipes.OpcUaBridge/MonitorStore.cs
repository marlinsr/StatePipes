using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using StatePipes.OpcUaBridge.Messages;
using System.Globalization;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// The monitors file. Every save replaces the whole file through a temporary file and a rename, so a crash
    /// leaves either the old file or the new one. A file the bridge cannot read is set aside rather than
    /// overwritten, so whatever it held can still be recovered by hand.
    /// </summary>
    internal sealed class MonitorStore(string path)
    {
        private const int Version = 1;
        private static readonly JsonSerializerSettings Settings = new()
        {
            Converters = [new StringEnumConverter()],
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public string Path { get; } = path;

        public IReadOnlyList<MonitorDefinition> Load()
        {
            if (!File.Exists(Path)) return [];
            try
            {
                var file = JsonConvert.DeserializeObject<StoreFile>(File.ReadAllText(Path), Settings);
                if (file?.Version != Version || file.Monitors == null) throw new JsonException($"Unsupported monitors file version {file?.Version}");
                return [.. file.Monitors.Select(entry => entry.ToDefinition())];
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
            {
                var corruptPath = $"{Path}.corrupt-{DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)}";
                Log?.LogError($"Monitors file {Path} is unreadable ({ex.Message}); moved it to {corruptPath} and starting with no monitors");
                File.Move(Path, corruptPath);
                return [];
            }
        }

        /// <summary>Writes every monitor, returning false (and logging) when the file could not be written.</summary>
        public bool Save(IReadOnlyList<MonitorDefinition> monitors)
        {
            var tempPath = Path + ".tmp";
            try
            {
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                StoreFile file = new() { Version = Version, Monitors = [.. monitors.Select(StoreEntry.From)] };
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(file, Settings));
                File.Move(tempPath, Path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log?.LogError($"Could not save monitors to {Path}: {ex.Message}");
                try { File.Delete(tempPath); } catch (Exception) { }
                return false;
            }
        }

        private sealed class StoreFile
        {
            [JsonProperty("version")] public int Version { get; set; }
            [JsonProperty("monitors")] public List<StoreEntry>? Monitors { get; set; }
        }

        private sealed class StoreEntry
        {
            [JsonProperty("senderCommandExchange", Required = Required.Always)] public string SenderCommandExchange { get; set; } = string.Empty;
            [JsonProperty("monitorName", Required = Required.Always)] public string MonitorName { get; set; } = string.Empty;
            [JsonProperty("nodeId", Required = Required.Always)] public string NodeId { get; set; } = string.Empty;
            [JsonProperty("monitoringMode", Required = Required.Always)] public MonitoringModeKind MonitoringMode { get; set; }
            [JsonProperty("dataChangeTrigger", Required = Required.Always)] public DataChangeTriggerKind DataChangeTrigger { get; set; }
            [JsonProperty("deadbandType", Required = Required.Always)] public DeadbandTypeKind DeadbandType { get; set; }
            [JsonProperty("deadbandValue", Required = Required.Always)] public double DeadbandValue { get; set; }
            [JsonProperty("samplingInterval", Required = Required.Always)] public double SamplingInterval { get; set; }
            [JsonProperty("queueSize", Required = Required.Always)] public uint QueueSize { get; set; }
            [JsonProperty("publishingInterval", Required = Required.Always)] public double PublishingInterval { get; set; }
            [JsonProperty("replyTo", Required = Required.Always)] public string ReplyTo { get; set; } = string.Empty;
            [JsonProperty("suspendedSince")] public DateTime? SuspendedSince { get; set; }

            public static StoreEntry From(MonitorDefinition definition) => new()
            {
                SenderCommandExchange = definition.Key.SenderCommandExchange,
                MonitorName = definition.Key.MonitorName,
                NodeId = definition.NodeIdText,
                MonitoringMode = definition.Parameters.MonitoringMode,
                DataChangeTrigger = definition.Parameters.DataChangeTrigger,
                DeadbandType = definition.Parameters.DeadbandType,
                DeadbandValue = definition.Parameters.DeadbandValue,
                SamplingInterval = definition.Parameters.SamplingInterval,
                QueueSize = definition.Parameters.QueueSize,
                PublishingInterval = definition.Parameters.PublishingInterval,
                ReplyTo = definition.ReplyToRaw,
                SuspendedSince = definition.SuspendedSince
            };

            public MonitorDefinition ToDefinition() => new(new MonitorKey(SenderCommandExchange, MonitorName), NodeId,
                new MonitorParameters(MonitoringMode, DataChangeTrigger, DeadbandType, DeadbandValue, SamplingInterval, QueueSize, PublishingInterval),
                ReplyTo, SuspendedSince);
        }
    }
}
