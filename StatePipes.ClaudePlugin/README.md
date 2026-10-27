# StatePipes Claude Code Plugin

Skills that guide Claude Code to design, scaffold, extend and verify a single StatePipes microservice.

Use StatePipes when a microservice under development has distinct behavior based on state or mode.

## Skills

| Skill | Use it when |
|---|---|
| `statepipes-domain-fit` | Starting a new service or feature: decides whether StatePipes fits |
| `statepipes-domain-modeling` | The fit is confirmed: turns the domain into an approved model of machines, states, triggers, events, guards, timers and proxies |
| `statepipes-conventions` | Reading or editing any StatePipes service |
| `statepipes-scaffold` | Creating or extending code: one operation per Visual Studio External Tool |
| `statepipes-verify` | Before calling work done |

**New service:** `domain-fit` → `domain-modeling` → `scaffold` + `conventions` → `verify`.
**Extending a service:** `conventions` + `scaffold` → `verify`.

## Loading the plugin locally

```bash
claude --plugin-dir StatePipes.ClaudePlugin
```

## Keeping it accurate

Scaffold recipes in `skills/statepipes-scaffold/operations/` cite the ServiceCreatorTool templates and injection
markers in a machine-readable form. Tests in `StatePipes.Test/ClaudePlugin/` check them against
`StatePipes.ServiceCreatorTool/Resources` and against the real generators, so run the test suite after changing a
template:

```bash
dotnet test --settings Tests.runsettings --filter "FullyQualifiedName~ClaudePlugin"
```

## Design

See `docs/superpowers/specs/2026-10-27-claude-plugin-design.md` (issue #93).
