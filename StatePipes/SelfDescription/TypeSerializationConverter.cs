using StatePipes.Messages;
using System.Reflection;

namespace StatePipes.SelfDescription
{
    internal class TypeSerializationConverter
    {
        private readonly TypeRepo _typeRepo;
        private readonly TypeRepo _nullableTypeRepo;
        private readonly Assembly? _callingAssembly;

        public TypeSerializationConverter()
        {
            _callingAssembly = Assembly.GetCallingAssembly();
            _typeRepo = new TypeRepo(_callingAssembly);
            _nullableTypeRepo = new TypeRepo(_callingAssembly);
        }
        public TypeSerialization CreateFromType(Type type)
        {
            TypeToTypeSerializationConverter converter = new(_typeRepo);
            return converter.Convert(type);
        }
        public Type? CreateType(TypeSerialization typeSerialization)
        {
            TypeSerializationToTypeConverter converter = new(_typeRepo, _nullableTypeRepo); 
            return converter.Convert(typeSerialization);
        }
    }
}
