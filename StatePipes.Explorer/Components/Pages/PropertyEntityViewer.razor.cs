using Newtonsoft.Json;
using StatePipes.ExplorerTypes;
using System.Text;
namespace StatePipes.Explorer.Components.Pages
{
    public partial class PropertyEntityViewer
    {
        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            return Editors.FirstOrDefault()?.GetJson(jsonStringBuilder, getName) ?? false;
        }

        private static List<List<T>> GetListForDimension<T>(int upperBound, List<T> rawList)
        {
            var ret = new List<List<T>>();
            int count = 0;
            List<T> elementList = [];
            foreach (var element in rawList)
            {
                elementList.Add(element);
                if (count == upperBound)
                {
                    count = 0;
                    ret.Add(elementList);
                    elementList = [];
                }
                else
                {
                    count++;
                }
            }
            return ret;
        }

        private static dynamic GetStrongTypeIEnumerableForArray(Array arr, Type elementType)
        {
            var castMethod = typeof(Enumerable).GetMethods()
                .Where(i => i.Name.Equals("Cast", StringComparison.InvariantCulture))
                .Where(i => i.IsGenericMethod)
                .Single();
            var castMethodOfThisType = castMethod.MakeGenericMethod([elementType]);
            dynamic enumerable = castMethodOfThisType.Invoke(null, new[] { arr })!;
            var toListMethod = typeof(Enumerable).GetMethods()
                .Where(i => i.Name.Equals("ToList", StringComparison.InvariantCulture))
                .Where(i => i.IsGenericMethod)
                .Single();
            var toListMethodOfThisType = toListMethod.MakeGenericMethod([elementType]);
            return toListMethodOfThisType.Invoke(null, new[] { enumerable })!;
        }
        private static dynamic? RectangularToJagged(object obj)
        {
            var arr = obj as Array;
            if (arr == null) return null;
            dynamic rawList = GetStrongTypeIEnumerableForArray(arr, obj.GetType()!.GetElementType()!);
            for (int i = arr.Rank - 1; i > 0; i--)
            {
                rawList = GetListForDimension(arr.GetUpperBound(i), rawList);
            }
            return rawList;
        }
        private static PropertyValueClass? HandleGeneric(Guid instanceGuid, string commandTypeFullName, string? name, Type propertyType, object? obj, bool isFromEvent, bool isNullable)
        {
            if (propertyType.IsGenericType && !propertyType.IsGenericTypeDefinition)
            {
                if (propertyType.FullName == typeof(IReadOnlyList<byte>).FullName) 
                    return new PropertyValueClass(instanceGuid, commandTypeFullName, name, ((IReadOnlyList<byte>?)obj)?.ToArray(), PropertyValueClass.PropertyValueType.ByteArray, propertyType, isNullable, isFromEvent);
                var genericTypeDef = propertyType.GetGenericTypeDefinition();
                if ((typeof(List<>).FullName == genericTypeDef.FullName || typeof(IReadOnlyList<>).FullName == genericTypeDef.FullName
                        || typeof(IEnumerable<>).FullName == genericTypeDef.FullName) 
                        && (propertyType.UnderlyingSystemType.GenericTypeArguments.Length == 1
                        && propertyType.UnderlyingSystemType.GenericTypeArguments[0].IsPrimitive)) 
                    return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.PrimitiveArray, propertyType, isNullable, isFromEvent);
                if (typeof(List<>).FullName == genericTypeDef.FullName || typeof(IReadOnlyList<>).FullName == genericTypeDef.FullName
                    || typeof(IEnumerable<>).FullName == genericTypeDef.FullName || typeof(HashSet<>).FullName == genericTypeDef.FullName) 
                    return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.Array, propertyType, isNullable, isFromEvent);
                if (typeof(Dictionary<,>).FullName == genericTypeDef.FullName
                    || typeof(IReadOnlyDictionary<,>).FullName == genericTypeDef.FullName) 
                    return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.Dictionary, propertyType, isNullable, isFromEvent);
            }
            return null;
        }
        private static PropertyValueClass? HandlePrimtiveArray(Guid instanceGuid, string commandTypeFullName, string? name, Type propertyType, object? obj, bool isFromEvent, bool isNullable)
        {
            if (propertyType.FullName == typeof(sbyte[]).FullName
               || propertyType.FullName == typeof(short[]).FullName
               || propertyType.FullName == typeof(int[]).FullName
               || propertyType.FullName == typeof(long[]).FullName
               || propertyType.FullName == typeof(float[]).FullName
               || propertyType.FullName == typeof(double[]).FullName
               || propertyType.FullName == typeof(ushort[]).FullName
               || propertyType.FullName == typeof(uint[]).FullName
               || propertyType.FullName == typeof(ulong[]).FullName) 
                return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.PrimitiveArray, propertyType, isNullable, isFromEvent);
            return null;
        }
        public static PropertyValueClass? GetPropertyValueClass(Guid instanceGuid, string commandTypeFullName, string? name, Type propertyType, object? obj, bool isFromEvent = false)
        {
            if (propertyType.GetCustomAttributes(typeof(JsonIgnoreAttribute), false).Any()) return null;
            var isNullable = false;
            var underlyingPropertyType = Nullable.GetUnderlyingType(propertyType);
            if (underlyingPropertyType != null)
            {
                propertyType = underlyingPropertyType;
                isNullable = true;
            }
            var propertyValueClassReturn = HandleGeneric(instanceGuid, commandTypeFullName, name, propertyType, obj, isFromEvent, isNullable);
            if (propertyValueClassReturn != null) return propertyValueClassReturn;
            if (propertyType.FullName == typeof(byte[]).FullName) 
                return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.ByteArray, propertyType, isNullable, isFromEvent);
            propertyValueClassReturn = HandlePrimtiveArray(instanceGuid, commandTypeFullName, name, propertyType, obj, isFromEvent, isNullable);
            if (propertyValueClassReturn != null) return propertyValueClassReturn;
            if (propertyType.IsArray)
            {
                if (obj == null) return null;
                obj = RectangularToJagged(obj);
                if (obj == null) return null;
                return GetPropertyValueClass(instanceGuid, commandTypeFullName, name, obj.GetType(), obj, isFromEvent);
            }
            if (propertyType.FullName == typeof(DictionaryEntryClass).FullName) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.DictionaryEntry, propertyType, isNullable, isFromEvent);
            if (propertyType.FullName == typeof(Decimal).FullName) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj?.ToString()?.ToLower(), PropertyValueClass.PropertyValueType.Decimal, propertyType, isNullable, isFromEvent);
            if (propertyType.FullName == typeof(bool).FullName) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj?.ToString()?.ToLower(), PropertyValueClass.PropertyValueType.Bool, propertyType, isNullable, isFromEvent);
            if (propertyType.FullName == typeof(Guid).FullName) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj?.ToString(), PropertyValueClass.PropertyValueType.Guid, propertyType, isNullable, isFromEvent);
            if (isFromEvent && propertyType.FullName == typeof(SPEImage).FullName) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.Image, propertyType, isNullable, isFromEvent);
            if (isFromEvent && propertyType.FullName == typeof(SPELineChart).FullName) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.LineChart, propertyType, isNullable, isFromEvent);
            if (propertyType.FullName == typeof(DateTime).FullName) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.DateTime, propertyType, isNullable, isFromEvent);
            if (propertyType.FullName == typeof(DateTimeOffset).FullName) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.DateTimeOffset, propertyType, isNullable, isFromEvent);
            if (propertyType.FullName == typeof(string).FullName) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.String, propertyType, isNullable, isFromEvent);
            if (propertyType.IsPrimitive) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.Primitive, propertyType, isNullable, isFromEvent);
            if (propertyType.IsEnum)
            {
                var enumNames = Enum.GetNames(propertyType);
                var enumVal = obj?.ToString();
                if (string.IsNullOrEmpty(enumVal)) enumVal = enumNames[0];
                return new PropertyValueClass(instanceGuid, commandTypeFullName, name, enumVal, enumNames?.ToList() ?? [], isNullable, isFromEvent);
            }
            if (propertyType.IsClass) return new PropertyValueClass(instanceGuid, commandTypeFullName, name, obj, PropertyValueClass.PropertyValueType.Class, propertyType, isNullable, isFromEvent);
            return null;
        }
    }
}
