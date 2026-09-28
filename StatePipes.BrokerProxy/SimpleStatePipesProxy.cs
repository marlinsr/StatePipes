using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using System;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.BrokerProxy
{
    internal class SimpleStatePipesProxy : IDisposable
    {
        private readonly Guid _id = Guid.NewGuid();
        private bool _disposedValue;
        private readonly BusConfig _busConfig;
        private readonly ReplyToEnvelope _replyTo;
        private ISimpleConnectionChannel? _connectionChannel;
        private string? _hashedPassword;
        public BusConfig BusConfig { get => JsonUtility.Clone(_busConfig); }
        public string Name { get; private set; } = string.Empty;
        public bool IsConnectedToBroker => _connectionChannel?.IsOpen ?? false;
        private SimpleMessageReceived? _messageHandler;
        private SimpleMessageReceived? _responseHandler;
        public SimpleStatePipesProxy(string name, BusConfig busConfig, string? hashedPassword = null)
        {
            Name = name;
            _busConfig = busConfig;
            _replyTo = new ReplyToEnvelope(busConfig);
            _hashedPassword = hashedPassword;
        }

        /// <summary>
        /// <paramref name="eventHandler"/> receives reflected events, <paramref name="responseHandler"/>
        /// reflected responses. Both are handed the reply-to header as raw bytes: the proxy never decodes it.
        /// </summary>
        public void Subscribe(SimpleMessageReceived eventHandler, SimpleMessageReceived responseHandler)
        {
            _messageHandler = eventHandler;
            _responseHandler = responseHandler;
        }
        public void UnSubscribe()
        {
            _messageHandler = null;
            _responseHandler = null;
        }
        public void SendCommand(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw)
        {
            try
            {
                if (_connectionChannel != null && _connectionChannel.IsOpen)
                {
                    // Was: new BusConfig(_busConfig, busConfigFrom) then serialize. Now a byte splice.
                    _connectionChannel.Send(body, routingKey, _replyTo.Wrap(replyToRaw), _busConfig.CommandExchangeName);
                }
                else
                {
                    Log?.LogVerbose($"Failed to send {routingKey}");
                }
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        public void Start()
        {
            if (_connectionChannel != null) return;
            _connectionChannel = SimpleConnectionChannelFactory.Create(_busConfig, _hashedPassword, ConfigureBuses);
        }
        public void Stop()
        {
            try
            {
                _connectionChannel?.Dispose();
                _connectionChannel = null;
            }
            catch { }
        }
        private void ConfigureBuses(ISimpleConnectionChannel connectionChannel)
        {
            try
            {
                connectionChannel.ConfigureBus(_id, CommunicationsType.Event, _busConfig.EventExchangeName, ConsumeEvent, ISimpleConnectionChannel.DefaultRoutingKeys);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Response, _busConfig.ResponseExchangeName, ConsumeResponse, ISimpleConnectionChannel.DefaultRoutingKeys, true);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Command, _busConfig.CommandExchangeName);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        private void ConsumeEvent(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw)
        {
            try
            {
                _messageHandler?.Invoke(body, routingKey, replyToRaw);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        private void ConsumeResponse(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw)
        {
            try
            {
                _responseHandler?.Invoke(body, routingKey, replyToRaw);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
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
