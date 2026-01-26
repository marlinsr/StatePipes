using StatePipes.Common;
using StatePipes.Interfaces;
using StatePipes.Messages;
using System.Reflection;
using System.Reflection.Emit;
namespace StatePipes.SelfDescription
{
    internal class TypeSerializationToTypeConverter(TypeRepo typeRepo, TypeRepo nullableTypeRepo)
    {
        private readonly TypeRepo _typeRepo = typeRepo;
        private readonly TypeRepo _nullableTypeRepo = nullableTypeRepo;
        private Dictionary<string, TypeBuilder> _builderRepo = [];
        public Type? Convert(TypeSerialization typeSerialization)
        {
            _builderRepo.Clear();
            var topLevelTypeDescription = typeSerialization.GetTopLevelDescription();
            var alreadyCreatedType = _typeRepo.GetTypeFromRepo(topLevelTypeDescription);
            if (alreadyCreatedType != null) return alreadyCreatedType;
            LocalCreateType(topLevelTypeDescription, typeSerialization);
            foreach (var typeBuilderEntry in _builderRepo)
            {
                var type = typeBuilderEntry.Value.CreateType();
                _typeRepo.AddTypeToRepo(type, typeBuilderEntry.Key);
            }
            _builderRepo.Clear();
            return _typeRepo.GetTypeFromRepo(topLevelTypeDescription);
        }
        private Type? CreateNullable(TypeDescription t, TypeSerialization tsi)
        {
            var alreadyDefinedNullableType = _nullableTypeRepo.GetTypeFromRepo(t);
            if (alreadyDefinedNullableType != null) return alreadyDefinedNullableType;
            var underLyingType = LocalCreateType(t, tsi);
            if (underLyingType == null) return null;
            var genericType = typeof(Nullable<>).MakeGenericType(underLyingType);
            _nullableTypeRepo.AddTypeToRepo(genericType, t.FullName);
            return genericType;
        }
        private static object? GetPropertyValueForConstructorParam(string? constructorParameterName, object attribute)
        {
            if (string.IsNullOrEmpty(constructorParameterName)) return null;
            var prop = attribute.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(p  => p.Name.ToLower() == constructorParameterName.ToLower());
            if (prop == null) return null;
            return prop.GetValue(attribute);
        }
        private CustomAttributeBuilder? CreateCustomAttributeBuilder(AttributeDescription attr, TypeSerialization tsi)
        {
            //All attributes must be immutable with one constructor and Properties and be json Serializable
            var localType = LocalCreateType(tsi.GetDescription(attr.FullName), tsi);
            if(localType == null) return null;
            dynamic ? attribute = JsonUtility.GetObjectFromJson(attr.Value, localType);
            if (attribute is null) return null;
            Type type = attribute.GetType();
            var constructor = type.GetConstructors().FirstOrDefault();
            if (constructor is null) return null;
            var contructorParams = constructor.GetParameters();
            if (contructorParams == null || contructorParams.Length == 0) return new CustomAttributeBuilder(constructor, Type.EmptyTypes);
            List<object?> constructorArgs = [];
            contructorParams.ToList().ForEach(cp => constructorArgs.Add(GetPropertyValueForConstructorParam(cp.Name, attribute)));
            return new CustomAttributeBuilder(constructor, constructorArgs.ToArray());
        }
        private void SetAttributes(TypeBuilder tb, List<AttributeDescription> attrs, TypeSerialization tsi)
        {
            attrs.ForEach(a =>
            {
                var cab = CreateCustomAttributeBuilder(a, tsi);
                if(cab is not null) tb.SetCustomAttribute(cab);
            });
        }
        private void SetAttributes(PropertyBuilder tb, List<AttributeDescription> attrs, TypeSerialization tsi)
        {
            attrs.ForEach(a =>
            {
                var cab = CreateCustomAttributeBuilder(a, tsi);
                if (cab is not null) tb.SetCustomAttribute(cab);
            });
        }
        private Type? LocalCreateType(TypeDescription t, TypeSerialization tsi, bool isNullable = false)
        {
            if (isNullable) return CreateNullable(t, tsi);
            var alreadyDefinedType = GetTypeFromRepo(t);
            if (alreadyDefinedType != null) return alreadyDefinedType;
            if (t.IsArray()) return CreateArray(t, tsi);
            if (t.IsGeneric() && _typeRepo.IsSupportedGeneric(t.GenericNames!)) return CreateGeneric(t, tsi);
            if (t.IsEnum()) return CreateEnum(t);
            TypeBuilder tb = _typeRepo.GetTypeBuilder(t);
            AddToBuilderRepo(tb, t);
            t.Properties.ForEach(p => CreateProperty(p, tb, tsi));
            if (t.IsCommand) tb.AddInterfaceImplementation(typeof(ICommand));
            if (t.IsEvent) tb.AddInterfaceImplementation(typeof(IEvent));
            if (t.IsAttribute) tb.SetParent(typeof(Attribute));
            SetAttributes(tb, t.Attributes, tsi);
            return tb;
        }
        private Type? GetTypeFromRepo(TypeNames t)
        {
            if (_builderRepo.ContainsKey(t.FullName))
            {
                return _builderRepo[t.FullName];
            }
            return _typeRepo.GetTypeFromRepo(t);
        }
        private void AddToBuilderRepo(TypeBuilder tb, TypeDescription t)
        {
            if (_builderRepo.ContainsKey(t.FullName))
            {
                _builderRepo[t.FullName] = tb;
            }
            else
            {
                _builderRepo.Add(t.FullName, tb);
            }
        }
        private Type[] GetGenericArgumentsTypeArray(TypeDescription t, TypeSerialization tsi)
        {
            List<Type> ret = [];
            for (int i = 0; i < t.GenericArgumentsNames.Length; i++)
            {
                var localType = LocalCreateType(tsi.GetDescription(t.GenericArgumentsNames[i].FullName), tsi);
                if (localType != null)
                {
                    ret.Add(localType);
                    _typeRepo.AddTypeToRepo(ret[i], t.GenericArgumentsNames[i].FullName);
                }
            }
            return ret.ToArray();
        }
        private Type? CreateArray(TypeDescription t, TypeSerialization tsi)
        {
            var arrayElementType = LocalCreateType(tsi.GetDescription(t.ArrayFullName), tsi);
            if (arrayElementType == null) return null;
            var arrayType = arrayElementType.MakeArrayType(t.ArrayRank);
            _typeRepo.AddTypeToRepo(arrayType, t.FullName);
            return arrayType;
        }
        private Type? CreateGeneric(TypeDescription t, TypeSerialization tsi)
        {
            var genericType = GetTypeFromRepo(t.GenericNames!)?.MakeGenericType(GetGenericArgumentsTypeArray(t, tsi));
            if (genericType == null) return null;
            _typeRepo.AddTypeToRepo(genericType, t.FullName);
            return genericType;
        }
        private Type CreateEnum(TypeDescription t)
        {
            EnumBuilder eb = _typeRepo.GetEnumBuilder(t);
            foreach (var enumVal in t.EnumValues)
            {
                eb.DefineLiteral(enumVal.Name, enumVal.Value);
            }
            var eType = eb.CreateType();
            _typeRepo.AddTypeToRepo(eType, t.FullName);
            return eType;
        }
        private void CreateProperty(ParameterDescription p, TypeBuilder tb, TypeSerialization tsi)
        {
            var parameterTypeDescription = tsi.GetDescription(p.FullName);
            var pType = LocalCreateType(parameterTypeDescription, tsi, p.IsNullable);
            if(pType == null) return;
            FieldBuilder fieldBldr = tb.DefineField($"_{p.Name}",
                                      pType,
                                      FieldAttributes.Private);
            PropertyBuilder propBldr = tb.DefineProperty(p.Name,
                                                             PropertyAttributes.HasDefault,
                                                             pType,
                                                             null);
            MethodAttributes getSetAttr =
                MethodAttributes.Public | MethodAttributes.SpecialName |
                    MethodAttributes.HideBySig;
            MethodBuilder getPropMthdBldr =
                tb.DefineMethod($"get_{p.Name}",
                                           getSetAttr,
                                           pType,
                                           Type.EmptyTypes);
            ILGenerator custNameGetIL = getPropMthdBldr.GetILGenerator();
            custNameGetIL.Emit(OpCodes.Ldarg_0);
            custNameGetIL.Emit(OpCodes.Ldfld, fieldBldr);
            custNameGetIL.Emit(OpCodes.Ret);
            MethodBuilder setPropMthdBldr =
                tb.DefineMethod($"set_{p.Name}",
                                           getSetAttr,
                                           null,
                                           [pType]);

            ILGenerator custNameSetIL = setPropMthdBldr.GetILGenerator();
            custNameSetIL.Emit(OpCodes.Ldarg_0);
            custNameSetIL.Emit(OpCodes.Ldarg_1);
            custNameSetIL.Emit(OpCodes.Stfld, fieldBldr);
            custNameSetIL.Emit(OpCodes.Ret);
            propBldr.SetGetMethod(getPropMthdBldr);
            propBldr.SetSetMethod(setPropMthdBldr);
            SetAttributes(propBldr, p.Attributes, tsi);
        }
    }
}
