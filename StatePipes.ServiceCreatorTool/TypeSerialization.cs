namespace StatePipes.ServiceCreatorTool
{
    public class TypeSerialization
    {
        public Dictionary<string, TypeDescription> TypeRepo { get; set; } = [];
        public string FullName { get; set; } = string.Empty;
        public TypeDescription GetTopLevelTypeDescription() => TypeRepo[FullName];
        public TypeDescription GetTypeDescription(string fullName) => TypeRepo[fullName];
        public void AddTypeDescription(TypeDescription typeDescription)
        {
            if (TypeRepo.ContainsKey(typeDescription.FullName))
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
            if (!TypeRepo.ContainsKey(fullName)) return null;
            TypeSerialization info = new() { FullName = fullName, TypeRepo = TypeRepo };
            return info;
        }
    }
}
