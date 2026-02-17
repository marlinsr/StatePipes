namespace StatePipes.Explorer.Components.Pages
{
    public class DictionaryEntryClass(object key, object val)
    {
        public object Key { get; } = key;
        public object Val { get; } = val;
    }
}
