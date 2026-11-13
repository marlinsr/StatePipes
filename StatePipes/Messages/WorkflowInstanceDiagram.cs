namespace StatePipes.Messages
{
    /// <summary>One running workflow instance: its type, persisted state, version, highlighted DOT graph and document.</summary>
    public class WorkflowInstanceDiagram(string workflowType, string currentState, long version, string dotGraph, string documentJson, string? error)
    {
        public string WorkflowType { get; } = workflowType;
        public string CurrentState { get; } = currentState;
        public long Version { get; } = version;
        public string DotGraph { get; } = dotGraph;
        public string DocumentJson { get; } = documentJson;
        public string? Error { get; } = error;
    }
}
