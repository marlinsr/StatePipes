namespace StatePipes.ServiceCreatorTool
{
    public class TypeSerialization
    {
        public Dictionary<string, TypeDescription> TypeRepo { get; set; } = [];
        public string QualifiedName { get; set; } = string.Empty;
        public TypeDescription GetTopLevelTypeDescription() => TypeRepo[QualifiedName];
        public TypeDescription GetTypeDescription(string qualifiedName) => TypeRepo[qualifiedName];
        public void AddTypeDescription(TypeDescription typeDescription)
        {
            if (TypeRepo.ContainsKey(typeDescription.QualifiedName))
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
            if (!TypeRepo.ContainsKey(qualifiedName)) return null;
            TypeSerialization info = new() { QualifiedName = qualifiedName, TypeRepo = TypeRepo };
            return info;
        }
    }
}
