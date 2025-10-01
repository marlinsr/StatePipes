namespace StatePipes.StateMachine.Test.Internal
{
    internal class FilterConditions(int skip, int block)
    {
        private int skip = skip;
        private int block = block;
        public int Skip { get => skip; }
        public int Block { get => block; }
        public bool IsFiltered()
        {
            if (skip > 0)
            {
                skip--;
                return false;
            }
            if (block > 0)
            {
                block--;
                return true;
            }
            return false;
        }
    }
}
