# Contributing to TeleFlow Telegram Schema Generator

This repository owns Telegram Bot API documentation parsing, schema extraction, normalization, validation, and generated C# output.

## Prerequisites

- .NET 10 SDK. The repository uses `global.json` with `rollForward: latestFeature`.
- PowerShell 7+ for local automation.

## Local Verification

Run from the repository root:

```powershell
./eng/verify.ps1
```

The script runs restore, whitespace formatting verification, style formatting verification, build, and tests for `TeleFlow.Telegram.SchemaGenerator.sln`.

## Pull Request Expectations

- Keep parsing, extraction, normalization, validation, and code generation concerns separate.
- Do not add silent fallbacks for unknown Telegram documentation shapes.
- Keep generated output deterministic.
- Update schema or generator version constants when the corresponding contract changes.
- Include fixture or regression coverage when parser, normalizer, or generator behavior changes.

## Relationship To TeleFlow

The main `IWFTech/TeleFlow` repository stores checked-in generated output and runtime/package tests.

This repository owns how that output is produced.
