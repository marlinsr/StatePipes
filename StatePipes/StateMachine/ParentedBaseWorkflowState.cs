using StatePipes.Interfaces;

namespace StatePipes.StateMachine
{
    public class ParentedBaseWorkflowState<TWorkflow, TParent> : BaseWorkflowState<TWorkflow>
        where TWorkflow : IWorkflow where TParent : BaseWorkflowState<TWorkflow>
    {
        public override void Configure(StateConfigurationWrapper stateConfig)
        {
            base.Configure(stateConfig);
            stateConfig.SubstateOf<TParent>();
        }
    }
}
