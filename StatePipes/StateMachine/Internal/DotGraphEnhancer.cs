using DotLang.CodeAnalysis.Syntax;

namespace StatePipes.StateMachine.Internal
{
    internal class DotGraphEnhancer
    {
        private const string LabelValue = "label";
        private List<string> _stateLabels = [];
        private readonly EventRegistrationManager _eventRegistrationManager;
        public DotGraphEnhancer(EventRegistrationManager eventRegistrationManager)
        {
            _eventRegistrationManager = eventRegistrationManager;
        }
        private string AppendPublishedEvents(string label, string value, string stateName)
        {
            if (LabelValue != label) return value;
            var events = _eventRegistrationManager.GetRegisteredEvents(stateName);
            for (int i = 0; i < events.Count; i++)
            {
                if (i == 0) value += $"\\nevents / {events[i]}";
                else value += $",\\n          {events[i]}";
            }
            return value;
        }
        private AttributeListSyntax? AddPublishedEvents(AttributeListSyntax? attributes, string stateName)
        {
            List<AttributeSyntax> newAttributes = [];
            if (attributes is null)
            {
                newAttributes.Add(new AttributeSyntax(LabelValue, AppendPublishedEvents(LabelValue, string.Empty, stateName)));
            }
            else
            {
                foreach (var attr in attributes.Attributes)
                {
                    if (!string.IsNullOrEmpty(attr.NameToken.StringValue))
                        newAttributes.Add(new AttributeSyntax(attr.NameToken.StringValue, AppendPublishedEvents(attr.NameToken.StringValue, attr.ValueToken.StringValue ?? string.Empty, stateName)));
                    else
                        newAttributes.Add(attr);
                }
            }
            return new AttributeListSyntax(newAttributes);
        }
        private AttributeListSyntax? AddCurrentState(AttributeListSyntax? attributes, string currentState)
        {
            List<AttributeSyntax> newAttributes = [];
            if (attributes is not null) AddPublishedEvents(attributes, currentState)?.Attributes.ToList().ForEach(a => newAttributes.Add(a));
            newAttributes.Add(new AttributeSyntax("style", "filled"));
            newAttributes.Add(new AttributeSyntax("fillcolor", "yellow"));
            return new AttributeListSyntax(newAttributes);
        }
        private StatementSyntax? HandleStatement(StatementSyntax? node, string currentState)
        {
            if (node is null) return null;
            if (node is NodeStatementSyntax nodeStatement)
            {
                if (string.IsNullOrEmpty(nodeStatement.Identifier.IdentifierToken.StringValue)) return node;
                if (nodeStatement.Identifier.IdentifierToken.StringValue == "init" || nodeStatement.Identifier.IdentifierToken.StringValue == "InitialState") return node;
                if (_stateLabels.Contains(nodeStatement.Identifier.IdentifierToken.StringValue)) return node;
                _stateLabels.Add(nodeStatement.Identifier.IdentifierToken.StringValue);
                return new NodeStatementSyntax(nodeStatement.Identifier, currentState == nodeStatement.Identifier.IdentifierToken.StringValue ? 
                    AddCurrentState(nodeStatement.Attributes, currentState) : 
                    AddPublishedEvents(nodeStatement.Attributes, nodeStatement.Identifier.IdentifierToken.StringValue));
            }
            else if (node is SubgraphStatementSyntax subgraphStatement)
                return HandleSubgraph(subgraphStatement, currentState);
            else if (node is NameValueStatementSyntax nameValueStatement && nameValueStatement.NameToken.StringValue == LabelValue)
                return new NameValueStatementSyntax(nameValueStatement.NameToken.StringValue, 
                    AppendPublishedEvents(nameValueStatement.NameToken.StringValue, 
                    nameValueStatement.ValueToken.StringValue ?? string.Empty, nameValueStatement.ValueToken.StringValue?.Split(@"\n")[0] ?? string.Empty));
            return node;
        }
        private SubgraphStatementSyntax HandleSubgraph(SubgraphStatementSyntax subgraph, string currentState)
        {
            List<StatementSyntax> newSubgraphStatements = [];
            foreach (var statement in subgraph.Statements)
            {
                var newStatement = HandleStatement(statement, currentState);
                if (newStatement != null) newSubgraphStatements.Add(newStatement);
            }
            return new SubgraphStatementSyntax(subgraph.IdentifierToken.StringValue, newSubgraphStatements);
        }
        private ToplevelGraphSyntax HandleGraph(ToplevelGraphSyntax graph, string currentState)
        {
            List<StatementSyntax> newGraphStatements = [];
            foreach (var statement in graph.Statements)
            {
                var newStatement = HandleStatement(statement, currentState);
                if (newStatement != null) newGraphStatements.Add(newStatement);
            }
            return new DigraphSyntax(graph.IdentifierToken.StringValue, false, newGraphStatements);
        }
        private SyntaxTree HandleSyntaxTree(SyntaxTree tree, string currentState)
        {
            List<ToplevelGraphSyntax> newGraphList = [];
            foreach (var graph in tree.Graphs)
            {
                var newGraph = HandleGraph(graph, currentState);
                if (newGraph != null) newGraphList.Add(newGraph);
            }
            return new SyntaxTree(newGraphList);
        }
        public string EnhanceDotGraph(string dotGraph, string currentState)
        {
            _stateLabels.Clear();
            var parser = new Parser(dotGraph);
            var syntaxTree = parser.Parse();
            var newSyntaxTree = HandleSyntaxTree(syntaxTree, currentState);
            return newSyntaxTree.ToString();
        }
    }
}
