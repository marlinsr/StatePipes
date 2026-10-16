using Opc.Ua;
using StatePipes.OpcUaBridge;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>Writing OPC UA variables through the bridge, against the in-process PLC's Setpoints nodes.</summary>
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    public class OpcUaBridgeSetTest
    {
        private static PlcServerFixture _plc = null!;

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext _) => _plc = await PlcServerFixture.StartAsync(48403);

        [ClassCleanup]
        public static async Task ClassCleanup() => await _plc.DisposeAsync();

        private static async Task<IReadOnlyList<OpcUaDataItem>> DiscoverAsync(BridgeSettings? settings = null)
        {
            await using OpcUaClient client = new(settings ?? _plc.CreateBridgeSettings(MonitorRegistryTest.Own));
            await client.ConnectAsync(CancellationToken.None);
            return await client.DiscoverAsync(CancellationToken.None);
        }

        [TestMethod]
        public async Task Discover_Setpoints_ReadableWithNaturalTypes()
        {
            var items = (await DiscoverAsync()).ToDictionary(i => i.NodeId);
            Assert.AreEqual(typeof(short), items[_plc.Setpoint("Int16")].ValueType);
            Assert.AreEqual(typeof(double[]), items[_plc.Setpoint("DoubleArray")].ValueType);
            Assert.IsNotNull(items[_plc.Setpoint("Density")].Structure);
            Assert.IsTrue(items[_plc.Setpoint("DensityArray")].IsArray);
            Assert.AreEqual(typeof(string), items[_plc.Setpoint("Matrix")].ValueType);
            Assert.HasCount(16, items.Keys.Where(n => n.IdentifierAsString?.StartsWith("Setpoints.") == true));
        }
    }
}
