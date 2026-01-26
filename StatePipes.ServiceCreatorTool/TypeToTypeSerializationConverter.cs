using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using StatePipes.ServiceCreatorTool;
using System.Reflection;

internal class TypeToTypeSerializationConverter
{
    private readonly TypeRepo _typeRepo;
    private readonly Type _commandType;
    private readonly Type _eventType;
    public TypeToTypeSerializationConverter(TypeRepo typeRepo,
        Type commandType,
        Type eventType)
    {
        _typeRepo = typeRepo;
        _commandType = commandType;
        _eventType = eventType;
    }
    public TypeSerialization Convert(Type t)
    {
        TypeSerialization typeSerialization = new();
        typeSerialization.FullName = t.FullName ?? string.Empty;
        typeSerialization.TypeRepo = [];
        CreateFromType(t, typeSerialization);
        return typeSerialization;
    }
    private void CreateFromType(Type t, TypeSerialization ts)
    {
        if (string.IsNullOrEmpty(t.FullName)) return;
        if (ts.HasTypeDescription(t.FullName)) return;
        var typeDescription = new TypeDescription();
        typeDescription.SetNames(t);
        typeDescription.IsEvent = _eventType.IsAssignableFrom(t);
        typeDescription.IsCommand = _commandType.IsAssignableFrom(t);
        typeDescription.IsAttribute = typeof(Attribute).IsAssignableFrom(t);
        typeDescription.Attributes = CreateClassAttributeList(t, ts);
        ts.AddTypeDescription(typeDescription);
        if (CreateArray(t, typeDescription, ts)) return;
        if (CreateGeneric(t, typeDescription, ts)) return;
        if (CreateEnum(t, typeDescription)) return;
        CreateProperties(t, typeDescription, ts);
    }
    private bool CreateArray(Type t, TypeDescription typeDescription, TypeSerialization ts)
    {
        if (t.IsArray)
        {
            var elementType = t.GetElementType();
            if (elementType != null)
            {
                typeDescription.ArrayFullName = elementType.FullName ?? string.Empty;
                CreateFromType(elementType, ts);
                typeDescription.ArrayRank = t.GetArrayRank();
            }
            return true;
        }
        return false;
    }
    private void AddGeneric(TypeNames genericTypeDescription, Type t, TypeDescription typeDescription, TypeSerialization ts)
    {
        typeDescription.GenericNames = genericTypeDescription;
        var genericArgumentsTypes = t.GetGenericArguments();
        if (genericArgumentsTypes != null && genericArgumentsTypes.Length > 0)
        {
            typeDescription.GenericArgumentsNames = new TypeNames[genericArgumentsTypes.Length];
            for (int i = 0; i < genericArgumentsTypes.Length; i++)
            {
                var genericArgument = new TypeNames();
                genericArgument.SetNames(genericArgumentsTypes[i]);
                typeDescription.GenericArgumentsNames[i] = genericArgument;
                CreateFromType(genericArgumentsTypes[i], ts);
            }
        }
    }
    private bool CreateGeneric(Type t, TypeDescription typeDescription, TypeSerialization ts)
    {
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
        {
            var genericTypeDef = t.GetGenericTypeDefinition();
            var genericTypeDescription = new TypeNames();
            genericTypeDescription.SetNames(genericTypeDef);
            if (_typeRepo.IsSupportedGeneric(genericTypeDescription))
            {
                AddGeneric(genericTypeDescription, t, typeDescription, ts);
                return true;
            }
        }
        return false;
    }
    private bool CreateEnum(Type t, TypeDescription typeDescription)
    {
        if (t.IsEnum)
        {
            Enum.GetNames(t).ToList().ForEach(enumName =>
            {
                try
                {
                    typeDescription.EnumValues.Add(new EnumValue(enumName, System.Convert.ToInt32(Enum.Parse(t, enumName))));
                }
                catch { }
            });
            return true;
        }
        return false;
    }
    private AttributeDescription GetAttributeTypeDescription(CustomAttributeData attributeData, Attribute customAttribute, TypeSerialization tsi)
    {
        CreateFromType(attributeData.AttributeType, tsi);
        var converter = new StringEnumConverter();
        AttributeDescription attributeDescription = new(attributeData.AttributeType.FullName ?? string.Empty, JsonConvert.SerializeObject(customAttribute, Formatting.None, converter));
        return attributeDescription;
    }
    private List<CustomAttributeData> FilterAttributes(IEnumerable<CustomAttributeData> attributes) => attributes.Where(attributeData => !string.IsNullOrEmpty(attributeData.AttributeType.FullName) && attributeData.AttributeType.FullName.StartsWith("StatePipes.")).ToList();
    private List<AttributeDescription> CreatePropertyAttributeList(PropertyInfo p, TypeSerialization ts)
    {
        List<AttributeDescription> attributeDescriptions = [];
        FilterAttributes(p.CustomAttributes).ForEach(t => {
            var customAttr = p.GetCustomAttribute(t.AttributeType);
            if (customAttr != null) attributeDescriptions.Add(GetAttributeTypeDescription(t, customAttr, ts));
        });
        return attributeDescriptions;
    }
    private List<AttributeDescription> CreateFieldAttributeList(FieldInfo f, TypeSerialization ts)
    {
        List<AttributeDescription> attributeDescriptions = [];
        FilterAttributes(f.CustomAttributes).ForEach(t => {
            var customAttr = f.GetCustomAttribute(t.AttributeType);
            if (customAttr != null) attributeDescriptions.Add(GetAttributeTypeDescription(t, customAttr, ts));
        });
        return attributeDescriptions;
    }
    private List<AttributeDescription> CreateClassAttributeList(Type decoratedObjectType, TypeSerialization ts)
    {
        List<AttributeDescription> attributeDescriptions = [];
        FilterAttributes(decoratedObjectType.CustomAttributes).ForEach(t => {
            var customAttr = decoratedObjectType.GetCustomAttribute(t.AttributeType);
            if (customAttr != null) attributeDescriptions.Add(GetAttributeTypeDescription(t, customAttr, ts));
        });
        return attributeDescriptions;
    }
    private void AddFieldAsProperty(FieldInfo f, TypeDescription typeDescription, TypeSerialization ts)
    {
        var attributeList = CreateFieldAttributeList(f, ts);
        if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var underLyingType = Nullable.GetUnderlyingType(f.FieldType)!;
            typeDescription.Properties.Add(new ParameterDescription(f.Name, underLyingType.FullName ?? string.Empty, true, attributeList));
            CreateFromType(underLyingType, ts);
        }
        else
        {
            typeDescription.Properties.Add(new ParameterDescription(f.Name, f.FieldType.FullName ?? string.Empty, false, attributeList));
            CreateFromType(f.FieldType, ts);
        }
    }
    private void HandleFields(Type t, TypeDescription typeDescription, TypeSerialization ts)
    {
        var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (fields != null && fields.Length > 0)
        {
            fields.ToList().ForEach(f =>
            {
                if (!f.GetCustomAttributes(typeof(JsonIgnoreAttribute), false).Any()) AddFieldAsProperty(f, typeDescription, ts);
            });
        }
    }
    private void AddProperty(Type t, PropertyInfo p, TypeDescription typeDescription, TypeSerialization ts)
    {
        if (p.CanWrite
            || p.GetSetMethod(false) != null
            || t.GetConstructors().Where(c => c.GetParameters()?.Where(prop => prop.ParameterType.FullName == p.PropertyType.FullName && p.Name.EndsWith(prop.Name!, StringComparison.InvariantCultureIgnoreCase)).Any() ?? false).Any())
        {
            var attributeList = CreatePropertyAttributeList(p, ts);
            if (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var underLyingType = Nullable.GetUnderlyingType(p.PropertyType)!;
                typeDescription.Properties.Add(new ParameterDescription(p.Name, underLyingType.FullName ?? string.Empty, true, attributeList));
                CreateFromType(underLyingType, ts);
            }
            else
            {
                typeDescription.Properties.Add(new ParameterDescription(p.Name, p.PropertyType.FullName ?? string.Empty, false, attributeList));
                CreateFromType(p.PropertyType, ts);
            }
        }
    }
    private void CreateProperties(Type t, TypeDescription typeDescription, TypeSerialization ts)
    {
        var properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (properties != null && properties.Length > 0)
        {
            properties.ToList().ForEach(p =>
            {
                if (p.GetCustomAttributes(typeof(JsonIgnoreAttribute), false).Length == 0
                    && (p.CanRead || p.GetGetMethod(false) != null)) AddProperty(t, p, typeDescription, ts);
            });
        }
        HandleFields(t, typeDescription, ts);
    }
}