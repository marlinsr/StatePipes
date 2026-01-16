using Autofac;
using RabbitMQ.Client.Events;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.Messages;
using System;
using System.Collections.Generic;
using System.Text;
using StatePipes.Comms.Internal;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.BrokerProxy
{
    internal class SimpleStatePipesService : TaskWrapper<BaseMessage>, IDisposable
    {
        private readonly Guid _id = Guid.NewGuid();
        private IContainer? _container;
        private SimpleConnectionChannel? _connectionChannel;
        private readonly BusConfig _busConfig;
        private readonly SimpleStatePipesProxy _proxy;
        public bool IsConnectedToBroker => (_connectionChannel?.IsOpen ?? false);
        public SimpleStatePipesService(BusConfig busConfig, SimpleStatePipesProxy proxy)
        {
            _busConfig = busConfig;
            _proxy = proxy;
            _proxy.Subscribe(ProxyMessageHandler);
        }
        private void EventSendHelper(byte[] message, string routingKey, BusConfig busConfigFrom, string exchangeName)
        {
            try
            {
                if (_connectionChannel != null)
                {
                    if (_connectionChannel.IsOpen)
                    {
                        _connectionChannel.Send(message, routingKey, busConfigFrom, exchangeName);
                    }
                    else
                    {
                        Log?.LogVerbose($"Failed to send {routingKey}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        public void ProxyMessageHandler(byte[]? eventMessage, string routingKey, BusConfig fromBusConfig, bool isResponse)
        {
            if (eventMessage == null) return;
            if (isResponse) SendResponse(eventMessage, routingKey, fromBusConfig.PreviousHop);
            else PublishEvent(eventMessage, routingKey, fromBusConfig);
        }
        public void PublishEvent(byte[] eventMessage, string routingKey, BusConfig fromBusConfig)
        {
            Log?.LogVerbose($"Publishing {routingKey}");
            EventSendHelper(eventMessage, routingKey, new BusConfig(_busConfig, fromBusConfig), _busConfig.EventExchangeName);
        }
        public void SendResponse(byte[] eventMessage, string routingKey, BusConfig? busConfig)
        {
            if (busConfig == null)
            {
                Log?.LogError($"Can't send response {routingKey} because BusConfig is null");
                return;
            }
            if (busConfig.BrokerUri != _busConfig.BrokerUri)
            {
                Log?.LogError($"Can't send response {routingKey} because it is on broker {busConfig.BrokerUri}");
                return;
            }
            if (busConfig.ClientCertPath != _busConfig.ClientCertPath)
            {
                Log?.LogError($"Can't send response {routingKey} because it uses a {busConfig.ClientCertPath} certificate for authentication");
                return;
            }
            Log?.LogVerbose($"Sending response {routingKey} to {busConfig.ResponseExchangeName}");
            EventSendHelper(eventMessage, routingKey, busConfig, busConfig.ResponseExchangeName);
        }
        public void Start() => StartLongRunningAndWait();
        public void Stop()
        {
            try
            {
                Cancel();
                _connectionChannel?.Dispose();
                _connectionChannel = null;
                _container?.Dispose();
                _container = null;
                _proxy.UnSubscribe();
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        private Task ConsumeCommand(object model, BasicDeliverEventArgs ea)
        {
            try
            {
                SimpleMessageHelper.Deserialize(ea, out byte[]? message, out string routingKey, out BusConfig? busConfig);
                if (message == null || busConfig == null || string.IsNullOrEmpty(routingKey)) return Task.CompletedTask;
                _proxy.SendCommand(message, ea.RoutingKey, busConfig);
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
                SimpleMessageHelper.Deserialize(ea, out byte[]? message, out string routingKey, out BusConfig? busConfig);
                if (message == null || busConfig == null || string.IsNullOrEmpty(routingKey)) return Task.CompletedTask;
                SendResponse(message, routingKey, busConfig.PreviousHop);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
            return Task.CompletedTask;
        }
        private void ConfigureBuses(SimpleConnectionChannel connectionChannel)
        {
            try
            {
                connectionChannel.ConfigureBus(_id, CommunicationsType.Command, _busConfig.CommandExchangeName, ConsumeCommand, SimpleConnectionChannel.DefaultRoutingKeys);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Response, _busConfig.ResponseExchangeName, ConsumeResponse, SimpleConnectionChannel.DefaultRoutingKeys, true);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Event, _busConfig.EventExchangeName);
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        protected override void DoWork()
        {
            while (true)
            {
                PerformCancellation();
                if (_connectionChannel == null)
                {
                    try { _connectionChannel = new SimpleConnectionChannel(_busConfig, null, ConfigureBuses); } catch { }
                }
                Thread.Sleep(StatePipesConnectionFactory.HeartbeatIntervalMilliseconds);
            }
        }
        public override void Dispose()
        {
            Stop();
            base.Dispose();
        }
    }
}
