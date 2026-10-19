using Opc.Ua;
using StatePipes.OpcUaBridge;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>Calling OPC UA methods through the bridge, against the in-process PLC's Methods object.</summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class OpcUaBridgeExecTest
    {
        private static PlcServerFixture _plc = null!;

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext _) => _plc = await PlcServerFixture.StartAsync(48404);

        [ClassCleanup]
        public static async Task ClassCleanup() => await _plc.DisposeAsync();

        private static async Task<OpcUaClient> ConnectAsync()
        {
            OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));
            await client.ConnectAsync(CancellationToken.None);
            return client;
        }

        [TestMethod]
        public async Task Call_Add_ReturnsSum()
        {
            await using var client = await ConnectAsync();

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("Add"), [new Variant(2), new Variant(3)], CancellationToken.None);

            Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code, result.StatusCode.ToString());
            Assert.AreEqual(5, result.OutputArguments[0].AsBoxedObject(Variant.BoxingBehavior.Legacy));
        }

        [TestMethod]
        public async Task Call_Slow20Seconds_OutlivesOperationTimeout()
        {
            await using var client = await ConnectAsync();

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("Slow"), [new Variant(20_000u)], CancellationToken.None);

            Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code, result.StatusCode.ToString());
        }

        [TestMethod]
        public async Task Call_Reject150_InvalidArgumentWithOutOfRange()
        {
            await using var client = await ConnectAsync();

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("Reject"), [new Variant(150.0)], CancellationToken.None);

            Assert.AreEqual(StatusCodes.BadInvalidArgument, result.StatusCode.Code, result.StatusCode.ToString());
            Assert.AreEqual(StatusCodes.BadOutOfRange, result.InputArgumentResults[0].Code);
        }

        [TestMethod]
        public async Task Call_StartOnPump1_Good()
        {
            await using var client = await ConnectAsync();

            var result = await client.CallAsync(_plc.Pump1, _plc.Method("Start"), [], CancellationToken.None);

            Assert.AreEqual(StatusCodes.Good, result.StatusCode.Code, result.StatusCode.ToString());
        }

        [TestMethod]
        public async Task Call_AfterDisconnect_CallSessionClosed()
        {
            await using var client = await ConnectAsync();
            await client.CallAsync(_plc.MethodsObject, _plc.Method("NoArgs"), [], CancellationToken.None);
            await client.DisconnectAsync();

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("NoArgs"), [], CancellationToken.None);

            Assert.AreEqual(StatusCodes.BadNotConnected, result.StatusCode.Code);
        }

        [TestMethod]
        public async Task Call_DisconnectDuringSlowCall_Throws()
        {
            await using var client = await ConnectAsync();
            var call = client.CallAsync(_plc.MethodsObject, _plc.Method("Slow"), [new Variant(10_000u)], CancellationToken.None);
            await Task.Delay(500);

            await client.DisconnectAsync();

            var ended = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(call, ended, "The call outlived its session");
            await Assert.ThrowsAsync<ServiceResultException>(() => call);
        }

        [TestMethod]
        public async Task Call_NotConnected_BadNotConnected()
        {
            await using OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));

            var result = await client.CallAsync(_plc.MethodsObject, _plc.Method("Add"), [new Variant(2), new Variant(3)], CancellationToken.None);

            Assert.AreEqual(StatusCodes.BadNotConnected, result.StatusCode.Code);
        }
    }
}
