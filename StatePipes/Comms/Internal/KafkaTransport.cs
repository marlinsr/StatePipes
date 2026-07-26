using Confluent.Kafka;
using Confluent.Kafka.Admin;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using StatePipes.ProcessLevelServices;
using System.Text;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Comms.Internal
{
    /// <summary>
    /// Kafka implementation of <see cref="ITransport"/>.
    ///
    /// <para><b>Semantics (all-ephemeral parity with the current RabbitMQ behavior):</b> every
    /// consumer uses a unique per-instance <c>group.id</c> with <c>auto.offset.reset=latest</c>
    /// and no offset commit, so each transport instance reads only messages produced after it
    /// starts and never replays backlog &mdash; mirroring today's GUID-named, per-process RabbitMQ
    /// queues. Durable commands / partitioned parallelism are intentionally deferred to a later phase.</para>
    ///
    /// <para><b>Routing keys:</b> Kafka has no server-side routing-key filtering, so the routing
    /// keys supplied to <see cref="ConfigureBus"/>/<see cref="Subscribe"/> become a client-side
    /// accept-set keyed on the message type name (carried in the <c>StatePipesType</c> header).
    /// The wildcard <c>"#"</c> means accept all.</para>
    ///
    /// <para>Topic = exchange name 1:1 (see <see cref="KafkaTopicNamer"/>).</para>
    ///
    /// <para><b>Note:</b> broker CA trust is not modeled on <see cref="BusConfig"/> (RabbitMQ relies
    /// on the OS trust store); set <c>SslCaLocation</c> here if librdkafka cannot validate the broker
    /// certificate in your environment. <see cref="IsOpen"/> reflects only whether this transport has
    /// been disposed, not live broker connectivity (librdkafka connects lazily).</para>
    /// </summary>
    internal class KafkaTransport : ITransport
    {
        private const int DefaultPort = 9093;
        private const string StatePipesTypeHeader = "StatePipesType";

        private readonly BusConfig _busConfig;
        private readonly string _certPath;
        private readonly string _certPassword;
        private readonly CancellationTokenSource _cts = new();
        private readonly Lock _lock = new();
        private readonly Dictionary<string, KafkaConsumerContext> _consumers = [];
        private readonly IProducer<string, byte[]> _producer;
        private bool _disposedValue;

        public KafkaTransport(BusConfig busConfig, string? hashedPassword, Action<ITransport>? configureBuses = null, CancellationToken cancelToken = default)
        {
            _busConfig = busConfig;
            cancelToken.Register(() => _cts.Cancel());
            _certPassword = File.ReadAllText(DirHelper.Find(busConfig.ClientCertPasswordPath, DirHelper.FileCategory.Certs)).Trim();
            if (hashedPassword != null && (string.IsNullOrEmpty(hashedPassword) || !PasswordHasher.VerifyPassword(hashedPassword, _certPassword)))
                throw new Exception("Invalid Hashed Password");
            _certPath = DirHelper.Find(busConfig.ClientCertPath, DirHelper.FileCategory.Certs);
            var producerConfig = ApplySsl(new ProducerConfig { EnableIdempotence = true, Acks = Acks.All });
            _producer = new ProducerBuilder<string, byte[]>(producerConfig)
                .SetErrorHandler((_, e) => Log?.LogVerbose($"Kafka producer error: {e.Reason}"))
                .Build();
            configureBuses?.Invoke(this);
        }

        private TConfig ApplySsl<TConfig>(TConfig config) where TConfig : ClientConfig
        {
            config.BootstrapServers = GetBootstrapServers(_busConfig.BrokerUri);
            config.SecurityProtocol = SecurityProtocol.Ssl;
            config.SslKeystoreLocation = _certPath;      // PKCS#12 client keystore (cert + private key)
            config.SslKeystorePassword = _certPassword;
            config.SslCaLocation = @"C:\ProgramData\Kafka\kafka-broker\Certs\amqp09-broker.cacert.pem";
            return config;
        }

        private static string GetBootstrapServers(string brokerUri)
        {
            if (Uri.TryCreate(brokerUri, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                return $"{uri.Host}:{(uri.Port > 0 ? uri.Port : DefaultPort)}";
            return brokerUri; // assume already "host:port[,host:port]"
        }

        public bool IsOpen
        {
            get { lock (_lock) { return !_disposedValue; } }
        }

        public void ConfigureBus(Guid id, CommunicationsType commsType, string exchangeName, Func<ReceivedTransportMessage, Task>? consumeMethod = null, List<string>? routingKeys = null, bool autoDelete = false)
        {
            var topic = KafkaTopicNamer.ToTopic(exchangeName);
            EnsureTopic(topic);
            if (consumeMethod == null) return; // producer-only declaration (parity with ExchangeDeclare without a queue)
            lock (_lock)
            {
                if (_disposedValue || _consumers.ContainsKey(topic)) return;
                var ctx = new KafkaConsumerContext(topic, consumeMethod, routingKeys);
                var consumerConfig = ApplySsl(new ConsumerConfig
                {
                    GroupId = $"{topic}.{Guid.NewGuid():N}", // unique per instance => broadcast + ephemeral
                    AutoOffsetReset = AutoOffsetReset.Latest, // new messages only (no backlog replay)
                    EnableAutoCommit = false,
                    AllowAutoCreateTopics = true
                });
                ctx.Consumer = new ConsumerBuilder<string, byte[]>(consumerConfig)
                    .SetErrorHandler((_, e) => Log?.LogVerbose($"Kafka consumer error on {topic}: {e.Reason}"))
                    .Build();
                ctx.Consumer.Subscribe(topic);
                ctx.Loop = new Thread(() => ConsumeLoop(ctx)) { IsBackground = true, Name = $"kafka-consume-{topic}" };
                _consumers[topic] = ctx;
                ctx.Loop.Start();
            }
        }

        private void EnsureTopic(string topic)
        {
            try
            {
                using var admin = new AdminClientBuilder(ApplySsl(new AdminClientConfig())).Build();
                admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]).GetAwaiter().GetResult();
            }
            catch (CreateTopicsException) { /* topic already exists or broker auto-creates: best effort, mirrors idempotent ExchangeDeclare */ }
            catch (Exception ex) { Log?.LogVerbose($"Could not ensure Kafka topic {topic}: {ex.Message}"); }
        }

        public void Subscribe(Guid id, string routingKey, BusConfig busConfig)
        {
            var topic = KafkaTopicNamer.ToTopic(busConfig.EventExchangeName);
            lock (_lock)
            {
                if (_consumers.TryGetValue(topic, out var ctx)) ctx.AddRoutingKey(routingKey);
            }
        }

        public void UnSubscribe(Guid id, string routingKey, BusConfig busConfig)
        {
            var topic = KafkaTopicNamer.ToTopic(busConfig.EventExchangeName);
            lock (_lock)
            {
                if (_consumers.TryGetValue(topic, out var ctx)) ctx.RemoveRoutingKey(routingKey);
            }
        }

        public void Send<T>(T message, BusConfig busConfigFrom, string exchangeName) where T : IMessage => Send<T>(message.GetType().FullName, message, busConfigFrom, exchangeName);

        public void Send<T>(string? sendCommandTypeFullName, T message, BusConfig busConfigFrom, string exchangeName)
        {
            if (message == null || string.IsNullOrEmpty(sendCommandTypeFullName)) return;
            MessageHelper.Serialize(message, busConfigFrom, out byte[] body, out IDictionary<string, object?> headers);
            var kafkaHeaders = new Headers { { StatePipesTypeHeader, Encoding.UTF8.GetBytes(sendCommandTypeFullName) } };
            foreach (var kv in headers)
            {
                if (kv.Value is string s) kafkaHeaders.Add(kv.Key, Encoding.UTF8.GetBytes(s));
                else if (kv.Value is byte[] b) kafkaHeaders.Add(kv.Key, b);
            }
            var topic = KafkaTopicNamer.ToTopic(exchangeName);
            try
            {
                _producer.Produce(topic, new Message<string, byte[]> { Key = sendCommandTypeFullName, Value = body, Headers = kafkaHeaders },
                    report => { if (report.Error.IsError) Log?.LogVerbose($"Failed to publish {sendCommandTypeFullName} to {topic}: {report.Error.Reason}"); });
            }
            catch (ProduceException<string, byte[]> e) { Log?.LogError($"Failed to publish {sendCommandTypeFullName} to topic {topic}: {e.Error.Reason}"); }
        }

        private void ConsumeLoop(KafkaConsumerContext ctx)
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
                    var received = ToReceivedTransportMessage(result, out var type);
                    if (!ctx.Accepts(type)) continue;
                    try { ctx.ConsumeMethod(received).GetAwaiter().GetResult(); }
                    catch (Exception ex) { Log?.LogException(ex); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log?.LogException(ex); }
        }

        private static ReceivedTransportMessage ToReceivedTransportMessage(ConsumeResult<string, byte[]> result, out string type)
        {
            type = string.Empty;
            var headers = new Dictionary<string, object?>();
            foreach (var h in result.Message.Headers)
            {
                var bytes = h.GetValueBytes();
                if (h.Key == StatePipesTypeHeader) type = Encoding.UTF8.GetString(bytes);
                else headers[h.Key] = bytes; // byte[] value: matches what MessageHelper.Deserialize expects
            }
            return new ReceivedTransportMessage(type, headers, result.Message.Value ?? []);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue) return;
            if (disposing)
            {
                List<KafkaConsumerContext> consumers;
                lock (_lock)
                {
                    _disposedValue = true;
                    consumers = [.. _consumers.Values];
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
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private sealed class KafkaConsumerContext(string topic, Func<ReceivedTransportMessage, Task> consumeMethod, List<string>? routingKeys)
        {
            private readonly HashSet<string> _acceptedTypes = routingKeys is { Count: > 0 } ? [.. routingKeys] : [.. ITransport.DefaultRoutingKeys];
            public string Topic { get; } = topic;
            public Func<ReceivedTransportMessage, Task> ConsumeMethod { get; } = consumeMethod;
            public IConsumer<string, byte[]>? Consumer { get; set; }
            public Thread? Loop { get; set; }
            public void AddRoutingKey(string routingKey) { lock (_acceptedTypes) _acceptedTypes.Add(routingKey); }
            public void RemoveRoutingKey(string routingKey) { lock (_acceptedTypes) _acceptedTypes.Remove(routingKey); }
            public bool Accepts(string type) { lock (_acceptedTypes) return _acceptedTypes.Contains("#") || _acceptedTypes.Contains(type); }
        }
    }
}
