# new-proxy

External Tool: **New StatePipes Proxy**. Generator: `ProxyGeneratorTool.GenerateProxy` plus `ProxyGenerator`.

A proxy lets this service send the public triggers of another StatePipes service and subscribe to its events.

## Who does it

The proxy class is generated from the other service's public triggers and events. Do **not** write it by hand.
Ask the user to run one of these in Visual Studio with `<Prefix>` (the class library) active, giving the moniker
`<Moniker>`:

- **Tools → New StatePipes Proxy**, when the other service's compiled DLL is available: select that DLL.
- **Tools → Create StatePipes Proxy From Service** (`-live`), when only the running service is available: enter the
  broker URI and the service's exchange name, then select the client certificate (`.p12`) and its password file.
  The service must be running and reachable.

Then continue here.

## What the tool produces (to check afterwards)

Creates: <Prefix>/Proxies/<Moniker>Proxy.cs

The first time a moniker is used it also inserts, above each marker:

| File | Snippet | Marker |
|---|---|---|
| `<Prefix>/Builders/DefaultSetup.cs` | `DefaultSetup_Register.sample` | Injection Point 1 |
| `<Prefix>/Builders/DefaultSetup.cs` | `DefaultSetup_Build.sample` | Injection Point 2 |
| `<Prefix>/Builders/DefaultServiceConfiguration.cs` | `DefaultServiceConfiguration_AddProxy.sample` | Injection Point 4 |
| `<Prefix>/ValueObjects/ProxyMonikers.cs` | `ProxyMonikers_Moniker.sample` | Injection Point 1 |

Template: DefaultSetup_Register.sample
Template: DefaultSetup_Build.sample
Template: DefaultServiceConfiguration_AddProxy.sample
Template: ProxyMonikers_Moniker.sample
Marker: // Injection Point 1, Do Not Delete or Modify
Marker: // Injection Point 2, Do Not Delete or Modify
Marker: // Injection Point 4, Do Not Delete or Modify

## After

Verify the four insertions exist and `<Moniker>Proxy.cs` builds. Handle the remote service's connection status
through the internal `ProxyConnectionStatusTrigger` the service already has.
