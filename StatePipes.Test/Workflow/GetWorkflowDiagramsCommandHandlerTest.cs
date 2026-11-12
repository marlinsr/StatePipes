using Autofac;
using StatePipes.Interfaces;
using StatePipes.Messages;
using StatePipes.StateMachine.Internal;
using StatePipes.Test.Workflow.Samples.Cart;
using StatePipes.Workflow.Internal;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class GetWorkflowDiagramsCommandHandlerTest : WorkflowInterrogateTestBase
    {
        private static IContainer Ctx(WorkflowEngineRegistry? reg)
        {
            var b = new ContainerBuilder();
            if (reg != null) b.RegisterInstance(reg).AsSelf();
            return b.Build();
        }

        private WorkflowEngineRegistry RegistryWithCart(string correlationId)
        {
            var reg = new WorkflowEngineRegistry();
            var cart = Engine(typeof(CartWorkflow));
            reg.Add(typeof(CartWorkflow), cart);
            Send(cart, new AddItem { Sku = "a" }, correlationId);
            return reg;
        }

        [TestMethod]
        public void Handle_RepliesWithInstancesAndEchoedId()
        {
            using var c = Ctx(RegistryWithCart("c1"));
            new GetWorkflowDiagramsCommandHandler(_bus, c).HandleMessage(new GetWorkflowDiagramsCommand("c1"), ReplyTo, false);
            var ev = _bus.EventInvocations.OfType<WorkflowDiagramsEvent>().Single();
            Assert.AreEqual("c1", ev.CorrelationId);
            Assert.AreEqual(nameof(CartOpenState), ev.Workflows.Single().CurrentState);
        }

        [TestMethod]
        public void Handle_NoRegistry_RepliesEmpty()
        {
            using var c = Ctx(null);
            new GetWorkflowDiagramsCommandHandler(_bus, c).HandleMessage(new GetWorkflowDiagramsCommand("c1"), ReplyTo, false);
            Assert.AreEqual(0, _bus.EventInvocations.OfType<WorkflowDiagramsEvent>().Single().Workflows.Count);
        }

        [TestMethod]
        public void Handle_BlankId_RepliesEmpty()
        {
            using var c = Ctx(RegistryWithCart("c1"));
            new GetWorkflowDiagramsCommandHandler(_bus, c).HandleMessage(new GetWorkflowDiagramsCommand("  "), ReplyTo, false);
            Assert.AreEqual(0, _bus.EventInvocations.OfType<WorkflowDiagramsEvent>().Single().Workflows.Count);
        }

        [TestMethod]
        public void Handle_NoResponseInfo_SendsNothing()
        {
            using var c = Ctx(RegistryWithCart("c1"));
            new GetWorkflowDiagramsCommandHandler(_bus, c).HandleMessage(new GetWorkflowDiagramsCommand("c1"), null, false);
            Assert.AreEqual(0, _bus.EventInvocations.OfType<WorkflowDiagramsEvent>().Count());
        }

        [TestMethod]
        public void RegisterOnce_RegistersHandler()
        {
            var b = new ContainerBuilder();
            b.RegisterInstance(_bus).As<IStatePipesService>();
            BaseStateMachineAndFirstStateContainerSetup.RegisterOnce(b);
            using var c = b.Build();
            Assert.IsTrue(c.Resolve<IEnumerable<IMessageHandler<GetWorkflowDiagramsCommand>>>().Any());
        }
    }
}
