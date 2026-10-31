# update-proxy

External Tool: **Update StatePipes Proxy** (`-u <proxy file>`). Generator: `ProxyGeneratorTool.GenerateProxy`.

Regenerates `<Prefix>/Proxies/<Moniker>Proxy.cs` after the remote service's public triggers or events changed.

## Who does it

Like `new-proxy`, the class is generated, so don't edit it by hand. Build first, then run the tool headless (see
"Running the tool headless" in the scaffold skill). `-u` names the existing proxy, which gives the moniker. The tool also keeps the
state machine the existing proxy targets, so don't pass `--statemachine` unless the model moved the proxy to
another machine:

```
& "<Tool>" -headless <common arguments> -u "<Prefix>/Proxies/<Moniker>Proxy.cs" --dll "<path to updated DLL>"
& "<Tool>" -headless -live <common arguments> -u "<Prefix>/Proxies/<Moniker>Proxy.cs" --broker <uri> --exchange <name> --cert "<.p12>" --certpw "<password file>"
```

Use the second form when the DLL isn't available. If headless isn't available, ask the user to select
`<Moniker>Proxy.cs` and run **Tools → Update StatePipes Proxy** or **Tools → Update StatePipes Proxy From Service**.

Because the proxy file already exists, the tool **skips** the `DefaultSetup.cs`, `DefaultServiceConfiguration.cs`
and `ProxyMonikers.cs` insertions. Nothing else changes.

## After

Build. Fix every compile error in states that used a removed or renamed remote trigger or event, then
`statepipes-verify`.
