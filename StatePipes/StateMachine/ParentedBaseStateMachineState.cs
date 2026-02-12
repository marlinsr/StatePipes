using StatePipes.Interfaces;
namespace StatePipes.StateMachine
{
    public class ParentedBaseStateMachineState<StateMachineType, ParentStateType> : BaseStateMachineState<StateMachineType> where StateMachineType : IStateMachine where ParentStateType : BaseStateMachineState<StateMachineType>
    {
        public override void Configure(StateConfigurationWrapper stateConfig)
        {
            base.Configure(stateConfig);
            stateConfig.SubstateOf<ParentStateType>();
        }
    }
}
