using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using StatePipes.SelfDescription;
using System.Reflection;
using static StatePipes.ProcessLevelServices.LoggerHolder;

namespace StatePipes.Explorer.NonWebClasses
{
    internal class DynamicEventHandler : IDisposable
    {
        private readonly StatePipesProxyInternal _proxy;
        private readonly EventJsonRepository _josonRepo = new();
        private readonly TypeSerializationJsonHelper _eventInstanceMgr;
        private bool disposedValue;
        public DynamicEventHandler(StatePipesProxyInternal proxy, EventJsonRepository josonRepo, TypeSerializationJsonHelper eventInstanceMgr)
        {
            _proxy = proxy;
            _josonRepo = josonRepo;
            _eventInstanceMgr = eventInstanceMgr;
            Subscribe();
        }
        private void Subscribe()
        {
            if (_eventInstanceMgr.ThisType == null) return;
            var methodInfo = GetType()?.GetMethod(nameof(OnTestEvent), BindingFlags.NonPublic | BindingFlags.Instance)?.MakeGenericMethod(_eventInstanceMgr.ThisType);
            if (methodInfo == null) return;
            var action = CreateDelegateByParameter(_eventInstanceMgr.ThisType, methodInfo);
            if (action == null) return;
            var subscribeMethod = _proxy.GetType()?.GetMethod("Subscribe", BindingFlags.Public | BindingFlags.Instance);
            if (subscribeMethod == null) return;
            var subscribeMethodOfEventType = subscribeMethod.MakeGenericMethod([_eventInstanceMgr.ThisType]);
            subscribeMethodOfEventType?.Invoke(_proxy, [action]);
        }
        private void UnSubscribe()
        {
            if (_eventInstanceMgr.ThisType == null) return;
            var methodInfo = GetType()?.GetMethod(nameof(OnTestEvent), BindingFlags.NonPublic | BindingFlags.Instance)?.MakeGenericMethod(_eventInstanceMgr.ThisType);
            if (methodInfo == null) return;
            var action = CreateDelegateByParameter(_eventInstanceMgr.ThisType, methodInfo!);
            if(action == null) return;  
            var unsubscribeMethod = _proxy.GetType().GetMethod("UnSubscribe", BindingFlags.Public | BindingFlags.Instance);
            if(unsubscribeMethod == null) return;
            var unsubscribeMethodOfEventType = unsubscribeMethod.MakeGenericMethod([_eventInstanceMgr.ThisType]);
            unsubscribeMethodOfEventType?.Invoke(_proxy, [action]);
        }
        private object? CreateDelegateByParameter(Type parameterType, MethodInfo method)
        {
            var createDelegate = GetType()?.GetMethod(nameof(CreateDelegate), BindingFlags.NonPublic | BindingFlags.Instance)
                ?.MakeGenericMethod([parameterType]);

            var del = createDelegate?.Invoke(this, [method]);

            return del;
        }
        private Action<TEvent, BusConfig, bool>? CreateDelegate<TEvent>(MethodInfo method)
        {
            var del = (Action<TEvent, BusConfig, bool>?)Delegate.CreateDelegate(typeof(Action<TEvent, BusConfig, bool>), this, method, false)!;
            return del;
        }
#pragma warning disable IDE0060 // Remove unused parameter
        private void OnTestEvent<T>(T ev, BusConfig busConfig, bool isResponse) where T : IEvent
#pragma warning restore IDE0060 // Remove unused parameter
        {
            Log?.LogVerbose($"Received Event {ev.GetType().Name}");
            _josonRepo.SetJsonString(ev);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    UnSubscribe();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
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
