# new-proxy

External Tool: **New StatePipes Proxy**. Generator: `ProxyGeneratorTool.GenerateProxy` plus `ProxyGenerator`.

A proxy lets this service send the public triggers of another StatePipes service and subscribe to its events.

## Who does it

The proxy class is generated from the other service's public triggers and events. Do **not** write it by hand.
Run the installed tool headless (no Visual Studio, no dialogs). See "Running the tool headless" in the scaffold
skill for `<Tool>`, the common arguments and the success check.

1. `dotnet build <SolutionDir>/<Solution>.slnx`. The tool reads the built output of this service.
2. From the other service's compiled DLL (preferred). Use the class library DLL in the other service's
   **`.Service`** output folder, which also holds `StatePipes.dll`, for example
   `<OtherSolutionDir>/<OtherPrefix>.Service/bin/Debug/net10.0/<OtherPrefix>.dll`:

   ```
   & "<Tool>" -headless <common arguments> --moniker <Moniker> --dll "<path to other service DLL>"
   ```

3. Or from the running service (`-live`), when no DLL is available. The service must be running and reachable.
   Ask the user for the broker URI, the service's exchange name, and the paths of the client certificate (`.p12`)
   and its password file. Never read or print the certificate password itself:

   ```
   & "<Tool>" -headless -live <common arguments> --moniker <Moniker> --broker <uri> --exchange <name> --cert "<.p12>" --certpw "<password file>" [--timeout <seconds>]
   ```

4. `dotnet build` again, then check the result as below.

If the tool isn't installed, or it prints `Incorrect Parameters Specified!` (an installed version older than
headless support), hand this step to the user: in Visual Studio, with `<Prefix>` active, **Tools → New StatePipes
Proxy** (select the DLL), or **Tools → Create StatePipes Proxy From Service** for the live route. Then continue here.

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
