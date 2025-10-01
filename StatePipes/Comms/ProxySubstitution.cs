namespace StatePipes.Comms
{
    public class ProxySubstitution
    {
        public string ChildName { get; }
        public string ParentName { get; }
        public ProxySubstitution(string childName, string parentName)
        {
            ChildName = childName;
            ParentName = parentName;
        }
    }
}
