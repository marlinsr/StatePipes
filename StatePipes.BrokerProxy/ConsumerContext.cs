using Confluent.Kafka;
using System;
using System.Collections.Generic;
using System.Text;

namespace StatePipes.BrokerProxy
{
    internal sealed class ConsumerContext
    {
        private readonly HashSet<string> _acceptedTypes;
        public ConsumerContext(string topic, SimpleMessageReceived consumeMethod, List<string>? routingKeys)
        {
            Topic = topic;
            ConsumeMethod = consumeMethod;
            _acceptedTypes = new HashSet<string>(routingKeys is { Count: > 0 } ? routingKeys : ISimpleConnectionChannel.DefaultRoutingKeys);
            AcceptsEverything = _acceptedTypes.Contains("#");
        }
        public string Topic { get; }
        public SimpleMessageReceived ConsumeMethod { get; }
        public IConsumer<string, byte[]>? Consumer { get; set; }
        public Thread? Loop { get; set; }
        private bool AcceptsEverything { get; }
        // Hot path: the common "#" case short-circuits before touching the set.
        public bool Accepts(string routingKey) => AcceptsEverything || _acceptedTypes.Contains(routingKey);
    }
}
