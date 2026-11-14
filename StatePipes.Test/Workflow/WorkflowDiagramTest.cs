using Autofac;
using StatePipes.Common.Internal;
using StatePipes.Interfaces;
using StatePipes.StateMachine.Internal;
using StatePipes.StateMachine.Test.Internal;
using StatePipes.Test.StateMachine.Samples;
using StatePipes.Test.Workflow.Samples.Cart;
using StatePipes.Workflow.Internal;
using System.Text.RegularExpressions;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    [OSCondition(OperatingSystems.Windows)]
    public partial class WorkflowDiagramTest
    {
        private static string CartGraph()
        {
            using var bus = new TestStatePipesService();
            using var timers = new InMemoryTimerService(bus, TimeProvider.System);
            return WorkflowMachineFactory.CreateForDiagram(WorkflowMetadata.Build(typeof(CartWorkflow)), bus, timers).Machine.GetDotGraph();
        }

        [TestMethod]
        public void CartDiagram_ContainsAllStatesAndTransitions()
        {
            var dot = CartGraph();
            foreach (var expected in new[] { nameof(CartOpenState), nameof(PricingState), nameof(CheckedOutState), nameof(AbandonedState), nameof(Checkout), nameof(PriceQuoted) })
                Assert.Contains(expected, dot);
        }

        [TestMethod]
        public void CartDiagram_FinalStatesDoubleBordered()
        {
            var dot = CartGraph();
            Assert.IsTrue(MyRegexCheckedOutState().IsMatch(dot), dot);
            Assert.IsTrue(MyRegexAbandonedState().IsMatch(dot), dot);
            Assert.IsFalse(MyRegexCartOpenState().IsMatch(dot), dot);
        }

        [TestMethod]
        public void CartDiagram_ListsPublishedEvents() => Assert.Contains(nameof(CartCheckedOut), CartGraph());

        [TestMethod]
        public void DummySetup_RegistersWorkflowDiagramMachines()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new TestStatePipesService()).As<IStatePipesService>();
            var dummy = new AllStateMachineDummyContainerSetup(typeof(CartWorkflow).Assembly, new NoDummies(), t => t == typeof(CartWorkflow));
            dummy.Register(builder);
            using var container = builder.Build();
            dummy.Build(container);
            var machines = container.Resolve<StateMachineManager>().GetAllStateMachines();
            Assert.Contains(m => m.StateMachineName == typeof(CartWorkflow).FullName, machines);
        }

        [TestMethod]
        public void RuntimeStateMachineManager_HasNoWorkflows()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new TestStatePipesService()).As<IStatePipesService>();
            var setup = new AllStateMachineContainerSetup(typeof(CartWorkflow).Assembly, false);
            setup.Register(builder);
            using var container = builder.Build();
            setup.Build(container);
            var names = container.Resolve<StateMachineManager>().GetAllStateMachines().Select(m => m.StateMachineName).ToList();
            Assert.Contains(typeof(PricingMachine).FullName, names);
            Assert.DoesNotContain(typeof(CartWorkflow).FullName, names);
        }

        [GeneratedRegex(@"""?CartOpenState""?\s*\[[^\n]*peripheries")]
        private static partial Regex MyRegexCartOpenState();
        [GeneratedRegex(@"AbandonedState[^\n]*peripheries\s*=\s*""?2")]
        private static partial Regex MyRegexAbandonedState();
        [GeneratedRegex(@"CheckedOutState[^\n]*peripheries\s*=\s*""?2")]
        private static partial Regex MyRegexCheckedOutState();
    }
}
