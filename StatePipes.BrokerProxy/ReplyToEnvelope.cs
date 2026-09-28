using StatePipes.Common;
using StatePipes.Comms;
using System;
using System.Text;

namespace StatePipes.BrokerProxy
{
    /// <summary>
    /// Builds the outgoing <c>StatePipesReplyTo</c> header for a reflected message without serializing anything.
    ///
    /// <para>Reflecting an event or a command means publishing it with <c>new BusConfig(destination, incoming)</c>
    /// as the reply-to -- that is, the destination's own fields with the incoming BusConfig hung off
    /// <c>PreviousHop</c>. Because the destination half never changes, its JSON can be serialized once at
    /// startup and split around the <c>PreviousHop</c> value:</para>
    ///
    /// <code>
    /// {"BrokerUri":...,"ExchangeNamePostfix":"...","PreviousHop":  &lt;-- _prefix
    ///                                                            &lt;incoming header bytes, verbatim&gt;
    /// }                                                            &lt;-- _suffix
    /// </code>
    ///
    /// <para>So each reflected message costs one buffer allocation and two memory copies, instead of a
    /// Newtonsoft deserialize plus re-serialize of the whole PreviousHop chain through the StatePipes
    /// converter pipeline. The result is JSON-equivalent to what the old code produced: property order and
    /// whitespace are the only differences, and neither is meaningful to a JSON reader.</para>
    /// </summary>
    internal sealed class ReplyToEnvelope
    {
        private const string PreviousHopProperty = "\"PreviousHop\"";
        private readonly byte[] _prefix;
        private readonly byte[] _suffix;

        /// <summary>This BusConfig on its own, with no previous hop. Used when there is nothing to nest.</summary>
        public byte[] SelfOnly { get; }

        public ReplyToEnvelope(BusConfig busConfig)
        {
            // A copy with PreviousHop explicitly cleared: whatever hop the caller's config happens to carry is
            // irrelevant, the incoming message supplies it per reflected message.
            var self = new BusConfig(busConfig.BrokerUri, busConfig.ExchangeNamePrefix, busConfig.ClientCertPath,
                busConfig.ClientCertPasswordPath, busConfig.ResponseExchangeGuid, busConfig.ExchangeNamePostfix, null);
            var json = JsonUtility.GetJsonStringForObject(self, true);
            SelfOnly = Encoding.UTF8.GetBytes(json);

            if (json.Length < 2 || json[^1] != '}')
                throw new InvalidOperationException($"BusConfig did not serialize to a JSON object: {json}");

            var property = json.IndexOf(PreviousHopProperty, StringComparison.Ordinal);
            if (property >= 0)
            {
                // "PreviousHop":null was emitted -- keep everything up to and including the colon, drop the null.
                var colon = json.IndexOf(':', property + PreviousHopProperty.Length);
                if (colon < 0) throw new InvalidOperationException($"Malformed {PreviousHopProperty} in {json}");
                var valueStart = colon + 1;
                var valueEnd = SkipJsonNull(json, valueStart);
                _prefix = Encoding.UTF8.GetBytes(json[..valueStart]);
                _suffix = Encoding.UTF8.GetBytes(json[valueEnd..]);
            }
            else
            {
                // Nulls were omitted from the output -- append the property just before the closing brace.
                _prefix = Encoding.UTF8.GetBytes(json[..^1] + ",\"" + nameof(BusConfig.PreviousHop) + "\":");
                _suffix = Encoding.UTF8.GetBytes("}");
            }
        }

        /// <summary>
        /// Returns this BusConfig's JSON with <paramref name="previousHopRaw"/> spliced in as its PreviousHop.
        /// Pure byte work -- no parsing of the incoming header, no serializing of the outgoing one.
        /// </summary>
        public byte[] Wrap(ReadOnlyMemory<byte> previousHopRaw)
        {
            if (previousHopRaw.IsEmpty) return SelfOnly;
            var wrapped = new byte[_prefix.Length + previousHopRaw.Length + _suffix.Length];
            _prefix.CopyTo(wrapped, 0);
            previousHopRaw.Span.CopyTo(wrapped.AsSpan(_prefix.Length));
            _suffix.CopyTo(wrapped, _prefix.Length + previousHopRaw.Length);
            return wrapped;
        }

        private static int SkipJsonNull(string json, int valueStart)
        {
            var i = valueStart;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (string.CompareOrdinal(json, i, "null", 0, 4) != 0)
                throw new InvalidOperationException($"Expected a null PreviousHop at offset {i} in {json}");
            return i + 4;
        }
    }
}
