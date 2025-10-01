namespace StatePipes.Explorer.Components.Pages
{
    public class DictionaryEntryClass
    {
        public object Key { get; }
        public object Val { get; }

        public DictionaryEntryClass(object key, object val)
        {
            Key = key;
            Val = val;
        }
    }
}
