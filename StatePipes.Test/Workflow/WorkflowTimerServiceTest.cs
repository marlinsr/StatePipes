using Microsoft.Extensions.Time.Testing;
using Moq;
using Newtonsoft.Json;
using StatePipes.Comms;
using StatePipes.Comms.Internal;
using StatePipes.Interfaces;
using StatePipes.Test.Common;
using StatePipes.Test.Workflow.Samples.Cart;
using StatePipes.Workflow.Internal;

namespace StatePipes.Test.Workflow
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowTimerServiceTest
    {
        [TestMethod]
        public void Start_WritesRow_Stop_Deletes_IsEnabledReflectsUncommitted()
        {
            var store = new InMemoryWorkflowStore(new FakeTimeProvider());
            var settings = WorkflowJson.CreateSettings(typeof(CartWorkflow).Assembly);
            var uow = store.Begin();
            var timers = new WorkflowTimerService(uow, "Wf", "c1", settings);
            var oneShot = Guid.NewGuid();
            var periodic = Guid.NewGuid();

            timers.StartOneShot(oneShot, TimeSpan.FromSeconds(5), new CartAbandoned { CorrelationId = "c1" });
            Assert.IsTrue(timers.IsEnabled(oneShot));
            Assert.AreEqual(0, store.Timers.Count);
            timers.Stop(oneShot);
            Assert.IsFalse(timers.IsEnabled(oneShot));
            timers.StartPeriodic(periodic, TimeSpan.FromMinutes(1), new CartAbandoned { CorrelationId = "c1" });
            uow.Commit();

            var row = store.Timers.Single();
            Assert.AreEqual(periodic, row.TimerId);
            Assert.AreEqual(TimeSpan.FromMinutes(1), row.Period);
            Assert.AreEqual(typeof(CartAbandoned).FullName, row.MessageTypeFullName);
            Assert.AreEqual(("Wf", "c1"), (row.WorkflowType, row.CorrelationId));
            Assert.AreEqual("c1", JsonConvert.DeserializeObject<CartAbandoned>(row.MessageJson, settings)!.CorrelationId);
        }
    }

    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class WorkflowTimerPollerTest
    {
        private static readonly BusConfig ReplyTo = new("b", "p", "c", "pw");
        private readonly JsonSerializerSettings _settings = WorkflowJson.CreateSettings(typeof(CartWorkflow).Assembly);

        private WorkflowTimerPoller NewPoller(IWorkflowStore store, ICommandRequeuer requeuer, TimeSpan? interval = null) =>
            new(store, ["Wf"], requeuer, ReplyTo, _settings, interval ?? TimeSpan.FromSeconds(1), typeof(CartWorkflow).Assembly);

        private static InMemoryWorkflowStore StoreWithDueTimer(string messageType, string json, out Guid id)
        {
            var store = new InMemoryWorkflowStore(new FakeTimeProvider());
            id = Guid.NewGuid();
            using var uow = store.Begin();
            uow.UpsertTimer(new TimerRecord(id, "Wf", "cart-9", null, messageType, json), TimeSpan.Zero);
            uow.Commit();
            return store;
        }

        [TestMethod]
        public void PollOnce_RequeuesDueMessageFlaggedAsTimerDelivery()
        {
            var json = JsonConvert.SerializeObject(new CartAbandoned { CorrelationId = "cart-9" }, _settings);
            var store = StoreWithDueTimer(typeof(CartAbandoned).FullName!, json, out _);
            var requeuer = new Mock<ICommandRequeuer>();
            using var poller = NewPoller(store, requeuer.Object);

            Assert.AreEqual(1, poller.PollOnce());
            requeuer.Verify(r => r.Requeue(It.Is<object>(o => o is CartAbandoned && ((CartAbandoned)o).CorrelationId == "cart-9"), ReplyTo, 0, true), Times.Once);
            Assert.AreEqual(0, store.Timers.Count);
        }

        [TestMethod]
        public void UnresolvableType_LeftInPlace_LoggedOnce()
        {
            using var log = new RecordingLogger();
            var store = StoreWithDueTimer("No.Such.Message", "{}", out var id);
            using var poller = NewPoller(store, Mock.Of<ICommandRequeuer>());
            poller.PollOnce();
            poller.PollOnce();
            Assert.AreEqual(1, log.At(ILogger.LogLevel.Error).Count(m => m.Contains(id.ToString())));
            Assert.AreEqual(id, store.Timers.Single().TimerId);
        }

        [TestMethod]
        public void StoreThrows_LoopLogsAndKeepsPolling()
        {
            using var log = new RecordingLogger();
            int calls = 0;
            var store = new Mock<IWorkflowStore>();
            store.Setup(s => s.FireDueTimers(It.IsAny<IReadOnlyCollection<string>>(), 100, It.IsAny<Func<TimerRecord, bool>>()))
                .Returns(() => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("db down") : 0);
            using var poller = NewPoller(store.Object, Mock.Of<ICommandRequeuer>(), TimeSpan.FromMilliseconds(20));
            poller.Start();
            InMemoryTimerServiceTest.WaitFor(() => calls >= 2, 2000);
            Assert.IsTrue(calls >= 2, $"calls={calls}");
            Assert.IsTrue(log.Contains("db down"));
        }
    }

    public class DispatchProbe : ICommand { }

    internal sealed class DispatchProbeHandler : IMessageHandler<DispatchProbe>
    {
        public static readonly List<(int Retry, bool Timer)> Seen = [];
        public void HandleMessage(DispatchProbe message, BusConfig? responseInfo, bool isResponse)
        {
            lock (Seen) Seen.Add((CommandDispatchContext.RetryCount, CommandDispatchContext.IsTimerDelivery));
        }
    }

    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class StatePipesDispatchTest
    {
        [TestMethod]
        public void RetryCountAndTimerFlag_VisibleToHandlerDuringDispatch()
        {
            lock (DispatchProbeHandler.Seen) DispatchProbeHandler.Seen.Clear();
            var builder = new Autofac.ContainerBuilder();
            Autofac.RegistrationExtensions.RegisterType<DispatchProbeHandler>(builder).As<IMessageHandler<DispatchProbe>>();
            using var container = builder.Build();
            using var bus = new StatePipes.StateMachine.Test.Internal.TestStatePipesService();
            bus.SetContainer(container);

            bus.Requeue(new DispatchProbe(), new BusConfig("", "", "", ""), 3, true);
            bus.SendCommand(new DispatchProbe());
            InMemoryTimerServiceTest.WaitFor(() => { lock (DispatchProbeHandler.Seen) return DispatchProbeHandler.Seen.Count == 2; });

            lock (DispatchProbeHandler.Seen) CollectionAssert.AreEqual(new[] { (3, true), (0, false) }, DispatchProbeHandler.Seen);
        }
    }
}
