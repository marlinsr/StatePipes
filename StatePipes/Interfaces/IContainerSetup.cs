using Autofac;
namespace StatePipes.Interfaces
{
    //This is implemented by a class in your library that implements the statemachine and registers all your entities [refer to Domain Driven Design].
    //It will be called during startup to register and build the Autofac container.
    //It must have a public parameterless constructor.
    public interface IContainerSetup
    {
        void Register(ContainerBuilder containerBuilder);
        void Build(IContainer container);
    }
}
