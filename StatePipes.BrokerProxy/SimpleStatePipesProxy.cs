using RabbitMQ.Client.Events;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using StatePipes.Messages;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.BrokerProxy
{
    internal class SimpleStatePipesProxy : IDisposable
    {
        private readonly Guid _id = Guid.NewGuid();
        private bool _disposedValue;
        private readonly BusConfig _busConfig;
        private SimpleConnectionChannel? _connectionChannel;
        private string? _hashedPassword;
        public BusConfig BusConfig { get => JsonUtility.Clone(_busConfig); }
        public string Name { get; private set; } = string.Empty;
        public bool IsConnectedToBroker => _connectionChannel?.IsOpen ?? false;
        private Action<byte[]?, string, BusConfig, bool>? _messageHandler;
        public SimpleStatePipesProxy(string name, BusConfig busConfig, string? hashedPassword = null)
        {
            Name = name;
            _busConfig = busConfig;
            _hashedPassword = hashedPassword;
        }

        public void Subscribe(Action<byte[]?, string, BusConfig, bool> handler)
        {
            _messageHandler = handler;
        }
        public void UnSubscribe()
        {
            _messageHandler = null;
        }
        public void SendCommand(byte[] message, string routingKey, BusConfig busConfigFrom)
        {
            try
            {
                if (_connectionChannel != null && _connectionChannel.IsOpen)
                {
                    _connectionChannel.Send(message, routingKey, new(_busConfig, busConfigFrom), _busConfig.CommandExchangeName);
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
            _connectionChannel = new SimpleConnectionChannel(_busConfig, _hashedPassword, ConfigureBuses);
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
        private void ConfigureBuses(SimpleConnectionChannel connectionChannel)
        {
            try
            {
                connectionChannel.ConfigureBus(_id, CommunicationsType.Event, _busConfig.EventExchangeName, ConsumeEvent, SimpleConnectionChannel.DefaultRoutingKeys);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Response, _busConfig.ResponseExchangeName, ConsumeResponse, SimpleConnectionChannel.DefaultRoutingKeys, true);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Command, _busConfig.CommandExchangeName);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        private Task ConsumeEvent(object model, BasicDeliverEventArgs ea)
        {
            try
            {
                SimpleMessageHelper.Deserialize(ea, out byte[]? eventMessage, out string routingKey, out BusConfig? busConfig);
                if (eventMessage == null || busConfig == null || string.IsNullOrEmpty(routingKey)) return Task.CompletedTask;
                _messageHandler?.Invoke(eventMessage, routingKey, busConfig, false);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
            return Task.CompletedTask;
        }
        private Task ConsumeResponse(object model, BasicDeliverEventArgs ea)
        {
            try
            {
                SimpleMessageHelper.Deserialize(ea, out byte[]? eventMessage, out string routingKey, out BusConfig? busConfig);
                if (eventMessage == null || busConfig == null || string.IsNullOrEmpty(routingKey)) return Task.CompletedTask;
                _messageHandler?.Invoke(eventMessage, routingKey, busConfig, true);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
            return Task.CompletedTask;
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
