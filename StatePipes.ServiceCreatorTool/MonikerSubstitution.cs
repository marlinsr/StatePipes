namespace StatePipes.ServiceCreatorTool
{
    internal class MonikerSubstitution
    {
        private readonly Dictionary<string, string> _replacementDictionary = new Dictionary<string, string>();
        public void AddMoniker(string moniker, string replacement) =>_replacementDictionary[moniker] = replacement;
        public string Replace(string contents)
        {
            string ret = contents;
            foreach (var item in _replacementDictionary)
            {
                ret = ret.Replace(item.Key, item.Value);
            }
            return ret;
        }
    }
}
