using Autofac;
namespace StatePipes.Interfaces
{
    public interface IDummyDependencyRegistration
    {
        void RegisterDummyForType(ContainerBuilder builder, Type t);
    }
}
