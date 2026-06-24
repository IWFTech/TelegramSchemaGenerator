# Contributing to TeleFlow Telegram Schema Generator

This repository owns Telegram Bot API documentation parsing, schema extraction, normalization, validation, and generated C# output.

## Prerequisites

- .NET 10 SDK. The repository uses `global.json` with `rollForward: latestFeature`.
- PowerShell 7+ for local automation.

## Local Verification

Run from the repository root:

```powershell
dotnet restore ./TeleFlow.Telegram.SchemaGenerator.csproj
dotnet restore ./tests/TeleFlow.Telegram.SchemaGenerator.Tests/TeleFlow.Telegram.SchemaGenerator.Tests.csproj
dotnet format whitespace ./TeleFlow.Telegram.SchemaGenerator.csproj --verify-no-changes --no-restore --verbosity minimal
dotnet format style ./TeleFlow.Telegram.SchemaGenerator.csproj --verify-no-changes --no-restore --verbosity minimal
dotnet build ./TeleFlow.Telegram.SchemaGenerator.csproj -c Release --no-restore /nodeReuse:false
dotnet test ./tests/TeleFlow.Telegram.SchemaGenerator.Tests/TeleFlow.Telegram.SchemaGenerator.Tests.csproj -c Release --no-restore /nodeReuse:false --logger "console;verbosity=minimal"
```

## Pull Request Expectations

- Keep parsing, extraction, normalization, validation, and code generation concerns separate.
- Do not add silent fallbacks for unknown Telegram documentation shapes.
- Keep generated output deterministic.
- Update schema or generator version constants when the corresponding contract changes.
- Include fixture or regression coverage when parser, normalizer, or generator behavior changes.

## Relationship To TeleFlow

The main `IWFTech/TeleFlow` repository stores checked-in generated output and runtime/package tests.

This repository owns how that output is produced.
