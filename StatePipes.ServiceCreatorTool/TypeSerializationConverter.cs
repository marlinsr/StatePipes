namespace StatePipes.ServiceCreatorTool
{
    public class TypeSerializationConverter
    {
        private TypeRepo _typeRepo = new();

        public TypeSerialization CreateFromType(Type type,
            Type commandType,
            Type eventType)
        {
            TypeToTypeSerializationConverter converter = new(_typeRepo, commandType, eventType);
            return converter.Convert(type);
        }
    }
}
