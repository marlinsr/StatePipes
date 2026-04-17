using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace StatePipes.ServiceCreatorTool
{
    internal class LiveServiceDescriptionClient(
        string brokerUri,
        string exchangeName,
        string clientCertPath,
        string clientCertPasswordPath)
    {
        private const string ReplyToHeader = "StatePipesReplyTo";
        private const string GetSelfDescriptionCommandTypeName = "StatePipes.Messages.GetSelfDescriptionCommand";

        public TypeSerializationList? Fetch(int timeoutSeconds = 30)
        {
            string responseGuid = Guid.NewGuid().ToString("N");
            string responseExchange = $"{exchangeName}.{responseGuid}.responses";
            var factory = BuildConnectionFactory();
            using var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            using var channel = connection.CreateChannelAsync().GetAwaiter().GetResult();
            channel.ExchangeDeclareAsync(responseExchange, ExchangeType.Topic, durable: true, autoDelete: true).GetAwaiter().GetResult();
            var queueResult = channel.QueueDeclareAsync(exclusive: true, autoDelete: true).GetAwaiter().GetResult();
            channel.QueueBindAsync(queueResult.QueueName, responseExchange, "#").GetAwaiter().GetResult();
            var tcs = new TaskCompletionSource<TypeSerializationList?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var consumer = new AsyncEventingBasicConsumer(channel);
            SetUpReceiver(consumer, tcs);
            channel.BasicConsumeAsync(queueResult.QueueName, autoAck: true, consumer).GetAwaiter().GetResult();
            if (!SendGetSelfDescriptionCommand(channel, GetBusConfigBytes(responseGuid))) return null;
            return TimeoutDetection( timeoutSeconds, tcs) ? null : tcs.Task.GetAwaiter().GetResult();
        }

        private byte[] GetBusConfigBytes(string responseGuid)
        {
            var replyToBusConfig = new
            {
                BrokerUri = brokerUri,
                ExchangeNamePrefix = exchangeName,
                ClientCertPath = Path.GetFileName(clientCertPath),
                ClientCertPasswordPath = Path.GetFileName(clientCertPasswordPath),
                ResponseExchangeGuid = responseGuid,
                PreviousHop = (object?)null
            };
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(replyToBusConfig));          
        }

        private static void SetUpReceiver(AsyncEventingBasicConsumer consumer, TaskCompletionSource<TypeSerializationList?> tcs)
        {
            consumer.ReceivedAsync += (_, ea) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var envelope = JsonConvert.DeserializeObject<SelfDescriptionEventEnvelope>(json);
                    tcs.TrySetResult(envelope?.TypeList);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error deserializing SelfDescriptionEvent: {ex.Message}");
                    tcs.TrySetResult(null);
                }
                return Task.CompletedTask;
            };
        }

        private bool SendGetSelfDescriptionCommand(IChannel channel, byte[] replyToBytes)
        {
            var commandsExchange = $"{exchangeName}.commands";
            var props = new BasicProperties
            {
                Type = GetSelfDescriptionCommandTypeName,
                Headers = new Dictionary<string, object?> { { ReplyToHeader, replyToBytes } }
            };
            var result = channel.BasicPublishAsync(exchange: commandsExchange, routingKey: GetSelfDescriptionCommandTypeName, mandatory: false, basicProperties: props, body: Encoding.UTF8.GetBytes("{}"));
            if (!result.IsCompletedSuccessfully)
            {
                Console.Error.WriteLine($"Failed to send GetSelfDescriptionCommand to {commandsExchange}.");
                return false;
            }
            Console.WriteLine($"GetSelfDescriptionCommand sent to {commandsExchange}, waiting for response...");
            return true;
        }

        private static bool TimeoutDetection(int timeoutSeconds, TaskCompletionSource<TypeSerializationList?> tcs)
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var completed = Task.WhenAny(tcs.Task, timeoutTask).GetAwaiter().GetResult();
            if (completed == timeoutTask)
            {
                Console.Error.WriteLine($"Timed out after {timeoutSeconds}s waiting for SelfDescriptionEvent.");
                return true;
            }
            return false;
        }

        private ConnectionFactory BuildConnectionFactory()
        {
            var uri = new Uri(brokerUri);
            string password = !string.IsNullOrEmpty(clientCertPasswordPath) && File.Exists(clientCertPasswordPath) ? File.ReadAllText(clientCertPasswordPath).Trim() : string.Empty;
            var factory = new ConnectionFactory
            {
                Uri = uri,
                Port = 5671,
                RequestedHeartbeat = TimeSpan.FromSeconds(1),
                AuthMechanisms = [new ExternalMechanismFactory()],
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false
            };
            if (!string.IsNullOrEmpty(clientCertPath) && File.Exists(clientCertPath))
            {
                var cert = X509CertificateLoader.LoadPkcs12FromFile(clientCertPath, password);
                var certs = new X509CertificateCollection {cert };
                factory.Ssl = new SslOption {Enabled = true, ServerName = uri.Host, Certs = certs, Version = SslProtocols.Tls13 };
            }
            return factory;
        }
    }
}
