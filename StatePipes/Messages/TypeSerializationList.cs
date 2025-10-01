using Newtonsoft.Json;
using StatePipes.Common;

namespace StatePipes.Messages
{
    public class TypeSerializationList
    {
        public List<TypeSerialization> TypeSerializations { get; } = [];

        [JsonConstructor]
        public TypeSerializationList(List<TypeSerialization> typeSerializations)
        {
            TypeSerializations = typeSerializations;
        }
        public TypeSerializationList() { }
        public string JsonSerialize() => JsonUtility.GetJsonStringForObject(this);
        public static TypeSerializationList? JsonDeserialize(string jsonString) => JsonUtility.GetObjectForJsonString<TypeSerializationList>(jsonString);
    }
}
