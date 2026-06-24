# Security Policy

## Supported Versions

TeleFlow Telegram Schema Generator has not reached its first stable public release yet.

Before `1.0.0`, security fixes are made on the active development branch and included in the next prerelease or release candidate.

After the first stable release, the latest stable minor line is the supported line unless a release note says otherwise.

## Reporting a Vulnerability

Use GitHub private vulnerability reporting for this repository.

Do not open a public issue with exploit details, private credentials, unpublished Telegram Bot API source snapshots, or supply-chain details that could be abused before a fix is available.

Include:
- affected generator version or commit;
- minimal reproduction or affected command;
- expected impact;
- whether generated output, package publishing, or downstream TeleFlow releases are affected;
- relevant environment details such as OS, architecture, .NET SDK/runtime, and command arguments.

## Scope

Security-sensitive areas include:
- Telegram Bot API documentation parsing;
- schema snapshot provenance and source hashing;
- generated C# output integrity;
- deterministic output and reproducibility;
- package publishing and supply-chain integrity;
- denial-of-service risks in parsing, normalization, validation, or generation;
- path handling for input/output arguments.

## Disclosure

Security fixes should be released with clear notes once a fix is available. Public details should not be disclosed before maintainers have had a reasonable chance to patch and publish.
