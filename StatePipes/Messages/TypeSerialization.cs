using Newtonsoft.Json;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Messages
{
    public class TypeSerialization
    {
        [JsonConstructor]
        public TypeSerialization(Dictionary<string, TypeDescription> typeRepo, string qualifiedName)
        {
            TypeRepo = typeRepo;
            QualifiedName = qualifiedName;
        }
        public TypeSerialization() { }

        public Dictionary<string, TypeDescription> TypeRepo { get; set; } = [];
        public string QualifiedName { get; set; } = string.Empty;
        public TypeDescription GetTopLevelDescription() => TypeRepo[QualifiedName];
        public TypeDescription GetDescription(string qualifiedName) => TypeRepo[qualifiedName];
        public void AddTypeDescription(TypeDescription typeDescription)
        {
            if(TypeRepo.ContainsKey(typeDescription.QualifiedName))
            {
                TypeRepo[typeDescription.QualifiedName] = typeDescription;
            }
            else
            {
                TypeRepo.Add(typeDescription.QualifiedName, typeDescription);
            }
        }
        public bool HasTypeDescription(string qualifiedName) => TypeRepo.ContainsKey(qualifiedName);
        public TypeSerialization? CreateSubTypeSerialization(string qualifiedName)
        {
            if (!TypeRepo.ContainsKey(qualifiedName))
            {
                Log?.LogError($" {qualifiedName} does not exist");
                return null;
            }
            TypeSerialization info = new() { QualifiedName = qualifiedName, TypeRepo = TypeRepo};
            return info;
        }
    }
}
