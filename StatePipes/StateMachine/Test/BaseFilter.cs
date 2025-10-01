namespace StatePipes.StateMachine.Test
{
    public abstract class BaseFilter<A> : ICloneable
    {
        public abstract bool IsFiltered(Type commandType);
        public bool IsFiltered<T>() where T : class, A => IsFiltered(typeof(T));
        public abstract object Clone();
    }
}
