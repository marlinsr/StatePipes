using Newtonsoft.Json;
using System.Text;

namespace StatePipes.ServiceCreatorTool
{
    /// <summary>
    /// Asks a running service to describe itself and waits for the answer. Transport-neutral: the RabbitMQ or
    /// Kafka specifics live behind <see cref="IServiceDescriptionTransport"/>, chosen by
    /// <see cref="ServiceDescriptionTransportFactory"/> from the broker URI.
    /// </summary>
    internal class LiveServiceDescriptionClient(
        string brokerUri,
        string exchangeName,
        string clientCertPath,
        string clientCertPasswordPath)
    {
        private const string GetSelfDescriptionCommandTypeName = "StatePipes.Messages.GetSelfDescriptionCommand";

        public TypeSerializationList? Fetch(int timeoutSeconds = 30)
        {
            string responseGuid = Guid.NewGuid().ToString("N");
            // These two must match BusConfig.CommandExchangeName / ResponseExchangeName (with an empty postfix),
            // because the service derives its reply destination from the BusConfig we send in the header below.
            string commandExchange = $"{exchangeName}.commands";
            string responseExchange = $"{exchangeName}.{responseGuid}.responses";
            var tcs = new TaskCompletionSource<TypeSerializationList?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var transport = ServiceDescriptionTransportFactory.Create(brokerUri, clientCertPath, clientCertPasswordPath);
            transport.ListenForResponses(responseExchange, body => tcs.TrySetResult(ParseSelfDescription(body)));
            if (!transport.SendGetSelfDescriptionCommand(commandExchange, GetSelfDescriptionCommandTypeName, GetBusConfigBytes(responseGuid))) return null;
            return TimeoutDetection(timeoutSeconds, tcs) ? null : tcs.Task.GetAwaiter().GetResult();
        }

        private byte[] GetBusConfigBytes(string responseGuid)
        {
            // Shaped like BusConfig so the service can deserialize it straight out of the StatePipesReplyTo
            // header. BrokerUri is what tells the service which transport to answer on, so the ssl:// prefix
            // that selected Kafka here selects Kafka there too.
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

        private static TypeSerializationList? ParseSelfDescription(byte[] body)
        {
            try
            {
                var envelope = JsonConvert.DeserializeObject<SelfDescriptionEventEnvelope>(Encoding.UTF8.GetString(body));
                return envelope?.TypeList;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error deserializing SelfDescriptionEvent: {ex.Message}");
                return null;
            }
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
    }
}
