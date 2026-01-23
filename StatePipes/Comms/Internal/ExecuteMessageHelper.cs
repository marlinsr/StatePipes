using Autofac;
using StatePipes.Interfaces;
using System.Diagnostics;
using System.Reflection;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Comms.Internal
{
    internal static class ExecuteMessageHelper
    {
        internal static void HandleMessage(object message, BusConfig busConfig, bool isResponse, object? handler, Type messageType)
        {
            try
            {
                if (handler == null) return;
                var handlerType = handler.GetType();
                if (handlerType == null) return;
                var parameterTypes = new[] { messageType, typeof(BusConfig), typeof(bool) };
                var handleMessageType = handlerType.GetMethods(BindingFlags.Public | BindingFlags.Instance).ToList()
                     .Where(i => i.Name.Equals("HandleMessage", StringComparison.InvariantCulture))
                     .Where(i => i.GetParameters().Select(a => a.ParameterType).SequenceEqual(parameterTypes)).FirstOrDefault();
                if (handleMessageType == null) return;
                Log?.LogVerbose($"Handling message {messageType.FullName}");
                Stopwatch stopwatch = Stopwatch.StartNew();
                handleMessageType?.Invoke(handler, new object[] { message, busConfig, isResponse });
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds > 500) Log?.LogWarning($"Long running command handler detected. Type: {messageType.FullName}, Time: {stopwatch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex) { Log?.LogInfo(ex.Message); }
        }
        internal static void ExecuteMessage(object message, BusConfig busConfig, bool isResponse, IContainer container)
        {
            try
            {
                var messageType = message.GetType();
                if (messageType == null) return;
                var messageHandlerType = typeof(IMessageHandler<>).MakeGenericType(messageType);
                if (messageHandlerType == null) return;
                if (!container.IsRegistered(messageHandlerType)) return;
                HandleMessage(message, busConfig, isResponse, container.Resolve(messageHandlerType), messageType);
            }
            catch (Exception ex)
            {
                Log?.LogVerbose(ex.Message);
            }
        }
    }
}
