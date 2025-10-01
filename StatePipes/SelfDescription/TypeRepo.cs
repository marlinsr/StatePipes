using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using Autofac.Util;
using StatePipes.Messages;
namespace StatePipes.SelfDescription
{
    internal class TypeRepo
    {
        private readonly Dictionary<string, ModuleBuilder> _moduleBuilderDictionary = [];
        private readonly Dictionary<string, AssemblyBuilder> _assemblyBuilderDictionary = [];
        private readonly Dictionary<string, Type> _typeRepo = [];
        private readonly List<Type> _supportedGenericsList = [
            typeof(List<>),
            typeof(Dictionary<,>),
            typeof(IReadOnlyDictionary<,>),
            typeof(HashSet<>),
            typeof(IReadOnlyList<>),
            typeof(IEnumerable<>)];
        private readonly List<Type> _baseTypeList = [
            typeof(string),
            typeof(bool),
            typeof(byte),
            typeof(sbyte),
            typeof(char),
            typeof(short),
            typeof(ushort),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(float),
            typeof(double),
            typeof(Guid),
            typeof(XElement),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(TimeSpan),
            typeof(decimal),
            typeof(byte[]),
            typeof(IReadOnlyList<byte>),
            typeof(List<byte>)];
        private readonly IEnumerable<Type> _statePipeTypes;
        private readonly IEnumerable<Type>? _callingAssemblyTypes;
        private readonly string? _statePipesAssemblyName;
        private readonly string? _callingAssemblyName;
        public TypeRepo(Assembly? callingAssembly)
        {
            var statePipesAssembly = typeof(TypeRepo).Assembly;
            _statePipeTypes = statePipesAssembly.GetLoadableTypes();
            _statePipesAssemblyName = statePipesAssembly.GetName().FullName;
            _callingAssemblyTypes = null;
            _callingAssemblyName = null;
            if (callingAssembly != null)
            {
                _callingAssemblyTypes = callingAssembly.GetLoadableTypes();
                _callingAssemblyName = callingAssembly.GetName().FullName;
            }
        }
        public void AddTypeToRepo(Type t, string typeAssemblyQualifiedName)
        {
            var tAssemblyName = t.Assembly.GetName().Name;
            if (tAssemblyName == _statePipesAssemblyName || tAssemblyName == _callingAssemblyName) return; 
            if (_baseTypeList.Contains(t)) return;
            if(_supportedGenericsList.Contains(t)) return;
            lock (_typeRepo)
            {
                if (_typeRepo.ContainsKey(typeAssemblyQualifiedName))
                {
                    return;
                }
                else
                {
                    _typeRepo.Add(typeAssemblyQualifiedName, t);
                }
            }
        }
        public Type? GetTypeFromRepo(TypeNames t)
        {
            
            if (t.AssemblyName == _statePipesAssemblyName) return _statePipeTypes.FirstOrDefault(l => l.FullName == t.FullName);
            if (t.AssemblyName == _callingAssemblyName) return _callingAssemblyTypes?.FirstOrDefault(l => l.FullName == t.FullName);
            foreach (var type in _baseTypeList)
            {
                if (type.FullName == t.FullName) return type;
            }
            foreach (var type in _supportedGenericsList)
            {
                if (type.FullName == t.FullName) return type;
            }
            lock (_typeRepo)
            {
                if (_typeRepo.ContainsKey(t.QualifiedName))
                {
                    return _typeRepo[t.QualifiedName];
                }
            }
            return null;
        }
        public EnumBuilder GetEnumBuilder(TypeDescription t)
        {
            lock (_moduleBuilderDictionary)
            {
                ModuleBuilder mb;
                var aName = new AssemblyName(t.AssemblyName);
                if (_assemblyBuilderDictionary.ContainsKey(aName.Name!))
                {
                    AssemblyBuilder ab = _assemblyBuilderDictionary[aName.Name!];
                    mb = _moduleBuilderDictionary[aName.Name!];
                }
                else
                {
                    AssemblyBuilder ab = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run);
                    _assemblyBuilderDictionary.Add(aName.Name!, ab);
                    mb = ab.DefineDynamicModule(aName.Name!);
                    _moduleBuilderDictionary.Add(aName.Name!, mb);
                }
                return mb.DefineEnum(t.FullName, TypeAttributes.Public, typeof(int));
            }
        }
        public TypeBuilder GetTypeBuilder(TypeDescription t)
        {
            lock (_moduleBuilderDictionary)
            {
                ModuleBuilder mb;
                var aName = new AssemblyName(t.AssemblyName);
                if (_assemblyBuilderDictionary.ContainsKey(aName.Name!))
                {
                    AssemblyBuilder ab = _assemblyBuilderDictionary[aName.Name!];
                    mb = _moduleBuilderDictionary[aName.Name!];
                }
                else
                {
                    AssemblyBuilder ab = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run);
                    _assemblyBuilderDictionary.Add(aName.Name!, ab);
                    mb = ab.DefineDynamicModule(aName.Name!);
                    _moduleBuilderDictionary.Add(aName.Name!, mb);
                }
                return mb.DefineType(t.FullName, TypeAttributes.Public);
            }
        }
        public bool IsSupportedGeneric(TypeNames t)
        {
            var statePipesAssembly = typeof(TypeRepo).Assembly;
            if (t.AssemblyName == statePipesAssembly.GetName().Name) return true;
            foreach (var type in _supportedGenericsList)
            {
                if (type.FullName == t.FullName) return true;
            }
            return false;
        }
    }
}
