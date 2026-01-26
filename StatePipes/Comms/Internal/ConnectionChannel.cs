using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StatePipes.Interfaces;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Comms.Internal
{
    internal class ConnectionChannel: IDisposable
    {
        private IConnection? _connection;
        private IChannel? _channel;
        private readonly Action<ConnectionChannel>? _configureBuses;
        private readonly CancellationToken _cancelToken;
        private readonly BusConfig _busConfig;
        private readonly System.Threading.Lock _lock = new();
        private bool _disposedValue;
        private Timer? _timer;
        private readonly string? _hashedPassword;
        public static List<string> DefaultRoutingKeys { get; } = ["#"];
        public ConnectionChannel(BusConfig busConfig, string? hashedPassword, Action<ConnectionChannel>? configureBuses = null, CancellationToken cancelToken = default)
        {
            _configureBuses = configureBuses;
            _cancelToken = cancelToken;
            _busConfig = busConfig;
            _hashedPassword = hashedPassword;
            InstantiateConnectionAndChannel(null);
        }
        private void InstantiateConnectionAndChannel(object? state)
        {
            lock (_lock)
            {
                try
                {
                    Cleanup();
                    _connection = StatePipesConnectionFactory.CreateConnection(_busConfig, _hashedPassword, _cancelToken);
                    _connection.ConnectionShutdownAsync += ConnectionShutdown;
                    CreateChannel();
                }
                catch
                {
                    ConnectionShutdownWorker();
                }
            }
        }
        private Task ConnectionShutdown(object sender, ShutdownEventArgs @event)
        {
            ConnectionShutdownWorker();
            return Task.CompletedTask;
        }

        private void ConnectionShutdownWorker()
        {
            _timer = new Timer(
                InstantiateConnectionAndChannel,
                null,
                TimeSpan.FromMilliseconds(StatePipesConnectionFactory.HeartbeatIntervalMilliseconds),
                TimeSpan.FromMilliseconds(Timeout.Infinite));
        }
        private void CreateChannel()
        {
            if (_connection == null)
            {
                Log?.LogError("Creating Channel when Connection is null, should never happen");
                InstantiateConnectionAndChannel(null);
                return;
            }
            _channel = _connection.CreateChannelAsync(null, _cancelToken).Result;
            _channel.ChannelShutdownAsync += ChannelShutdown;
            _configureBuses?.Invoke(this);
        }
        private Task ChannelShutdown(object sender, ShutdownEventArgs ev)
        {
            ConnectionShutdownWorker();
            return Task.CompletedTask;
        }
        public bool IsOpen
        {
            get
            {
                lock (_lock)
                {
                    return (_connection?.IsOpen ?? false) && (_channel?.IsOpen ?? false);
                }
            }
        }
        private static string GetQueueName(Guid id, CommunicationsType commsType) => commsType.ToString() + "." + id.ToString("N");
        public void ConfigureBus(Guid id, CommunicationsType commsType, string exchangeName, AsyncEventHandler<BasicDeliverEventArgs>? consumeMethod = null, List<string>? routingKeys = null, bool autoDelete = false)
        {
            //No Need to lock this because InstantiateConnectionAndChannel locks
            if (_channel == null)
            {
                Log?.LogError($"Failed to configure busses because _channel == null");
                return;
            }
            _channel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Topic, autoDelete: autoDelete, durable: true, passive: false, noWait: false, cancellationToken: _cancelToken).Wait();
            if (consumeMethod != null)
            {
                var queueName = GetQueueName(id, commsType);
                _channel.QueueDeclareAsync(queueName).Wait();

                routingKeys?.ForEach(routingKey => _channel.QueueBindAsync(queue: queueName, exchange: exchangeName, routingKey: routingKey, arguments: null, noWait: false, _cancelToken).Wait());
                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += consumeMethod;
                _channel.BasicConsumeAsync(queue: queueName, autoAck: true, consumer: consumer, cancellationToken: _cancelToken).Wait();
            }
        }
        public void Subscribe(Guid id, string routingKey, BusConfig busConfig)
        {
            lock (_lock)
            {
                _channel?.QueueBindAsync(queue: GetQueueName(id, CommunicationsType.Event), exchange: busConfig.EventExchangeName, routingKey: routingKey);
            }
        }
        public void UnSubscribe(Guid id, string routingKey, BusConfig busConfig)
        {
            lock (_lock)
            {
                _channel?.QueueUnbindAsync(queue: GetQueueName(id, CommunicationsType.Event), exchange: busConfig.EventExchangeName, routingKey: routingKey);
            }
        }
        public void Send<T>(T message, BusConfig busConfigFrom, string exchangeName) where T : IMessage => Send<T>(message.GetType().FullName, message, busConfigFrom, exchangeName);
        public void Send<T>(string? sendCommandTypeFullName, T message, BusConfig busConfigFrom, string exchangeName) 
        {
            if(message == null || string.IsNullOrEmpty(sendCommandTypeFullName)) return;
            MessageHelper.Serialize(sendCommandTypeFullName, message, busConfigFrom, out byte[] body, out BasicProperties properties);
            lock (_lock)
            {
                if (_channel == null) return;
                try
                {
                    var result = _channel.BasicPublishAsync(exchange: exchangeName, routingKey: sendCommandTypeFullName, basicProperties: properties,
                                         body: body, mandatory: false, cancellationToken: _cancelToken);
                    if (!result.IsCompletedSuccessfully) Log?.LogVerbose($"Failed to publish message{message.GetType().FullName} to exchange {exchangeName}");
                }
                catch (Exception e) { Log?.LogError($"Exchange '{exchangeName}' does not exist or has mismatched properties: {sendCommandTypeFullName} Exception: {e.Message}"); }
            }
        }
        protected void Cleanup()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                if (_channel != null) try { _channel.ChannelShutdownAsync -= ChannelShutdown; } catch { };
                _channel?.Dispose();
                _channel = null;
                if (_connection != null) try { _connection.ConnectionShutdownAsync -= ConnectionShutdown; } catch { };
                _connection?.Dispose();
                _connection = null;
            }
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    Cleanup();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                _disposedValue = true;
            }
        }
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
