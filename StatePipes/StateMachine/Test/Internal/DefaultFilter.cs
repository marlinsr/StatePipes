namespace StatePipes.StateMachine.Test.Internal
{
    internal class DefaultFilter<A> : BaseFilter<A>
    {
        private readonly Dictionary<string, FilterConditions> CommandsFiltered = [];
        public void Add<T>(int skip = 0, int block = int.MaxValue) where T : A
        {
            var name = typeof(T)?.FullName;
            if (!string.IsNullOrEmpty(name)) CommandsFiltered.Add(name, new FilterConditions(skip, block));
        }
        public bool Remove<T>() where T : A
        {
            var name = typeof(T)?.FullName;
            return !string.IsNullOrEmpty(name) && CommandsFiltered.ContainsKey(name) ? CommandsFiltered.Remove(name) : false;
        }
        public override bool IsFiltered(Type commandType)
        {
            var name = commandType?.FullName;
            if (string.IsNullOrEmpty(name)) return false;
            if (!CommandsFiltered.TryGetValue(name, out FilterConditions? value)) return false;
            return value.IsFiltered();
        }
        public void Clear() => CommandsFiltered.Clear();
        public override object Clone()
        {
            DefaultFilter<A> ret = new();
            CommandsFiltered.ToList().ForEach(rejectedCommand => ret.CommandsFiltered.Add(rejectedCommand.Key, new FilterConditions(rejectedCommand.Value.Skip, rejectedCommand.Value.Block)));
            return ret;
        }
    }
}
