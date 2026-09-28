using Confluent.Kafka;
using Confluent.Kafka.Admin;
using StatePipes.Common.Internal;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.ProcessLevelServices;
using System;
using System.Collections.Generic;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.BrokerProxy
{
    /// <summary>
    /// Kafka <see cref="ISimpleConnectionChannel"/>, tuned for the reason this service hand-rolls its comms:
    /// the shortest path from "message arrives" to "message republished".
    ///
    /// <para><b>Latency choices.</b> librdkafka's defaults favour throughput and would silently add tens of
    /// milliseconds per reflected message:</para>
    /// <list type="bullet">
    /// <item><c>LingerMs = 0</c> -- the important one. The default 5 ms batching window would be charged to every
    ///       reflected message for no benefit, since messages are published one at a time.</item>
    /// <item><c>FetchWaitMaxMs = 10</c> (default 500) -- caps how long the broker holds an idle fetch, which is
    ///       exactly what a message arriving into a quiet proxy waits on.</item>
    /// <item><c>SocketNagleDisable = true</c> -- no TCP coalescing delay on small messages.</item>
    /// <item>Fire-and-forget <c>Produce</c>, never <c>ProduceAsync</c>: the reflect path hands the message to
    ///       librdkafka's queue and returns, which is what makes <c>Acks.All</c> free here -- it is paid on
    ///       librdkafka's background thread rather than on the reflect path.</item>
    /// <item>One consumer thread per bus, so a slow command cannot delay an event.</item>
    /// <item>No idempotence: per-message sequencing buys a guarantee a reflector does not need.</item>
    /// </list>
    ///
    /// <para><b>Wire compatibility</b> with <see cref="KafkaTransport"/>, so real Kafka services understand us:
    /// topic = exchange name via <see cref="KafkaTopicNamer"/>, message type in <c>StatePipesType</c>, reply-to
    /// BusConfig in <c>StatePipesReplyTo</c>. Like the RabbitMQ side, every consumer is ephemeral -- a unique
    /// group id reading only what is produced after it starts. <see cref="IsOpen"/> tracks disposal rather than
    /// live connectivity, because librdkafka connects lazily and reconnects on its own.</para>
    /// </summary>
    internal class KafkaConnectionChannel : ISimpleConnectionChannel
    {
        private const int DefaultPort = 9093;
        private const int FetchWaitMaxMilliseconds = 10;

        private readonly CancellationTokenSource _cts = new();
        private readonly object _lock = new();
        private readonly List<ConsumerContext> _consumers = new();
        private readonly string _bootstrapServers;
        private readonly string _clientCertPath;
        private readonly string _certPassword;
        private readonly string _caPem;
        private readonly IProducer<string, byte[]> _producer;
        private bool _disposedValue;

        public KafkaConnectionChannel(BusConfig busConfig, string? hashedPassword, Action<ISimpleConnectionChannel>? configureBuses = null, CancellationToken cancelToken = default)
        {
            if (cancelToken.CanBeCanceled) cancelToken.Register(() => { try { _cts.Cancel(); } catch { } });
            try
            {
                _certPassword = File.ReadAllText(DirHelper.Find(busConfig.ClientCertPasswordPath, DirHelper.FileCategory.Certs)).Trim();
                if (hashedPassword != null && (string.IsNullOrEmpty(hashedPassword) || !PasswordHasher.VerifyPassword(hashedPassword, _certPassword)))
                    throw new Exception("Invalid Hashed Password");
                _clientCertPath = DirHelper.Find(busConfig.ClientCertPath, DirHelper.FileCategory.Certs);
                _caPem = KafkaCertificates.ExtractCaChainPem(_clientCertPath, _certPassword);
                _bootstrapServers = GetBootstrapServers(busConfig.BrokerUri);
                _producer = new ProducerBuilder<string, byte[]>(ApplySsl(new ProducerConfig
                {
                    Acks = Acks.All,    // free here: paid on librdkafka's thread, not on the reflect path
                    LingerMs = 0        // never sit on a message waiting to batch it
                }))
                    .SetErrorHandler((_, e) => Log?.LogVerbose($"Kafka producer error on {busConfig.BrokerUri}: {e.Reason}"))
                    .Build();
            }
            catch (Exception ex)
            {
                Log?.LogError($"Failed to create Kafka channel to {busConfig.BrokerUri} for {busConfig.ExchangeNamePrefix}{busConfig.ExchangeNamePostfix}: {ex.Message}");
                throw;
            }
            configureBuses?.Invoke(this);
        }

        public bool IsOpen
        {
            get { lock (_lock) { return !_disposedValue && !_cts.IsCancellationRequested; } }
        }

        public void ConfigureBus(Guid id, CommunicationsType commsType, string exchangeName, SimpleMessageReceived? consumeMethod = null, List<string>? routingKeys = null, bool autoDelete = false)
        {
            var topic = KafkaTopicNamer.ToTopic(exchangeName);
            EnsureTopic(topic);
            if (consumeMethod == null) return; // declaration only, matching ExchangeDeclare without a queue
            lock (_lock)
            {
                if (_disposedValue) return;
                var ctx = new ConsumerContext(topic, consumeMethod, routingKeys);
                ctx.Consumer = new ConsumerBuilder<string, byte[]>(ApplySsl(new ConsumerConfig
                {
                    GroupId = $"{topic}.{id:N}",                 // unique per bus => broadcast, nothing shared
                    AutoOffsetReset = AutoOffsetReset.Latest,    // reflect new traffic, never replay a backlog
                    EnableAutoCommit = false,                    // nobody ever reads these offsets back
                    FetchWaitMaxMs = FetchWaitMaxMilliseconds,   // do not let an idle fetch sit for 500ms
                    AllowAutoCreateTopics = true
                }))
                    .SetErrorHandler((_, e) => Log?.LogVerbose($"Kafka consumer error on {topic}: {e.Reason}"))
                    .Build();
                ctx.Consumer.Subscribe(topic);
                ctx.Loop = new Thread(() => ConsumeLoop(ctx))
                {
                    IsBackground = true,
                    Name = $"kafka-{commsType}-{topic}"
                };
                _consumers.Add(ctx);
                ctx.Loop.Start();
            }
        }

        public void Send(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw, string exchangeName)
        {
            if (string.IsNullOrEmpty(routingKey))
            {
                Log?.LogError("Failed to get FullName for message.");
                return;
            }
            var topic = KafkaTopicNamer.ToTopic(exchangeName);
            try
            {
                // Produce, not ProduceAsync: enqueue and return so the reflect path never waits on the broker.
                _producer.Produce(topic, new Message<string, byte[]>
                {
                    Key = routingKey,
                    Value = body,
                    Headers = SimpleMessageHelper.SerializeKafkaHeaders(routingKey, replyToRaw)
                },
                report => { if (report.Error.IsError) Log?.LogVerbose($"Failed to publish message {routingKey} to topic {topic}: {report.Error.Reason}"); });
            }
            catch (Exception e)
            {
                Log?.LogError($"Topic '{topic}' could not be published to: {routingKey} Exception: {e.Message}");
            }
        }

        private void ConsumeLoop(ConsumerContext ctx)
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    ConsumeResult<string, byte[]> result;
                    try { result = ctx.Consumer!.Consume(_cts.Token); }
                    catch (ConsumeException ce) { Log?.LogVerbose($"Kafka consume error on {ctx.Topic}: {ce.Error.Reason}"); continue; }
                    catch (OperationCanceledException) { break; }
                    if (result?.Message == null) continue;
                    try
                    {
                        if (!SimpleMessageHelper.TryRead(result, out byte[]? body, out string routingKey, out ReadOnlyMemory<byte> replyToRaw)) continue;
                        // Kafka does no server-side routing-key filtering, so the bind list is a client-side check.
                        if (!ctx.Accepts(routingKey)) continue;
                        ctx.ConsumeMethod(body!, routingKey, replyToRaw);
                    }
                    catch (Exception ex) { Log?.LogException(ex); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log?.LogException(ex); }
        }

        private void EnsureTopic(string topic)
        {
            try
            {
                using var admin = new AdminClientBuilder(ApplySsl(new AdminClientConfig())).Build();
                admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]).GetAwaiter().GetResult();
            }
            catch (CreateTopicsException) { /* already there, which is all we needed */ }
            catch (Exception ex) { Log?.LogVerbose($"Could not ensure Kafka topic {topic}: {ex.Message}"); }
        }

        private TConfig ApplySsl<TConfig>(TConfig config) where TConfig : ClientConfig
        {
            config.BootstrapServers = _bootstrapServers;
            config.SecurityProtocol = SecurityProtocol.Ssl;
            config.SslKeystoreLocation = _clientCertPath;   // PKCS#12 client keystore (cert + private key)
            config.SslKeystorePassword = _certPassword;
            config.SslCaPem = _caPem;
            config.EnableSslCertificateVerification = true;
            config.SslEndpointIdentificationAlgorithm = SslEndpointIdentificationAlgorithm.Https;
            config.SocketNagleDisable = true;               // small messages go out immediately
            return config;
        }

        private static string GetBootstrapServers(string brokerUri)
        {
            if (Uri.TryCreate(brokerUri, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                return $"{uri.Host}:{(uri.Port > 0 ? uri.Port : DefaultPort)}";
            return brokerUri; // assume already "host:port[,host:port]"
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue) return;
            if (disposing)
            {
                List<ConsumerContext> consumers;
                lock (_lock)
                {
                    _disposedValue = true;
                    consumers = new List<ConsumerContext>(_consumers);
                    _consumers.Clear();
                }
                try { _cts.Cancel(); } catch { }
                foreach (var ctx in consumers)
                {
                    try { ctx.Loop?.Join(TimeSpan.FromSeconds(5)); } catch { }
                    try { ctx.Consumer?.Close(); } catch { }
                    try { ctx.Consumer?.Dispose(); } catch { }
                }
                try { _producer.Flush(TimeSpan.FromSeconds(5)); } catch { }
                try { _producer.Dispose(); } catch { }
                try { _cts.Dispose(); } catch { }
            }
            _disposedValue = true;
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private sealed class ConsumerContext
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
}
