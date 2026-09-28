using Autofac;
using StatePipes.Common;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using System;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.BrokerProxy
{
    internal class SimpleStatePipesService : TaskWrapper<BaseMessage>, IDisposable
    {
        private readonly Guid _id = Guid.NewGuid();
        private IContainer? _container;
        private ISimpleConnectionChannel? _connectionChannel;
        private readonly BusConfig _busConfig;
        private readonly ReplyToEnvelope _replyTo;
        private readonly SimpleStatePipesProxy _proxy;
        public bool IsConnectedToBroker => (_connectionChannel?.IsOpen ?? false);
        public SimpleStatePipesService(BusConfig busConfig, SimpleStatePipesProxy proxy)
        {
            _busConfig = busConfig;
            _replyTo = new ReplyToEnvelope(busConfig);
            _proxy = proxy;
            _proxy.Subscribe(PublishEvent, ReflectResponse);
        }
        private void EventSendHelper(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw, string exchangeName)
        {
            try
            {
                if (_connectionChannel != null)
                {
                    if (_connectionChannel.IsOpen)
                    {
                        _connectionChannel.Send(body, routingKey, replyToRaw, exchangeName);
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
        /// <summary>
        /// Reflects an event onto the destination broker. The incoming reply-to header is spliced in as this
        /// hop's PreviousHop without ever being parsed -- the whole point of ReplyToEnvelope.
        /// </summary>
        public void PublishEvent(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw)
        {
            Log?.LogVerbose($"Publishing {routingKey}");
            EventSendHelper(body, routingKey, _replyTo.Wrap(replyToRaw), _busConfig.EventExchangeName);
        }
        /// <summary>
        /// Reflects a response back to the hop that issued the command. This is the one path that has to read
        /// the reply-to header, because the destination is derived from PreviousHop's fields.
        /// </summary>
        public void ReflectResponse(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw)
        {
            var previousHop = SimpleMessageHelper.ReadPreviousHop(replyToRaw);
            SendResponse(body, routingKey, previousHop);
        }
        public void SendResponse(byte[] body, string routingKey, BusConfig? busConfig)
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
            EventSendHelper(body, routingKey, new ReplyToEnvelope(busConfig).SelfOnly, busConfig.ResponseExchangeName);
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
        // routingKey is the message type name, taken from the AMQP Type property or the Kafka StatePipesType
        // header. The previous version forwarded ea.RoutingKey here; publishers always set the AMQP routing key
        // to that same type name (see Send), so this is the same value with one less broker concept in the way.
        private void ConsumeCommand(byte[] body, string routingKey, ReadOnlyMemory<byte> replyToRaw)
        {
            try
            {
                _proxy.SendCommand(body, routingKey, replyToRaw);
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
                SendResponse(body, routingKey, SimpleMessageHelper.ReadPreviousHop(replyToRaw));
            }
            catch (Exception ex)
            {
                Log?.LogException(ex);
            }
        }
        private void ConfigureBuses(ISimpleConnectionChannel connectionChannel)
        {
            try
            {
                connectionChannel.ConfigureBus(_id, CommunicationsType.Command, _busConfig.CommandExchangeName, ConsumeCommand, ISimpleConnectionChannel.DefaultRoutingKeys);
                connectionChannel.ConfigureBus(_id, CommunicationsType.Response, _busConfig.ResponseExchangeName, ConsumeResponse, ISimpleConnectionChannel.DefaultRoutingKeys, true);
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
                    try { _connectionChannel = SimpleConnectionChannelFactory.Create(_busConfig, null, ConfigureBuses); } catch { }
                }
                Thread.Sleep(TransportConstants.HeartbeatIntervalMilliseconds);
            }
        }
        public override void Dispose()
        {
            Stop();
            base.Dispose();
        }
    }
}
