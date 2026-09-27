using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace StatePipes.ServiceCreatorTool
{
    /// <summary>
    /// RabbitMQ flavor of <see cref="IServiceDescriptionTransport"/>: a topic exchange for the replies with an
    /// exclusive auto-delete queue bound to it, and a direct publish onto the service's commands exchange.
    /// </summary>
    internal sealed class RabbitMqServiceDescriptionTransport : IServiceDescriptionTransport
    {
        private const string ReplyToHeader = "StatePipesReplyTo";
        private const int TlsPort = 5671;
        private readonly IConnection _connection;
        private readonly IChannel _channel;

        public RabbitMqServiceDescriptionTransport(string brokerUri, string clientCertPath, string clientCertPasswordPath)
        {
            var factory = BuildConnectionFactory(brokerUri, clientCertPath, clientCertPasswordPath);
            _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();
        }

        public void ListenForResponses(string responseExchangeName, Action<byte[]> onMessageBody)
        {
            _channel.ExchangeDeclareAsync(responseExchangeName, ExchangeType.Topic, durable: true, autoDelete: true).GetAwaiter().GetResult();
            var queueResult = _channel.QueueDeclareAsync(exclusive: true, autoDelete: true).GetAwaiter().GetResult();
            _channel.QueueBindAsync(queueResult.QueueName, responseExchangeName, "#").GetAwaiter().GetResult();
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (_, ea) =>
            {
                try { onMessageBody(ea.Body.ToArray()); }
                catch (Exception ex) { Console.Error.WriteLine($"Error handling response message: {ex.Message}"); }
                return Task.CompletedTask;
            };
            _channel.BasicConsumeAsync(queueResult.QueueName, autoAck: true, consumer).GetAwaiter().GetResult();
        }

        public bool SendGetSelfDescriptionCommand(string commandExchangeName, string commandTypeName, byte[] replyToBytes)
        {
            var props = new BasicProperties
            {
                Type = commandTypeName,
                Headers = new Dictionary<string, object?> { { ReplyToHeader, replyToBytes } }
            };
            var result = _channel.BasicPublishAsync(exchange: commandExchangeName, routingKey: commandTypeName, mandatory: false, basicProperties: props, body: Encoding.UTF8.GetBytes("{}"));
            if (!result.IsCompletedSuccessfully)
            {
                Console.Error.WriteLine($"Failed to send {commandTypeName} to {commandExchangeName}.");
                return false;
            }
            Console.WriteLine($"{commandTypeName} sent to {commandExchangeName}, waiting for response...");
            return true;
        }

        private static ConnectionFactory BuildConnectionFactory(string brokerUri, string clientCertPath, string clientCertPasswordPath)
        {
            var uri = new Uri(brokerUri);
            string password = !string.IsNullOrEmpty(clientCertPasswordPath) && File.Exists(clientCertPasswordPath) ? File.ReadAllText(clientCertPasswordPath).Trim() : string.Empty;
            var factory = new ConnectionFactory
            {
                Uri = uri,
                Port = TlsPort,
                RequestedHeartbeat = TimeSpan.FromSeconds(1),
                AuthMechanisms = [new ExternalMechanismFactory()],
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false
            };
            if (!string.IsNullOrEmpty(clientCertPath) && File.Exists(clientCertPath))
            {
                var cert = X509CertificateLoader.LoadPkcs12FromFile(clientCertPath, password);
                var certs = new X509CertificateCollection { cert };
                factory.Ssl = new SslOption { Enabled = true, ServerName = uri.Host, Certs = certs, Version = SslProtocols.Tls13 };
            }
            return factory;
        }

        public void Dispose()
        {
            try { _channel.Dispose(); } catch { }
            try { _connection.Dispose(); } catch { }
        }
    }
}
