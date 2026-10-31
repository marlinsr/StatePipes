# new-proxy

External Tool: **New StatePipes Proxy**. Generator: `ProxyGeneratorTool.GenerateProxy` plus `ProxyGenerator`.

A proxy lets this service send the public triggers of another StatePipes service and subscribe to its events.

## Who does it

The proxy class is generated from the other service's public triggers and events. Do **not** write it by hand.
Run the installed tool headless (no Visual Studio, no dialogs). See "Running the tool headless" in the scaffold
skill for `<Tool>`, the common arguments and the success check.

1. `dotnet build <SolutionDir>/<Solution>.slnx`. The tool reads the built output of this service.
2. Choose the target machine `<Machine>` (see "Which state machine" below). Always pass it as
   `--statemachine <Machine>`. With more than one machine the tool won't guess, and without the argument it stops
   with `Headless mode needs --statemachine <name>; one of: ...`.
3. From the other service's compiled DLL (preferred). Use the class library DLL in the other service's
   **`.Service`** output folder, which also holds `StatePipes.dll`, for example
   `<OtherSolutionDir>/<OtherPrefix>.Service/bin/Debug/net10.0/<OtherPrefix>.dll`:

   ```
   & "<Tool>" -headless <common arguments> --moniker <Moniker> --statemachine <Machine> --dll "<path to other service DLL>"
   ```

4. Or from the running service (`-live`), when no DLL is available. The service must be running and reachable.
   Ask the user for the broker URI, the service's exchange name, and the paths of the client certificate (`.p12`)
   and its password file. Never read or print the certificate password itself:

   ```
   & "<Tool>" -headless -live <common arguments> --moniker <Moniker> --statemachine <Machine> --broker <uri> --exchange <name> --cert "<.p12>" --certpw "<password file>" [--timeout <seconds>]
   ```

5. `dotnet build` again, then check the result as below.

If the tool isn't installed, or it prints `Incorrect Parameters Specified!` (an installed version older than
headless support), hand this step to the user: in Visual Studio, with `<Prefix>` active, **Tools → New StatePipes
Proxy** (select the DLL), or **Tools → Create StatePipes Proxy From Service** for the live route. Then continue here.

## Which state machine

The proxy turns every event it receives from the other service into an internal trigger named
`<Event>From<Moniker>Trigger` on **one** machine of this service, and it sends `ProxyConnectionStatusTrigger` to that
same machine. The target machine is therefore the one whose states react to that service. Take it from the approved
model:

1. The model's Proxies table names the target machine for each proxy. Use it.
2. Otherwise, take the machine whose transitions table has the handler rows for the proxy's events consumed
   (`<Event>From<Moniker>Trigger`) or for its connection status.
3. If the model's rows for that proxy are spread over several machines, choose the machine with most of them and
   forward to the others with `FireExternal`. Record the choice in the model's Proxies table.
4. If no machine handles anything from that proxy yet, the model is incomplete. Ask the user which machine reacts
   to the service, and add the rows.

Ask the user only in case 4. A service with a single machine still gets `--statemachine`, so the command is the same
every time.

## What the tool produces (to check afterwards)

Creates: <Prefix>/Proxies/<Moniker>Proxy.cs

Internal triggers `<Event>From<Moniker>Trigger` are generated inside the proxy file, in the namespace
`<Prefix>.StateMachines.<Machine>.Triggers.Internal`. If `<Machine>` has no
`StateMachines/<Machine>/Triggers/Internal/ProxyConnectionStatusTrigger.cs` (every machine except the service's first
one), the tool creates it from `ProxyConnectionStatusTrigger_cs.sample` and prints `Created <path>`.

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
through `ProxyConnectionStatusTrigger` in `<Machine>`.
