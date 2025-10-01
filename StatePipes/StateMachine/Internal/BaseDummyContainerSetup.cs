using Autofac;
using Autofac.Util;
using StatePipes.Interfaces;
using System.Reflection;

namespace StatePipes.StateMachine.Internal
{
    internal class BaseDummyContainerSetup : IContainerSetup
    {
        private readonly IDummyDependencyRegistration _dummyRegisterator;
        private readonly Assembly _assembly;
        private readonly Type _stateMachineType;
        private readonly Type _stateClassType;

        public BaseDummyContainerSetup(Type stateMachineType, IDummyDependencyRegistration dummyRegisterator)
        {
            _stateMachineType = stateMachineType;
            _assembly = _stateMachineType.Assembly;
            _dummyRegisterator = dummyRegisterator;
            _stateClassType = BaseStateMachineAndFirstStateContainerSetup.GetStateClassType(_stateMachineType);
        }
        public void Build(IContainer container)
        {
        }

        public void Register(ContainerBuilder containerBuilder)
        {
            CreateDummys(containerBuilder);
        }

        #region Testing and Diagraming
        private List<Type> GetStateClassTypeList()
        {
            return _assembly.GetLoadableTypes().Where(t => _stateClassType.IsAssignableFrom(t)).ToList();
        }

        private List<Type>? GetConstructorParameterTypes(Type t)
        {
            var parameterLestConstructor = t.GetConstructor(Type.EmptyTypes);
            if (parameterLestConstructor != null) return null;
            var constructors = t.GetConstructors();
            var parameters = constructors.First().GetParameters();
            if (parameters == null) return null;
            return parameters.Select(p => p.ParameterType).ToList();
        }

        private void CreateDummys(ContainerBuilder builder)
        {
            if (_dummyRegisterator == null) throw new InvalidOperationException("dummyRegisterator == null!");
            var states = GetStateClassTypeList();
            if (states == null) return;
            states.ForEach(state =>
            {
                if (state != null)
                {
                    var stateParams = GetConstructorParameterTypes(state);
                    if (stateParams != null) stateParams.ForEach(param => _dummyRegisterator.RegisterDummyForType(builder, param));
                }
            });
            var stateMachineParams = GetConstructorParameterTypes(typeof(BaseStateMachine));
            if (stateMachineParams != null) stateMachineParams.ForEach(param => _dummyRegisterator.RegisterDummyForType(builder, param));
        }
        #endregion
    }
}
