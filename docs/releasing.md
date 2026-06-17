# Releasing Mqtt.Client

## Versioning

`Mqtt.Client` uses [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning).
The base version lives in `version.json`; the patch / pre-release suffix is computed
from git height and the current branch.

### How a version is constructed

| `version.json` `version` | Branch | Resulting NuGet version |
| --- | --- | --- |
| `0.1-alpha` | feature branch | `0.1.{height}-alpha.g{shortsha}` (private) |
| `0.1-alpha` | `main` (public ref) | `0.1.{height}-alpha` |
| `0.1-alpha` | `release/v0.1` | `0.1.{height}` (stable) |

`publicReleaseRefSpec` in `version.json` controls which refs produce "stable" public
versions. By default: `main` and `release/v*` branches.

### Local pack

```bash
dotnet pack src/Mqtt.Client -c Release -o nupkg
```

The package + symbol package land in `nupkg/`. The version embedded in the `.nuspec`
will reflect your current commit / branch.

### Cutting a release

1. Bump `version.json` `version` to e.g. `"0.2-alpha"`.
2. Commit and push to `main`.
3. Tag the commit as `v0.2.<height>-alpha` (use the version BDN/NB.GV reports).
4. The `pack-nupkg` job in `.github/workflows/ci.yml` builds and uploads the nupkg
   artifact when a tag matching `v*` is pushed.
5. To publish to nuget.org, manually download the artifact and run
   `dotnet nuget push <pkg> -k <key> -s https://api.nuget.org/v3/index.json`,
   or wire up an automated push step (intentionally left manual to avoid accidental
   publishes).

### Increment policy

`version.json` declares `release.versionIncrement = "minor"`. Use `nbgv release` to
automate the branch-cut + version bump locally:

```bash
dotnet tool install --global nbgv
nbgv prepare-release   # creates release/v0.1 branch and bumps main to 0.2
```

### Pre-1.0 stability

The library is currently `0.x` (alpha). Public API is permitted to change between
minor versions. After 1.0, semver applies: breaking changes only on major bumps.
