namespace StatePipes.Explorer.NonWebClasses
{
    public class CommandEntry(string fullName, string json)
    {
        public string FullName { get; } = fullName;
        public string Json { get; set; } = json;
        public string OriginalJson { get; } = json;

        public void ResetJson()
        {
            Json = OriginalJson;
        }
    }
}
