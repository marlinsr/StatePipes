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

## Installing the plugin

The StatePipes repository is also a Claude Code plugin marketplace (`.claude-plugin/marketplace.json` at its root).
In an interactive `claude` session:

```
/plugin marketplace add marlinsr/StatePipes
/plugin install statepipes@statepipes
```

To install from a local clone instead, for example to try a branch, give the clone's path:

```
/plugin marketplace add C:/Users/<you>/source/repos/StatePipes
/plugin install statepipes@statepipes
```

Installed plugins are copied into Claude Code's plugin cache. After changing a skill, run
`/plugin marketplace update statepipes` and reinstall, or restart Claude Code if it doesn't pick the change up.

## Loading the plugin for one session (while editing skills)

```bash
claude --plugin-dir StatePipes.ClaudePlugin
```

Run from the repository root. Skill edits take effect the next time a session starts this way.

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
