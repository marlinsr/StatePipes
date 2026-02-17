namespace StatePipes.Comms
{
    public class ProxySubstitution(string childName, string parentName)
    {
        public string ChildName { get; } = childName;
        public string ParentName { get; } = parentName;
    }
}
