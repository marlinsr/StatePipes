using Confluent.Kafka;
using Confluent.Kafka.Admin;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace StatePipes.ServiceCreatorTool
{
    /// <summary>
    /// Kafka flavor of <see cref="IServiceDescriptionTransport"/>, matching the wire conventions of
    /// StatePipes.Comms.Internal.KafkaTransport so a running Kafka service understands us:
    ///
    /// <list type="bullet">
    /// <item>topic name = exchange name 1:1, with characters illegal in Kafka topics replaced (KafkaTopicNamer)</item>
    /// <item>the message type travels in the <c>StatePipesType</c> header, since Kafka has no AMQP
    ///       <c>BasicProperties.Type</c>; the reply-to BusConfig travels in <c>StatePipesReplyTo</c> as it does on RabbitMQ</item>
    /// <item>mutual TLS from one .p12 keystore, with the CA chain lifted out of that same bundle</item>
    /// </list>
    /// </summary>
    internal sealed class KafkaServiceDescriptionTransport : IServiceDescriptionTransport
    {
        private const int DefaultPort = 9093;
        private const int MaxTopicLength = 249;
        private const string StatePipesTypeHeader = "StatePipesType";
        private const string ReplyToHeader = "StatePipesReplyTo";

        private readonly string _bootstrapServers;
        private readonly string _clientCertPath;
        private readonly string _certPassword;
        private readonly string _caPem;
        private readonly IProducer<string, byte[]> _producer;
        private readonly CancellationTokenSource _cts = new();
        private IConsumer<string, byte[]>? _consumer;
        private Thread? _consumeLoop;

        public KafkaServiceDescriptionTransport(string brokerUri, string clientCertPath, string clientCertPasswordPath)
        {
            _bootstrapServers = GetBootstrapServers(brokerUri);
            _clientCertPath = clientCertPath;
            _certPassword = !string.IsNullOrEmpty(clientCertPasswordPath) && File.Exists(clientCertPasswordPath)
                ? File.ReadAllText(clientCertPasswordPath).Trim()
                : string.Empty;
            _caPem = ExtractCaChainPem(_clientCertPath, _certPassword);
            _producer = new ProducerBuilder<string, byte[]>(ApplySsl(new ProducerConfig { Acks = Acks.All }))
                .SetErrorHandler((_, e) => Console.Error.WriteLine($"Kafka producer error: {e.Reason}"))
                .Build();
        }

        public void ListenForResponses(string responseExchangeName, Action<byte[]> onMessageBody)
        {
            var topic = ToTopic(responseExchangeName);
            EnsureTopic(topic);
            // Earliest, not Latest: this topic is created fresh for this one request, so there is no backlog to
            // replay, and reading from the start removes the race where the service answers before our consumer
            // finishes joining its group. KafkaTransport can afford Latest because its consumers are long-lived.
            _consumer = new ConsumerBuilder<string, byte[]>(ApplySsl(new ConsumerConfig
            {
                GroupId = $"{topic}.{Guid.NewGuid():N}", // unique per run => we are not competing with anyone
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = true,
                AllowAutoCreateTopics = true
            }))
                .SetErrorHandler((_, e) => Console.Error.WriteLine($"Kafka consumer error on {topic}: {e.Reason}"))
                .Build();
            _consumer.Subscribe(topic);
            _consumeLoop = new Thread(() => ConsumeLoop(onMessageBody)) { IsBackground = true, Name = $"kafka-selfdescription-{topic}" };
            _consumeLoop.Start();
        }
        private Message<string, byte[]> CreateMessage(string commandTypeName, byte[] replyToBytes)
        {
            return new Message<string, byte[]>
            {
                Key = commandTypeName,
                Value = Encoding.UTF8.GetBytes("{}"),
                Headers = new Headers
                {
                    { StatePipesTypeHeader, Encoding.UTF8.GetBytes(commandTypeName) },
                    { ReplyToHeader, replyToBytes }
                }
            };
        }
        public bool SendGetSelfDescriptionCommand(string commandExchangeName, string commandTypeName, byte[] replyToBytes)
        {
            var topic = ToTopic(commandExchangeName);
            var message = CreateMessage(commandTypeName, replyToBytes);
            try
            {
                var result = _producer.ProduceAsync(topic, message).GetAwaiter().GetResult();
                if (result.Status == PersistenceStatus.NotPersisted)
                {
                    Console.Error.WriteLine($"Failed to send {commandTypeName} to {topic}: not persisted.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to send {commandTypeName} to {topic}: {ex.Message}");
                return false;
            }
            Console.WriteLine($"{commandTypeName} sent to {topic}, waiting for response...");
            return true;
        }

        private void ConsumeLoop(Action<byte[]> onMessageBody)
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    ConsumeResult<string, byte[]> result;
                    try { result = _consumer!.Consume(_cts.Token); }
                    catch (ConsumeException ce) { Console.Error.WriteLine($"Kafka consume error: {ce.Error.Reason}"); continue; }
                    catch (OperationCanceledException) { break; }
                    if (result?.Message?.Value == null) continue;
                    // No StatePipesType filtering here: the response topic carries the GUID of this one request,
                    // so anything that lands on it is the reply we asked for.
                    try { onMessageBody(result.Message.Value); }
                    catch (Exception ex) { Console.Error.WriteLine($"Error handling response message: {ex.Message}"); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.Error.WriteLine($"Kafka consume loop stopped: {ex.Message}"); }
        }

        private void EnsureTopic(string topic)
        {
            try
            {
                using var admin = new AdminClientBuilder(ApplySsl(new AdminClientConfig()))
                    .SetErrorHandler((_, e) => Console.Error.WriteLine($"Kafka admin error: {e.Reason}"))
                    .Build();
                admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]).GetAwaiter().GetResult();
            }
            catch (CreateTopicsException) { /* already exists -- fine, we only need it to be there */ }
            catch (Exception ex) { Console.Error.WriteLine($"Could not ensure Kafka topic {topic}: {ex.Message}"); }
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
            return config;
        }

        private static string GetBootstrapServers(string brokerUri)
        {
            if (Uri.TryCreate(brokerUri, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                return $"{uri.Host}:{(uri.Port > 0 ? uri.Port : DefaultPort)}";
            return brokerUri; // assume already "host:port[,host:port]"
        }

        /// <summary>Same rule as KafkaTopicNamer -- the two must agree or we would listen on the wrong topic.</summary>
        private static string ToTopic(string exchangeName)
        {
            if (string.IsNullOrEmpty(exchangeName)) throw new ArgumentException("Exchange name must be non-empty.", nameof(exchangeName));
            var builder = new StringBuilder(exchangeName.Length);
            foreach (var c in exchangeName) builder.Append(IsLegal(c) ? c : '_');
            var topic = builder.ToString();
            return topic.Length > MaxTopicLength ? topic[..MaxTopicLength] : topic;
        }

        private static bool IsLegal(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';

        /// <summary>
        /// librdkafka reads only the certificate and key out of a keystore and ignores the CA chain inside it,
        /// so the trust anchors are pulled out here and passed separately as SslCaPem.
        /// </summary>
        private static string ExtractCaChainPem(string p12Path, string password)
        {
            var bundle = X509CertificateLoader.LoadPkcs12Collection(File.ReadAllBytes(p12Path), password, X509KeyStorageFlags.EphemeralKeySet);
            var pem = new StringBuilder();
            var count = 0;
            foreach (var certificate in bundle)
            {
                using (certificate)
                {
                    if (certificate.HasPrivateKey) continue; // that entry is our own client identity, not a trust anchor
                    pem.AppendLine(certificate.ExportCertificatePem());
                    count++;
                }
            }
            if (count == 0) throw new InvalidOperationException($"The PKCS#12 bundle {p12Path} contains no CA certificates, so the broker certificate cannot be verified.");
            return pem.ToString();
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _consumeLoop?.Join(TimeSpan.FromSeconds(5)); } catch { }
            try { _consumer?.Close(); } catch { }
            try { _consumer?.Dispose(); } catch { }
            try { _producer.Flush(TimeSpan.FromSeconds(5)); } catch { }
            try { _producer.Dispose(); } catch { }
            try { _cts.Dispose(); } catch { }
        }
    }
}
