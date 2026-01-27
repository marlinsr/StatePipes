using StatePipes.Common;
using StatePipes.Messages;

namespace StatePipes.SelfDescription
{
    internal class TypeSerializationJsonHelper
    {
        private readonly TypeSerialization _typeSerialization;
        private readonly TypeSerializationConverter _typeSerializationConverter;
        public Type? ThisType { get; }
        public TypeSerializationJsonHelper(TypeSerialization thisTypeSerialization, TypeSerializationConverter typeSerializationConverter)
        {
            _typeSerializationConverter = typeSerializationConverter;
            _typeSerialization = thisTypeSerialization;
            ThisType = _typeSerializationConverter.CreateType(_typeSerialization);
        }
        public string GenerateExampleJson()
        {
            JsonExampleGenerator exampleCreator = new(_typeSerialization, _typeSerializationConverter);
            string nonFormattedJson = exampleCreator.GenerateJsonExample();
            dynamic? obj = GetObjectFromJson(nonFormattedJson);
            return JsonUtility.GetJsonStringForObject(obj);
        }
        public dynamic? TypeDefault(Type t)
        {
            JsonExampleGenerator exampleCreator = new(_typeSerialization, _typeSerializationConverter);
            return JsonUtility.GetObjectFromJson(exampleCreator.GenerateDefault(t),t);
        }
        public dynamic? GetObjectFromJson(string jsonString) => ThisType == null ? null : JsonUtility.GetObjectFromJson(jsonString, ThisType);
    }
}
