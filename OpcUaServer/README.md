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
| `Setpoints.Density` | DensityDataType | yes |
| `Setpoints.DensityArray` | DensityDataType[] | yes |
| `Setpoints.Tank` | TankDataType | yes |
| `Setpoints.Percent` | Double, accepts 0–100 only | yes; values outside 0–100 are refused with `BadOutOfRange` |
| `Setpoints.ReadOnly` | Double, read only | no |
| `Setpoints.UserReadOnly` | Double, writable but not by the session's user | no |
| `Setpoints.Matrix` | Double, 2×2 matrix | no |
| `Setpoints.Number` | abstract Number | no |
| `Setpoints.Text` | LocalizedText | no |
