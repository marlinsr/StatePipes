using Autofac;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StatePipes.Common;
using StatePipes.Common.Internal;
using StatePipes.Comms;
using StatePipes.Interfaces;
using StatePipes.ProcessLevelServices;
using StatePipes.StateMachine.Test.Internal;
using StatePipes.Test.Workflow.Samples.Cart;
using StatePipes.Workflow.Internal;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowStoreConfigTest
    {
        [TestMethod]
        public void Defaults()
        {
            var cfg = new WorkflowStoreConfig("postgres-db", "statepipes", "statepipes", "statepipes.postgres-db.client.p12");
            Assert.AreEqual(5432, cfg.Port);
            Assert.AreEqual("CommonPassword.txt", cfg.PasswordFileName);
            Assert.AreEqual(10, cfg.LockedMaxRetries);
            Assert.AreEqual(TimeSpan.FromMilliseconds(50), cfg.LockedRetryDelay);
            Assert.AreEqual(TimeSpan.FromSeconds(1), cfg.TimerPollInterval);
        }

        [TestMethod]
        public void JsonRoundTrip()
        {
            var cfg = new WorkflowStoreConfig("h", "d", "u", "c.p12", 6543, "pw.txt", 3, TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(5));
            var back = JsonUtility.Clone(cfg);
            Assert.AreEqual((cfg.Host, cfg.Port, cfg.Database, cfg.Username, cfg.ClientCertFileName, cfg.PasswordFileName, cfg.LockedMaxRetries, cfg.LockedRetryDelay, cfg.TimerPollInterval),
                (back.Host, back.Port, back.Database, back.Username, back.ClientCertFileName, back.PasswordFileName, back.LockedMaxRetries, back.LockedRetryDelay, back.TimerPollInterval));
        }

        [TestMethod]
        public void ServiceConfiguration_WithoutWorkflowStore_DeserializesOldJson()
        {
            var sc = new ServiceConfiguration(new BusConfig("b", "p", "c", "pw"), "Asm", "Asm.Setup", [], [], new ServiceArgs(null));
            var json = JObject.FromObject(sc);
            json.Remove("WorkflowStore");
            var back = JsonConvert.DeserializeObject<ServiceConfiguration>(json.ToString())!;
            Assert.IsNull(back.WorkflowStore);
            Assert.AreEqual("Asm", back.AssemblyName);
        }
    }

    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class AllWorkflowContainerSetupTest
    {
        private static readonly WorkflowStoreConfig Config = new("h", "d", "u", "c.p12");

        [TestMethod]
        public void NoWorkflows_NeverTouchesStore()
        {
            var setup = new AllWorkflowContainerSetup(typeof(IWorkflow).Assembly, _ => throw new AssertFailedException("store created"), null, null);
            var builder = new ContainerBuilder();
            setup.Register(builder);
            using var container = builder.Build();
            setup.Build(container);
        }

        [TestMethod]
        public void WorkflowsWithoutConfig_Throws()
        {
            var setup = new AllWorkflowContainerSetup(typeof(CartWorkflow).Assembly, _ => new InMemoryWorkflowStore(TimeProvider.System), null, null,
                t => t == typeof(CartWorkflow));
            var ex = Assert.ThrowsExactly<WorkflowValidationException>(() => setup.Register(new ContainerBuilder()));
            StringAssert.Contains(ex.Message, "WorkflowStore");
        }

        [TestMethod]
        public void RegistersHandlerPerTrigger_AndRoutesToEngine()
        {
            var store = new InMemoryWorkflowStore(new FakeTimeProvider());
            var setup = new AllWorkflowContainerSetup(typeof(CartWorkflow).Assembly, _ => store, Config, null,
                t => t == typeof(CartWorkflow), startPoller: false);
            using var bus = new TestStatePipesService();
            using var timers = new InMemoryTimerService(bus, TimeProvider.System);
            var builder = new ContainerBuilder();
            builder.RegisterInstance(bus).As<IStatePipesService>();
            builder.RegisterInstance(timers).AsSelf().ExternallyOwned();
            setup.Register(builder);
            using var container = builder.Build();
            setup.Build(container);

            foreach (var trigger in new[] { typeof(AddItem), typeof(Checkout), typeof(PriceQuoted), typeof(CartAbandoned) })
                Assert.IsTrue(container.IsRegistered(typeof(IMessageHandler<>).MakeGenericType(trigger)), trigger.Name);
            container.Resolve<IMessageHandler<AddItem>>().HandleMessage(new AddItem { CorrelationId = "c1", Sku = "a" }, new BusConfig("", "", "", ""), false);
            Assert.AreEqual("c1", store.Instances.Single().CorrelationId);
        }
    }
}
