# Releasing CnCNew

A release is a **tag** `vMAJOR.MINOR.PATCH`. A pipeline builds finished binaries from the tag; the release holds no source archive that a player must build
and no game data. The launcher (`tools/CnCNew.Launcher`) reads the releases and installs the client from them.

The project owner announces a release; the version number is then raised (`MAJOR.MINOR.PATCH`, Semantic Versioning: 0.x while the game is in test).

## What a release holds

| Asset | What it is |
|---|---|
| `CnCNew-Launcher-<version>.exe` + `.sha256` | the launcher a player starts (`scripts/package-launcher.ps1`) |
| `CnCNew-Client-<version>-win-x64.zip` + `.sha256` | the self-contained client and the patcher (`scripts/package-client.ps1 -Zip`); no .NET installation needed |
| `CnCNew-Server-<version>-win-x64.zip` / `-linux-x64.zip` + `.sha256` | the dedicated server (`scripts/package-server.ps1 -Zip`, `docs/DEDICATED-SERVER.md`) |
| the release page text | the section of `CHANGELOG.md` for the version |

The names are the ones the launcher expects (client and launcher, each with its `.sha256` in `sha256sum` format: `<hash>  <file name>`). No asset contains a file of
the game: `scripts/check-separation.ps1` is run on the client package by the build and fails the release otherwise. The player's game files are made on the
player's machine from their own Steam installation.

## The version

`version.txt` (one line, `MAJOR.MINOR.PATCH`) is the product version. `Directory.Build.props` reads it into the assembly, file and informational version of every
project (`install.json` states it as `productVersion`). It is **not** the build identity of the network layers (`CnCNew.Net.BuildIdentity`: commit and modified
flag) and moves no protocol, simulation or manifest value; two clients of the same version but different commits still refuse each other on a server.

## Release day (the person who tags)

1. On `master`, working tree clean, everything pushed that belongs in the release.
2. `pwsh scripts/audit-publish.ps1` - no game data in the history. It needs the game data, so no pipeline can do it for you.
3. `pwsh scripts/test.ps1 -Tier full` - tier 4 (a release is a trigger for it, `docs/TESTING.md`); it needs the game data and a display. Also
   `pwsh scripts/package-client.ps1` / `scripts/check-client-package.ps1` if the packaging changed.
4. `pwsh scripts/release.ps1 -Version X.Y.Z -DryRun` shows the checks and the CHANGELOG section it would write. Then
   `pwsh scripts/release.ps1 -Version X.Y.Z`: it refuses unless the tree is clean and the branch is `master`, raises `version.txt`, adds the `CHANGELOG.md` entry
   from the Conventional Commit subjects since the last tag, commits `chore(release): vX.Y.Z` and creates the annotated tag `vX.Y.Z`. **It never pushes.**
   Read the new `CHANGELOG.md` entry and amend the commit if the notes need words (before pushing).
5. `git push origin master vX.Y.Z` - the tag starts the pipeline.
6. Watch the pipeline. When it is green, open the release page, check the assets, download the launcher on a machine that never saw the project and run it.

A wrong release is not repaired by moving the tag: delete the release, raise the patch version and release again.

## The two pipelines

The repository's remote is GitHub and the launcher reads **GitHub releases**, so `.github/workflows/release.yml` is the pipeline that matters for players. The
GitLab pipeline `.gitlab-ci.yml` does the same for a GitLab copy of the repository (a mirror, or if the project moves). Both call the same script,
`scripts/release-build.ps1 -Version X.Y.Z`, so what a release holds is defined in one place; the script can also be run by hand on a clean checkout of the tag.

| | GitHub (`.github/workflows/release.yml`) | GitLab (`.gitlab-ci.yml`) |
|---|---|---|
| starts on | tag `v[0-9]+.[0-9]+.[0-9]+`; "Run workflow" builds without publishing | tag matching `^v\d+\.\d+\.\d+$`; manual job `build:dry-run` on branches builds without publishing |
| runner | `windows-latest` (PowerShell 7 and Git included, .NET 10 SDK by `actions/setup-dotnet` from `global.json`) | a Windows runner tagged `windows` with PowerShell 7, Git and the .NET 10 SDK, and a Linux Docker runner tagged `docker` for the release job |
| publishes | `softprops/action-gh-release` creates the release with the assets | the assets go to the generic package registry, `release-cli` creates the release page with links |

Neither pipeline could be run in the session that wrote them (no runner was available): the YAML was checked for syntax only, and the first real tag is the first real
test. Expect to adjust runner tags, the shell of the GitLab Windows runner (the script lines are PowerShell) and the name of the output of `package-launcher.ps1`.

## What the pipelines do not do

- They do not run `scripts/audit-publish.ps1` or the test tiers: both need the game data, which no runner has.
- They do not sign the launcher or the binaries; Windows SmartScreen may warn on a first start.
- They do not push anything back, bump versions or write the changelog: `scripts/release.ps1` does that, locally.
- They do not publish pre-releases: only tags of the form `vX.Y.Z` start them.
