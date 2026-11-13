using Autofac;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.Messages;

namespace StatePipes.Workflow.Internal
{
    /// <summary>
    /// Answers <see cref="GetWorkflowDiagramsCommand"/> with every running workflow instance for the CorrelationId.
    /// Registered in every service; a service without workflows has no registry and replies with an empty list.
    /// </summary>
    internal class GetWorkflowDiagramsCommandHandler(IStatePipesService bus, IComponentContext context) : IMessageHandler<GetWorkflowDiagramsCommand>
    {
        public void HandleMessage(GetWorkflowDiagramsCommand command, BusConfig? responseInfo, bool isResponse)
        {
            if (responseInfo == null) return;
            var registry = context.ResolveOptional<WorkflowEngineRegistry>();
            IReadOnlyList<WorkflowInstanceDiagram> workflows = registry == null || string.IsNullOrWhiteSpace(command.CorrelationId)
                ? []
                : registry.Interrogate(command.CorrelationId);
            bus.SendResponse(new WorkflowDiagramsEvent(command.CorrelationId, workflows), responseInfo);
        }
    }
}
