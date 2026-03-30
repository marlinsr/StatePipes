using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.BrokerProxy
{
    internal class SimpleConnectionChannel : IDisposable
    {
        private IConnection? _connection;
        private IChannel? _channel;
        private readonly Action<SimpleConnectionChannel>? _configureBuses;
        private readonly CancellationToken _cancelToken;
        private readonly BusConfig _busConfig;
        private readonly object _lock = new();
        private bool _disposedValue;
        private Timer? _timer;
        private readonly string? _hashedPassword;
        public static List<string> DefaultRoutingKeys { get; } = new List<string> { "#" };
        public SimpleConnectionChannel(BusConfig busConfig, string? hashedPassword, Action<SimpleConnectionChannel>? configureBuses = null, CancellationToken cancelToken = default)
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
                    CleanupUnsafe();
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
            _channel = _connection.CreateChannelAsync(null, _cancelToken).GetAwaiter().GetResult();
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
        private string GetQueueName(Guid id, CommunicationsType commsType) => commsType.ToString() + "." + id.ToString("N");
        public void ConfigureBus(Guid id, CommunicationsType commsType, string exchangeName, AsyncEventHandler<BasicDeliverEventArgs>? consumeMethod = null, List<string>? routingKeys = null, bool autoDelete = false)
        {
            if (_channel == null)
            {
                Log?.LogError($"Failed to configure busses because _channel == null");
                return;
            }
            _channel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Topic, autoDelete: autoDelete, durable: true, passive: false, noWait: false, cancellationToken: _cancelToken).GetAwaiter().GetResult();
            if (consumeMethod != null)
            {
                var queueName = GetQueueName(id, commsType);
                _channel.QueueDeclareAsync(queueName).GetAwaiter().GetResult();

                if (routingKeys != null) routingKeys.ForEach(routingKey => _channel.QueueBindAsync(queue: queueName, exchange: exchangeName, routingKey: routingKey, arguments: null, noWait: false, _cancelToken).GetAwaiter().GetResult());
                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += consumeMethod;
                _channel.BasicConsumeAsync(queue: queueName, autoAck: true, consumer: consumer, cancellationToken: _cancelToken).GetAwaiter().GetResult();
            }
        }
        public void Send(byte[] message, string routingKey, BusConfig busConfigFrom, string exchangeName)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(routingKey))
                {
                    Log?.LogError("Failed to get FullName for message.");
                    return;
                }
                if (_channel == null)
                {
                    Log?.LogError($"Failed to send message {routingKey} because _channel == null");
                    return;
                }
                SimpleMessageHelper.Serialize(routingKey, busConfigFrom, out BasicProperties properties);
                try
                {
                    var result = _channel.BasicPublishAsync(exchange: exchangeName,
                                         routingKey: routingKey,
                                         basicProperties: properties,
                                         body: message,
                                         mandatory: false, cancellationToken: _cancelToken);
                    if (!result.IsCompletedSuccessfully) Log?.LogVerbose($"Failed to publish message {routingKey} to exchange {exchangeName}");
                }
                catch (Exception e)
                {
                    Log?.LogError($"Exchange '{exchangeName}' does not exist or has mismatched properties: {routingKey} Exception: {e.Message}");
                }
            }
        }
        /// <summary>
        /// Thread-safe cleanup entry point. Acquires _lock before clearing resources.
        /// Use this when calling from a context that does NOT already hold _lock.
        /// </summary>
        protected void Cleanup()
        {
            lock (_lock)
            {
                CleanupUnsafe();
            }
        }
        /// <summary>
        /// Clears connection, channel, and timer resources.
        /// CALLER MUST HOLD _lock. Use Cleanup() if calling from an unlocked context.
        /// </summary>
        private void CleanupUnsafe()
        {
            _timer?.Dispose();
            _timer = null;
            if (_channel != null) try { _channel.ChannelShutdownAsync -= ChannelShutdown; } catch { }
            ;
            _channel?.Dispose();
            _channel = null;
            if (_connection != null) try { _connection.ConnectionShutdownAsync -= ConnectionShutdown; } catch { }
            ;
            _connection?.Dispose();
            _connection = null;
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Cleanup();
                }

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
