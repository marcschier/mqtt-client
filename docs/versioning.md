# Versioning policy

`Mqtt.Client` follows [Semantic Versioning 2.0.0](https://semver.org/) once 1.0 ships.

## Version layout

The version is derived from `version.json` via [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning). The on-package version follows `{Major}.{Minor}.{GitHeight}[-{prerelease}][.g{shortSha}]`.

## What is a breaking change?

After 1.0, the following are considered breaking changes and require a major bump:

- Removal or rename of any public type, member, parameter, or namespace
- Changing the signature of a public method (return type, parameter type, parameter order, defaulting)
- Adding a required member to a public type a caller could already construct
- Tightening behavior contracts (e.g. throwing where we previously returned `false`)
- Bumping the minimum target framework
- Removing a transport, packet, or property previously supported

The following are **not** breaking:

- Adding new public types / methods / overloads (minor bump)
- Adding new optional MQTT 5 properties on existing types (minor bump)
- Performance improvements that preserve observable behavior (patch bump)
- Bug fixes that align with documented behavior (patch bump)
- Internal refactors (patch bump)

## Public-API guardrail

Every change to the exported surface must update `tests/Mqtt.Client.UnitTests/PublicApi.expected.txt`. CI runs the snapshot test on every PR — accidental surface changes fail the build.

## Deprecation policy

Public members may be marked `[Obsolete]` in a minor release and remain functional for at least one additional minor release before removal in the next major.

## Pre-1.0

While the version line is `0.x`, any release may include breaking changes. Lock to an exact version in production.
