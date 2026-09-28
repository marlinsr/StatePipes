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
                { Acks = Acks.All, LingerMs = 0 }))
                    .SetErrorHandler((_, e) => Log?.LogVerbose($"Kafka producer error on {busConfig.BrokerUri}: {e.Reason}")).Build();
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
                { GroupId = $"{topic}.{id:N}", AutoOffsetReset = AutoOffsetReset.Latest, EnableAutoCommit = false, FetchWaitMaxMs = FetchWaitMaxMilliseconds, AllowAutoCreateTopics = true }))
                    .SetErrorHandler((_, e) => Log?.LogVerbose($"Kafka consumer error on {topic}: {e.Reason}")).Build();
                ctx.Consumer.Subscribe(topic);
                ctx.Loop = new Thread(() => ConsumeLoop(ctx)) {IsBackground = true, Name = $"kafka-{commsType}-{topic}"};
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
                {Key = routingKey, Value = body, Headers = SimpleMessageHelper.SerializeKafkaHeaders(routingKey, replyToRaw)},
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
    }
}
