using StatePipes.Common;
using StatePipes.ExplorerTypes;
using StatePipes.Messages;
using System.Xml.Linq;
namespace StatePipes.SelfDescription
{
    internal class JsonExampleGenerator(TypeSerialization thisTypeSerialization, TypeSerializationConverter typeSerializationConverter)
    {
        private readonly TypeSerialization _thisTypeSerialization = thisTypeSerialization;
        private readonly TypeSerializationConverter _typeSerializationConverter = typeSerializationConverter;
        public string GenerateDefault(Type t)
        {
            var td = _thisTypeSerialization.GetDescription(t.FullName!);
            return GenerateDefault(td);
        }
        private static string? GenerateDefaultFromAttributes(List<AttributeDescription> attrs)
        {
            var defaultAttr = attrs.FirstOrDefault(a => a.FullName.Contains(typeof(SPEDefaultAttribute).FullName!));
            if (defaultAttr is null) return null;
            return JsonUtility.GetObjectForJsonString<SPEDefaultAttribute>(defaultAttr.Value)?.DefaultValue;
        }
        private string GenerateDefault(TypeDescription typeDescription)
        {
            var defaultJson = GenerateDefaultFromAttributes(typeDescription.Attributes);
            if (defaultJson is not null) return defaultJson;
            var tempTsi = _thisTypeSerialization.CreateSubTypeSerialization(typeDescription.FullName);
            if (tempTsi == null) return "{}";
            var type = _typeSerializationConverter.CreateType(tempTsi);
            if (type == null) return "{}";
            if (type.IsPrimitive || type == typeof(decimal))
            {
                return Activator.CreateInstance(type)?.ToString()?.ToLower() ?? string.Empty;
            }
            else if (type.IsEnum)
            {
                return ((int)Activator.CreateInstance(type)!).ToString();
            }
            else if (type == typeof(string))
            {
                return $"\"\"";
            }
            else if (type == typeof(Guid))
            {
                return $"\"{Guid.NewGuid()}\"";
            }
            else if (type == typeof(XElement))
            {
                return JsonUtility.GetJsonStringForObject(new XElement("Default"));
            }
            else if (type == typeof(DateTime))
            {
                return $"\"{DateTime.Now.ToString()}\"";
            }
            else if (type == typeof(DateTimeOffset))
            {
                return $"\"{DateTimeOffset.Now.ToString()}\"";
            }
            else if (type == typeof(TimeSpan))
            {
                return $"\"{TimeSpan.Zero.ToString()}\"";
            }
            else
            {
                return GenerateJsonWorker(string.Empty, typeDescription);
            }
        }
        private static void DictionaryGenerator(ref string outputString, string key, string val)
        {
            outputString += "{";
            outputString += "}";
        }
        private static void RankGenerator(ref string outputString, int rank, string element, int numElements)
        {
            if (rank == 1)
            {
                for (int i = 0; i < numElements; i++)
                {
                    if (i != 0)
                    {
                        outputString += ", ";
                    }
                    outputString += element;
                }
            }
            else
            {
                for (int i = 0; i < numElements; i++)
                {
                    if (i != 0) outputString += ", ";
                    outputString += "[";
                    RankGenerator(ref outputString, rank - 1, element, numElements);
                    outputString += "]";
                }
            }
        }
        private string ArrayGenerator(int propArrayRank, TypeDescription arrayType)
        {
            string ret = string.Empty;
            ret += "[";
            RankGenerator(ref ret, propArrayRank, GenerateDefault(arrayType), 0);
            ret += "]";

            return ret;
        }
        private string GenerateProperty(ParameterDescription parameterDescription)
        {
            string propertyJson = string.Empty;
            var defaultJson = GenerateDefaultFromAttributes(parameterDescription.Attributes);
            var propTypeDescription = _thisTypeSerialization.GetDescription(parameterDescription.FullName);
            if (defaultJson is not null)
            {
                propertyJson += defaultJson;
            }
            else if (propTypeDescription.FullName == typeof(byte[]).FullName
                || propTypeDescription.IsGeneric() && propTypeDescription.GenericNames!.FullName == typeof(List<>).FullName && propTypeDescription.GenericArgumentsNames[0].FullName == typeof(byte).FullName
                || propTypeDescription.IsGeneric() && propTypeDescription.GenericNames!.FullName == typeof(IReadOnlyList<>).FullName && propTypeDescription.GenericArgumentsNames[0].FullName == typeof(byte).FullName
                )
            {
                propertyJson += "\"\\\"\\\"\"";
            }
            else if (propTypeDescription.IsArray())
            {
                propertyJson += ArrayGenerator(propTypeDescription.ArrayRank, _thisTypeSerialization.GetDescription(propTypeDescription.FullName));
            }
            else if (propTypeDescription.IsGeneric() && propTypeDescription.GenericNames!.FullName == typeof(List<>).FullName)
            {
                propertyJson += ArrayGenerator(1, _thisTypeSerialization.GetDescription(propTypeDescription.GenericArgumentsNames[0].FullName));
            }
            else if (propTypeDescription.IsGeneric() && propTypeDescription.GenericNames!.FullName == typeof(HashSet<>).FullName)
            {
                propertyJson += ArrayGenerator(1, _thisTypeSerialization.GetDescription(propTypeDescription.GenericArgumentsNames[0].FullName));
            }
            else if (propTypeDescription.IsGeneric() && propTypeDescription.GenericNames!.FullName == typeof(IReadOnlyList<>).FullName)
            {
                propertyJson += ArrayGenerator(1, _thisTypeSerialization.GetDescription(propTypeDescription.GenericArgumentsNames[0].FullName));
            }
            else if (propTypeDescription.IsGeneric() && propTypeDescription.GenericNames!.FullName == typeof(IEnumerable<>).FullName)
            {
                propertyJson += ArrayGenerator(1, _thisTypeSerialization.GetDescription(propTypeDescription.GenericArgumentsNames[0].FullName));
            }
            else if (propTypeDescription.IsGeneric() && propTypeDescription.GenericNames!.FullName == typeof(Dictionary<,>).FullName)
            {
                DictionaryGenerator(ref propertyJson, GenerateDefault(_thisTypeSerialization.GetDescription(propTypeDescription.GenericArgumentsNames[0].FullName)), GenerateDefault(_thisTypeSerialization.GetDescription(propTypeDescription.GenericArgumentsNames[1].FullName)));
            }
            else if (propTypeDescription.IsGeneric() && propTypeDescription.GenericNames!.FullName == typeof(IReadOnlyDictionary<,>).FullName)
            {
                DictionaryGenerator(ref propertyJson, GenerateDefault(_thisTypeSerialization.GetDescription(propTypeDescription.GenericArgumentsNames[0].FullName)), GenerateDefault(_thisTypeSerialization.GetDescription(propTypeDescription.GenericArgumentsNames[1].FullName)));
            }
            else
            {
                propertyJson += GenerateDefault(propTypeDescription);
            }
            return propertyJson;
        }
        private string GenerateJsonWorker(string currentJson, TypeDescription td)
        {
            var tsJson = GenerateDefaultFromAttributes(td.Attributes);
            if (tsJson != null)
            {
                return currentJson += tsJson;
            }
            string classJson = "{";
            int propertyNumber = 0;
            foreach (var prop in td.Properties)
            {
                if (propertyNumber++ > 0)
                {
                    classJson += ",";
                }
                classJson += $"\"{prop.Name}\":";
                classJson += GenerateProperty(prop);
            }
            classJson += "}";
            return currentJson + classJson;
        }
        public string GenerateJsonExample()
        {
            return GenerateJsonWorker(string.Empty, _thisTypeSerialization.GetTopLevelDescription());
        }
    }
}
