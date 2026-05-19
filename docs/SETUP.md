# StatePipes — Automated Release Setup

This is a one-time setup guide for the `.github/workflows/release.yml` workflow.
After this, every merge to `main` will run the full release pipeline.

## 1. Drop the workflow into the repo

Place the file at:

```
.github/workflows/release.yml
```

Commit and push it to `main`.

## 2. Add three repository secrets

Go to **Settings → Secrets and variables → Actions → New repository secret** and add:

| Secret name           | Value                                                                |
| --------------------- | -------------------------------------------------------------------- |
| `DOCKERHUB_USERNAME`  | The Docker Hub account that owns `bigfish88/*` (usually `bigfish88`) |
| `DOCKERHUB_TOKEN`     | A Docker Hub **access token** (Account Settings → Security)          |
| `NUGET_API_KEY`       | A nuget.org API key scoped to push `StatePipes` (and `*` of future versions) |

`GITHUB_TOKEN` is provided automatically — nothing to add for it.

## 3. (Optional but recommended) Add three PR labels

These let you control the version bump per-PR:

- `release:patch` — bumps `z` in `x.y.z` (this is the default if no label is set)
- `release:minor` — bumps `y`, resets `z` to 0
- `release:major` — bumps `x`, resets `y` and `z` to 0

Create them under **Issues → Labels → New label**.

## 4. How it works (per merge to main)

1. **bump-version** (Linux)
   - Reads the merged PR for that commit, picks the bump type from labels.
   - Bumps `<VersionPrefix>` in `SolutionInfo.proj`.
   - Parses the PR body for `closes #N` / `fixes #N` / `resolves #N`, looks up
     each issue title, and inserts a new entry under a `## Release Notes`
     section in `README.md`.
   - Commits the two file changes back to `main` with `[skip ci] [release-bot]`
     so it doesn't loop.

2. **build-windows** (Windows runner)
   - `dotnet restore` and `dotnet build StatePipes.sln -c Release`.
   - `StatePipes.csproj` has `GeneratePackageOnBuild=True` on Windows, so this
     produces `StatePipes/bin/Release/StatePipes.<version>.nupkg`.
   - `StatePipes.ServiceCreatorTool.Installer.csproj`'s post-build event runs
     `makensis.exe` (installed via Chocolatey) and produces
     `StatePipes.ServiceCreatorTool.Installer/StatePipes.ServiceCreatorTool.Installer.exe`.
   - Both artifacts are uploaded for the publish job.

3. **docker** (Linux runner, parallel to build-windows)
   - Logs into Docker Hub.
   - Builds and pushes:
     - `bigfish88/statepipesexplorer:v<version>` + `:latest`
     - `bigfish88/statepipesbrokerproxy:v<version>` + `:latest`

4. **publish** (Linux runner, after both)
   - Pushes the `.nupkg` to nuget.org (`--skip-duplicate` so re-runs are safe).
   - Creates a GitHub Release `v<version>` targeting `main`, with
     auto-generated notes plus the closing-issue block, and the installer `.exe`
     attached. The installer download URL stays in the same shape you have today:

     ```
     https://github.com/marlinsr/StatePipes/releases/download/v<version>/StatePipes.ServiceCreatorTool.Installer.exe
     ```

## 5. First-run checklist

Before the first merge that uses this workflow:

- [ ] `release.yml` lives at `.github/workflows/release.yml` on `main`
- [ ] Three secrets added (`DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`, `NUGET_API_KEY`)
- [ ] The Docker Hub token has *Write* permission on `bigfish88/statepipesexplorer`
      and `bigfish88/statepipesbrokerproxy`
- [ ] The NuGet API key is valid and scoped to push `StatePipes`
- [ ] `SolutionInfo.proj` currently shows the *last released* version — the next
      merge will bump from there. Right now it's `4.0.12`, so the next merge
      (with no label) will publish `v4.0.13`.

## 6. Manual fallback / troubleshooting

- The job will skip itself when the head commit has `[release-bot]` in its
  message (the bump commit). So you can land normal commits alongside without
  re-triggering.
- If a step fails partway through, you can re-run the workflow from the
  Actions tab — NuGet push is idempotent and `softprops/action-gh-release` will
  update an existing release.
- If you need to skip a specific merge (e.g. docs-only), include `[skip ci]`
  in the merge commit message.

## 7. Things to double-check on first real run

- That `dotnet 10.0.x` is what `actions/setup-dotnet` resolves to. .NET 10 is
  current; if Microsoft renames or the channel changes you may need to pin a
  specific SDK version.
- That `makensis.exe` from `choco install nsis` runs successfully against the
  `.nsi` script. The script's `!define MUI_ICON` references
  `${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico`, which ships with NSIS
  on Windows, so it should be fine.
- That the docker images build on `ubuntu-latest` — they're already
  Linux-based (`FROM mcr.microsoft.com/dotnet/sdk:10.0`), so this should be a
  drop-in for what you ran manually.
