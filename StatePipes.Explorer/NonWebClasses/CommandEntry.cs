namespace StatePipes.Explorer.NonWebClasses
{
    public class CommandEntry
    {
        public string FullName { get; }
        public string Json { get; set; }
        public string OriginalJson { get; }
        public CommandEntry(string fullName, string json)
        {
            FullName = fullName;
            Json = json;
            OriginalJson = json;
        }
        public void ResetJson()
        {
            Json = OriginalJson;
        }
    }
}
