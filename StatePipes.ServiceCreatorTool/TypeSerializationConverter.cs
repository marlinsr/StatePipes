namespace StatePipes.ServiceCreatorTool
{
    public class TypeSerializationConverter
    {
        private TypeRepo _typeRepo = new TypeRepo();

        public TypeSerialization CreateFromType(Type type,
            Type commandType,
            Type eventType)
        {
            TypeToTypeSerializationConverter converter = new TypeToTypeSerializationConverter(_typeRepo, commandType, eventType);
            return converter.Convert(type);
        }
    }
}
