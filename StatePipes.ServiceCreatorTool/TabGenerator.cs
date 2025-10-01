namespace StatePipes.ServiceCreatorTool
{
    internal class TabGenerator
    {
        private int _numTabs;
        public string TabString { get; private set; } = string.Empty;
        public TabGenerator() { }
        private void SetTabString()
        {
            string tabString = string.Empty;
            for (int i = 0; i < _numTabs; i++)
            {
                tabString += "    ";
            }
            TabString = tabString;
        }
        public void Indent()
        {
            ++_numTabs;
            SetTabString();
        }
        public void Outdent()
        {
            if (_numTabs == 0) return;
            --_numTabs;
            SetTabString();
        }
        public void Reset() => TabString = string.Empty;

    }
}
