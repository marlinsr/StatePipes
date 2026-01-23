using Autofac.Util;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using System.Reflection;

namespace StatePipes.Common.Internal
{
    //For dynamically created types used by the explorer
    internal class TypeDictionary
    {
        private Dictionary<string, Type> _typeDictionary = [];
        private static bool IsConcrete(Type type) => !type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition;
        public void SetupAssembylyMessageTypes(Assembly? assembly)
        {
            if (assembly == null) return;
            var types = assembly.GetLoadableTypes().Where(t => t.IsPublic && IsConcrete(t) && t.GetInterfaces().Any(i => typeof(IMessage).IsAssignableFrom(i)));
            foreach (var type in types)
            {
                var fullName = type.FullName;
                if (string.IsNullOrEmpty(fullName)) continue;
                if (_typeDictionary.ContainsKey(fullName)) continue;
                _typeDictionary[fullName] = type;
            }
        }
        public TypeDictionary()
        {
            var statePipesAssebly = typeof(StatePipesService).Assembly;
            SetupAssembylyMessageTypes(statePipesAssebly);
        }
        public void Add(Type type) => Add(type.FullName, type);
        public void Add(string? receivedEventTypeFullName, Type convertToType)
        {
            lock (_typeDictionary)
            {
                if (string.IsNullOrEmpty(receivedEventTypeFullName)) return;
                _typeDictionary[receivedEventTypeFullName] = convertToType;
            }
        }
        public Type? Get(string fullName)
        {
            //SRM Remove Later
            if (fullName.Contains("DummyEvent")) StatePipes.ProcessLevelServices.LoggerHolder.Log?.LogInfo("Received DummyEvent");
            lock (_typeDictionary)
            {
                if (!_typeDictionary.TryGetValue(fullName, out Type? value)) return null;
                return value;
            }
        }
    }
}
