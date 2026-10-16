using Newtonsoft.Json.Linq;
using Opc.Ua;
using StatePipes.OpcUaBridge.Messages;
using System.Text;
using static StatePipes.OpcUaBridge.SelfDescriptionBuilder;

namespace StatePipes.OpcUaBridge
{
    /// <summary>A Monitor command's body, read without knowing which data item it is for.</summary>
    /// <param name="Parameters">Null when the body is not a valid Monitor command; <paramref name="Status"/> says why.</param>
    internal sealed record ParsedMonitorCommand(MonitorParameters? Parameters, StatusCode Status);

    /// <summary>How a client asked for a data item to be monitored: everything a Monitor command carries.</summary>
    internal sealed record MonitorParameters(MonitoringModeKind MonitoringMode, DataChangeTriggerKind DataChangeTrigger, DeadbandTypeKind DeadbandType,
        double DeadbandValue, double SamplingInterval, uint QueueSize, double PublishingInterval)
    {
        /// <summary>
        /// Reads and validates a Monitor command body. Never throws: anything that is not a valid command comes
        /// back as <see cref="StatusCodes.BadInvalidArgument"/>. Enums may arrive as names or numbers, since
        /// clients serialize them either way. A deadband value means nothing without a deadband, so it is
        /// dropped rather than validated when the type is None.
        /// </summary>
        public static ParsedMonitorCommand Parse(byte[] body)
        {
            var json = TryParseObject(body);
            if (json == null) return Invalid;
            if (!TryReadEnum(json, MonitoringModeProperty, out MonitoringModeKind mode)
                || !TryReadEnum(json, DataChangeTriggerProperty, out DataChangeTriggerKind trigger)
                || !TryReadEnum(json, DeadbandTypeProperty, out DeadbandTypeKind deadbandType)
                || !TryReadNumber(json, DeadbandValueProperty, out double deadbandValue)
                || !TryReadNumber(json, SamplingIntervalProperty, out double samplingInterval)
                || !TryReadNumber(json, QueueSizeProperty, out double queueSize)
                || !TryReadNumber(json, PublishingIntervalProperty, out double publishingInterval))
                return Invalid;
            if (deadbandType == DeadbandTypeKind.None) deadbandValue = 0;
            if (samplingInterval < 0 || publishingInterval <= 0 || queueSize < 1 || queueSize > uint.MaxValue || queueSize != Math.Floor(queueSize)
                || deadbandValue < 0 || (deadbandType == DeadbandTypeKind.Percent && deadbandValue > 100))
                return Invalid;
            return new(new MonitorParameters(mode, trigger, deadbandType, deadbandValue, samplingInterval, (uint)queueSize, publishingInterval), StatusCodes.Good);
        }

        private static ParsedMonitorCommand Invalid => new(null, StatusCodes.BadInvalidArgument);

        private static JObject? TryParseObject(byte[] body)
        {
            try { return JObject.Parse(Encoding.UTF8.GetString(body)); }
            catch (Exception) { return null; }
        }

        private static bool TryReadEnum<TEnum>(JObject json, string property, out TEnum value) where TEnum : struct, Enum
        {
            value = default;
            var token = json[property];
            if (token?.Type == JTokenType.Integer)
            {
                value = (TEnum)Enum.ToObject(typeof(TEnum), (long)token);
                return Enum.IsDefined(value);
            }
            return token?.Type == JTokenType.String && Enum.TryParse((string?)token, false, out value) && Enum.IsDefined(value);
        }

        private static bool TryReadNumber(JObject json, string property, out double value)
        {
            value = 0;
            var token = json[property];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) return false;
            value = (double)token;
            return double.IsFinite(value);
        }
    }
}
