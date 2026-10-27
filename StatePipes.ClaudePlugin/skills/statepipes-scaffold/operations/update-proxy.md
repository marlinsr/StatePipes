# update-proxy

External Tool: **Update StatePipes Proxy** (`-u <proxy file>`). Generator: `ProxyGeneratorTool.GenerateProxy`.

Regenerates `<Prefix>/Proxies/<Moniker>Proxy.cs` after the remote service's public triggers or events changed.

## Who does it

Like `new-proxy`, the class comes from reflection over the remote service's DLL: do not edit it by hand. Ask the
user to select `<Moniker>Proxy.cs` and run **Tools → Update StatePipes Proxy**, choosing the updated DLL.

Because the proxy file already exists, the tool **skips** the `DefaultSetup.cs`, `DefaultServiceConfiguration.cs`
and `ProxyMonikers.cs` insertions. Nothing else changes.

## After

Build. Fix every compile error in states that used a removed or renamed remote trigger or event, then
`statepipes-verify`.
