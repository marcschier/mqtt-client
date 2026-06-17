# Security policy

## Supported versions

| Version | Status |
| --- | --- |
| 0.x (alpha) | Best-effort — current `main` is the only supported branch. |

Once a 1.x release ships, the most recent minor will receive security fixes.

## Reporting a vulnerability

**Please do not open a public issue for suspected vulnerabilities.**

Report privately via GitHub Security Advisories:

> [https://github.com/marcschier/mqtt-client/security/advisories/new](https://github.com/marcschier/mqtt-client/security/advisories/new)

Expected response window: an acknowledgement within 7 days, an initial assessment
within 30 days.

Please include:
- A description of the vulnerability and its impact.
- Steps to reproduce, ideally a minimal repro.
- The library version (commit SHA / package version) you reproduced on.
- Whether the issue is theoretical or you have a working exploit.

## Scope

In scope:
- Memory-safety issues in the codec, transport, dispatcher, or client.
- Authentication / TLS bypasses.
- Denial-of-service vectors with realistic preconditions (e.g. malicious broker bytes
  causing unbounded memory growth).
- Logic errors that violate the MQTT 3.1.1 / 5.0 spec in a security-relevant way.

Out of scope:
- Issues that require a hostile process already running on the same machine.
- Issues in third-party dependencies (report those upstream; we'll update once they
  ship a fix).
- Denial-of-service requiring an attacker to send a sustained gigabit-scale flow.

## Threat model

See [docs/security-audit.md](./docs/security-audit.md) for the full threat model and
the latest assessment.
