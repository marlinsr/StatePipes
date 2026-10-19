using Opc.Ua;
using StatePipes.OpcUaBridge;
using System.Text.RegularExpressions;

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

        private static BridgeSettings Settings(Regex? methodFilter = null, bool includeNamespaceZero = false)
        {
            var settings = _plc.CreateBridgeSettings(MonitorRegistryTest.Own);
            return new BridgeSettings
            {
                BusConfig = settings.BusConfig,
                OpcUaEndpoint = settings.OpcUaEndpoint,
                UseSecurity = settings.UseSecurity,
                AutoAcceptUntrustedCertificates = settings.AutoAcceptUntrustedCertificates,
                PkiPath = settings.PkiPath,
                ApplicationName = settings.ApplicationName,
                MethodFilter = methodFilter,
                IncludeNamespaceZero = includeNamespaceZero
            };
        }

        private static async Task<IReadOnlyList<OpcUaMethodItem>> DiscoverMethodsAsync(BridgeSettings? settings = null)
        {
            await using OpcUaClient client = new(settings ?? Settings());
            await client.ConnectAsync(CancellationToken.None);
            return (await client.DiscoverAsync(CancellationToken.None)).Methods;
        }

        private static bool IsInMethodsObject(OpcUaMethodItem method) => method.MethodId.IdentifierAsString?.StartsWith("Methods.") == true;

        [TestMethod]
        public async Task Discover_MethodsTable_ExecExactlyAsSpec()
        {
            var methods = await DiscoverMethodsAsync();

            CollectionAssert.AreEquivalent(
                new[] { "Add", "Echo", "EchoDensity", "NoArgs", "Slow", "Reject", "TextOut", "Start", "Start" }.Select(n => _plc.Method(n)).ToArray(),
                methods.Where(IsInMethodsObject).Select(m => m.MethodId).ToArray());
            Assert.IsTrue(methods.Any(m => m.MethodId.IdentifierAsString == "CalculatePressureVolumeMass"));
        }

        [TestMethod]
        public async Task Discover_Arguments_Typed()
        {
            var methods = (await DiscoverMethodsAsync()).Where(m => m.ObjectId == _plc.MethodsObject).ToDictionary(m => m.MethodId.IdentifierAsString!);

            var add = methods["Methods.Add"];
            CollectionAssert.AreEqual(new[] { "a", "b" }, add.Inputs.Select(a => a.PropertyName).ToArray());
            Assert.AreEqual(typeof(int), add.Inputs[0].ValueType);
            Assert.AreEqual(BuiltInType.Int32, add.Inputs[0].BuiltInType);
            Assert.AreEqual("sum", add.Outputs.Single().PropertyName);
            var echo = methods["Methods.Echo"].Inputs.ToDictionary(a => a.PropertyName);
            Assert.AreEqual(typeof(double[]), echo["da"].ValueType);
            Assert.IsTrue(echo["da"].IsArray);
            Assert.AreEqual(typeof(byte[]), echo["bytes"].ValueType);
            var echoDensity = methods["Methods.EchoDensity"];
            Assert.IsNotNull(echoDensity.Inputs[0].Structure);
            Assert.IsFalse(echoDensity.Inputs[0].IsArray);
            Assert.IsTrue(echoDensity.Inputs[1].IsArray);
            Assert.AreEqual(typeof(string), methods["Methods.TextOut"].Outputs.Single().ValueType);
            Assert.IsEmpty(methods["Methods.NoArgs"].Inputs);
            Assert.IsEmpty(methods["Methods.NoArgs"].Outputs);
            Assert.AreEqual($"{OpcUaNaming.MessageNamespace(MonitorRegistryTest.Own.ExchangeNamePrefix)}.Exec_ns{_plc.NamespaceIndex}_s_Methods_Add_Command", add.ExecCommandTypeFullName);
        }

        [TestMethod]
        public async Task Discover_SharedMethod_OneExecPerObject()
        {
            var starts = (await DiscoverMethodsAsync()).Where(m => m.MethodId == _plc.Method("Start")).ToList();

            CollectionAssert.AreEquivalent(new[] { _plc.Pump1, _plc.Pump2 }, starts.Select(m => m.ObjectId).ToArray());
            Assert.AreEqual(2, starts.Select(m => m.ExecCommandTypeFullName).Distinct().Count());
            Assert.IsTrue(starts.All(m => m.ExecCommandTypeFullName.Contains("__")), "A shared method is named with its object");
        }

        [TestMethod]
        public async Task Discover_NoServerOwnMethods()
        {
            var methods = await DiscoverMethodsAsync(Settings(includeNamespaceZero: true));

            Assert.IsFalse(methods.Any(m => m.MethodId.NamespaceIndex == 0), string.Join(", ", methods.Where(m => m.MethodId.NamespaceIndex == 0).Select(m => m.MethodIdText)));
            Assert.IsTrue(methods.All(m => m.MethodId.NamespaceIndex == _plc.NamespaceIndex));
        }

        [TestMethod]
        public async Task Discover_MethodFilter_Narrows()
        {
            var methods = await DiscoverMethodsAsync(Settings(methodFilter: new Regex("Methods\\.Add$")));

            CollectionAssert.AreEqual(new[] { _plc.Method("Add") }, methods.Where(IsInMethodsObject).Select(m => m.MethodId).ToArray());
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
