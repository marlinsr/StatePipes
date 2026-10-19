# OpcUaServer (PLC simulator)

An OPC UA server at `opc.tcp://localhost:4840/PLCSimulator` (security None) for trying out StatePipes.OpcUaBridge.
Its nodes are in namespace `http://plcsimulator.org/` (normally `ns=2`) under `Objects/PLC_Data`.

## Simulated values

`pressure`, `msg`, `Density` and `Tank` change every 500 ms. `pressure`, `msg` and `Density` are writable, but a
written value is overwritten within half a second.

## Setpoints (for testing writes)

`PLC_Data/Setpoints` holds nodes the simulation never changes, so a value written to one stays until the next write.

| NodeId (`ns=2;s=...`) | Type | Bridge Set command |
|---|---|---|
| `Setpoints.Double` | Double | yes |
| `Setpoints.Int16` | Int16 | yes (written as Int16) |
| `Setpoints.Boolean` | Boolean | yes |
| `Setpoints.String` | String | yes |
| `Setpoints.DateTime` | DateTime | yes |
| `Setpoints.DoubleArray` | Double[] | yes |
| `Setpoints.Int32Array` | Int32[] | yes |
| `Setpoints.ByteArray` | Byte[] | yes (sent and published as base64) |
| `Setpoints.Density` | DensityDataType | yes |
| `Setpoints.DensityArray` | DensityDataType[] | yes |
| `Setpoints.Tank` | TankDataType | yes |
| `Setpoints.Percent` | Double, accepts 0–100 only | yes; values outside 0–100 are refused with `BadOutOfRange` |
| `Setpoints.ReadOnly` | Double, read only | no |
| `Setpoints.UserReadOnly` | Double, writable but not by the session's user | no |
| `Setpoints.Matrix` | Double, 2×2 matrix | no |
| `Setpoints.Number` | abstract Number | no |
| `Setpoints.Text` | LocalizedText | no |

## Methods (for testing calls)

`PLC_Data/Methods` is an object holding methods for the bridge's Exec commands, and `Methods.NoArgsCount`, an
Int32 that counts calls to `NoArgs`. `CalculatePressureVolumeMass` on `PLC_Data` is unchanged.

| NodeId (`ns=2;s=...`) | Arguments → outputs | Bridge Exec command |
|---|---|---|
| `Methods.Add` | (Int32 a, Int32 b) → Int32 sum | yes |
| `Methods.Echo` | (Double d, Int16 i16, Boolean b, String s, DateTime dt, Double[] da, Byte[] bytes) → the same | yes |
| `Methods.EchoDensity` | (DensityDataType density, DensityDataType[] densities) → the same | yes |
| `Methods.NoArgs` | () → (); increments `Methods.NoArgsCount` | yes |
| `Methods.Slow` | (UInt32 ms) → (); returns after `ms` milliseconds | yes |
| `Methods.Reject` | (Double percent) → (); outside 0–100 the call fails with `BadInvalidArgument` and `percent` gets `BadOutOfRange` | yes |
| `Methods.TextOut` | () → LocalizedText text | yes (output as a string) |
| `Methods.NotExecutable` | not executable | no |
| `Methods.UserNotExecutable` | executable, but not by the session's user | no |
| `Methods.MatrixIn` | (Double[,] matrix) → () | no |
| `Methods.Start` | () → (); one method node, a component of `Methods.Pump1` and also referenced from `Methods.Pump2` | yes, one per pump |
