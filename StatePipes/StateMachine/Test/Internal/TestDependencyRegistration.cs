using Autofac;
using StatePipes.Interfaces;

namespace StatePipes.StateMachine.Test.Internal
{
    internal class TestDependencyRegistration : IDummyDependencyRegistration
    {
        private readonly Dictionary<Type, List<object>> preRepo = [];
        public IDummyDependencyRegistration? DummyDependencyRegistration { get; set; }
        public void Register<T>(object obj)
        {
            if (obj == null) return;
            var t = typeof(T);
            if (preRepo.ContainsKey(t)) preRepo[t].Add(obj);
            else preRepo.Add(t, new List<object> { obj });
        }
        internal void Register(ContainerBuilder builder)
        {
            foreach (var tp in preRepo.Keys)
            {
                foreach (var o in preRepo[tp])
                {
                    _ = builder.RegisterInstance(o).As(tp);
                }
            }
        }
        public void RegisterDummyForType(ContainerBuilder builder, Type t)
        {
            if (!HasType(t))
            {
                DummyDependencyRegistration?.RegisterDummyForType(builder, t);
            }
        }
        internal bool HasType(Type type) => preRepo.ContainsKey(type);
    }
}
