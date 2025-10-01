namespace StatePipes.ServiceCreatorTool
{
    public class TypeSerializationList
    {
        public List<TypeSerialization> TypeSerializations { get; } = [];
        public TypeSerializationList(List<TypeSerialization> typeSerializations)
        {
            TypeSerializations = typeSerializations;
        }
        public TypeSerializationList() { }
    }
}
