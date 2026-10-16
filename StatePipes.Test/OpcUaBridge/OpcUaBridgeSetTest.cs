using Opc.Ua;
using StatePipes.OpcUaBridge;
using System.Text.RegularExpressions;

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

        private static readonly string[] WritableSetpoints = ["Double", "Int16", "Boolean", "String", "DateTime", "DoubleArray", "Int32Array", "Density", "DensityArray", "Tank", "Percent"];
        private static readonly string[] UnwritableSetpoints = ["ReadOnly", "UserReadOnly", "Matrix", "Number", "Text"];

        [TestMethod]
        public async Task Discover_Setpoints_WritableExactlyAsSpecified()
        {
            var items = (await DiscoverAsync()).ToDictionary(i => i.NodeId);
            foreach (var name in WritableSetpoints) Assert.IsTrue(items[_plc.Setpoint(name)].IsWritable, name);
            foreach (var name in UnwritableSetpoints) Assert.IsFalse(items[_plc.Setpoint(name)].IsWritable, name);
            Assert.AreEqual(BuiltInType.Int16, items[_plc.Setpoint("Int16")].BuiltInType);
            Assert.AreEqual(BuiltInType.Double, items[_plc.Setpoint("DoubleArray")].BuiltInType);
        }

        [TestMethod]
        public async Task Discover_WriteFilter_NarrowsWritable()
        {
            var settings = _plc.CreateBridgeSettings(MonitorRegistryTest.Own);
            var filtered = new BridgeSettings
            {
                BusConfig = settings.BusConfig,
                OpcUaEndpoint = settings.OpcUaEndpoint,
                UseSecurity = settings.UseSecurity,
                AutoAcceptUntrustedCertificates = settings.AutoAcceptUntrustedCertificates,
                PkiPath = settings.PkiPath,
                ApplicationName = settings.ApplicationName,
                WriteFilter = new Regex("Setpoints\\.Double$")
            };
            var items = (await DiscoverAsync(filtered)).Where(i => i.NodeId.IdentifierAsString?.StartsWith("Setpoints.") == true).ToList();
            CollectionAssert.AreEqual(new[] { _plc.Setpoint("Double") }, items.Where(i => i.IsWritable).Select(i => i.NodeId).ToArray());
            Assert.IsTrue(items.Any(i => i.NodeId == _plc.Setpoint("ReadOnly")), "Unwritable items stay readable");
        }

        [TestMethod]
        public async Task WriteValues_WritesAndReturnsPerNodeResults()
        {
            await using OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));
            await client.ConnectAsync(CancellationToken.None);

            var results = await client.WriteValuesAsync([(_plc.Setpoint("Double"), new Variant(42.0)), (_plc.Setpoint("Percent"), new Variant(150.0))], CancellationToken.None);

            CollectionAssert.AreEqual(new[] { StatusCodes.Good, StatusCodes.BadOutOfRange }, results.Select(r => r.Code).ToArray());
            var read = await client.ReadValuesAsync([_plc.Setpoint("Double")], CancellationToken.None);
            Assert.AreEqual(42.0, read[0].WrappedValue.AsBoxedObject(Variant.BoxingBehavior.Legacy));
        }

        [TestMethod]
        public async Task WriteValues_NotConnected_AllBadNotConnected()
        {
            await using OpcUaClient client = new(_plc.CreateBridgeSettings(MonitorRegistryTest.Own));

            var results = await client.WriteValuesAsync([(_plc.Setpoint("Double"), new Variant(1.0)), (_plc.Setpoint("Int16"), new Variant((short)1))], CancellationToken.None);

            CollectionAssert.AreEqual(new[] { StatusCodes.BadNotConnected, StatusCodes.BadNotConnected }, results.Select(r => r.Code).ToArray());
        }
    }
}
