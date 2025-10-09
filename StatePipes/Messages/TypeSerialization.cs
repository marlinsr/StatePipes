using Newtonsoft.Json;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Messages
{
    public class TypeSerialization
    {
        [JsonConstructor]
        public TypeSerialization(Dictionary<string, TypeDescription> typeRepo, string fullName)
        {
            TypeRepo = typeRepo;
            FullName = fullName;
        }
        public TypeSerialization() { }

        public Dictionary<string, TypeDescription> TypeRepo { get; set; } = [];
        public string FullName { get; set; } = string.Empty;
        public TypeDescription GetTopLevelDescription() => TypeRepo[FullName];
        public TypeDescription GetDescription(string fullName) => TypeRepo[fullName];
        public void AddTypeDescription(TypeDescription typeDescription)
        {
            if(TypeRepo.ContainsKey(typeDescription.FullName))
            {
                TypeRepo[typeDescription.FullName] = typeDescription;
            }
            else
            {
                TypeRepo.Add(typeDescription.FullName, typeDescription);
            }
        }
        public bool HasTypeDescription(string fullName) => TypeRepo.ContainsKey(fullName);
        public TypeSerialization? CreateSubTypeSerialization(string fullName)
        {
            if (!TypeRepo.ContainsKey(fullName))
            {
                Log?.LogError($" {fullName} does not exist");
                return null;
            }
            TypeSerialization info = new() { FullName = fullName, TypeRepo = TypeRepo};
            return info;
        }
    }
}
