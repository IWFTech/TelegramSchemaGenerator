# TeleFlow Telegram Schema Generator

The generator is a strict Telegram docs compiler:
- `parse-docs` parses Telegram HTML into a structured raw snapshot
- `normalize` extracts and validates schema sections, type expressions, naming, and abstractions
- `generate` writes the checked-in schema output and, when requested, the runtime Telegram client extension output

The pipeline is fail-closed. It must not silently skip schema sections or guess unresolved type expressions.

Snapshot metadata:
- `raw` snapshot contains:
  - `SourceUrl`
  - `SourceCapturedAtUtc`
  - `SourceSha256`
  - `TelegramBotApiVersion`
  - `TelegramBotApiReleasedAt`
  - `TelegramBotApiChangelogAnchor`
- `normalized` snapshot additionally contains:
  - `SchemaVersion`
  - `GeneratorVersion`

Generated `.g.cs` files must repeat the normalized snapshot provenance in their auto-generated header, including the Telegram Bot API version and changelog anchor used to build the schema.

The runtime Telegram output is intentionally separate from `TeleFlow.Telegram.Schema`:
- schema DTOs, methods, responses, and abstractions are written to `TeleFlow.Telegram.Schema`
- generated `ITelegramClient` method extensions are written to `TeleFlow.Telegram.Client/Generated/Methods`
- generated known update-type constants are written to `TeleFlow.Telegram.Client/Generated/TelegramUpdateTypes.g.cs`
- client extensions are thin convenience methods that construct generated method models, apply supported bot defaults, and call `ITelegramClient.SendAsync(...)`

## Commands

The examples below assume the main TeleFlow repository is available next to this repository:

```powershell
$teleflow = "..\TeleFlow"
```

Parse official Telegram Bot API docs into the checked-in raw snapshot:

```powershell
dotnet run -- parse-docs --url https://core.telegram.org/bots/api --output "$teleflow\schema\telegram-bot-api\raw\telegram-bot-api.raw.json"
```

Normalize the raw snapshot:

```powershell
dotnet run -- normalize --input "$teleflow\schema\telegram-bot-api\raw\telegram-bot-api.raw.json" --output "$teleflow\schema\telegram-bot-api\normalized\telegram-bot-api.normalized.json"
```

Generate the schema project and Telegram runtime client extensions:

```powershell
dotnet run -- generate --input "$teleflow\schema\telegram-bot-api\normalized\telegram-bot-api.normalized.json" --generated-output "$teleflow\TeleFlow.Telegram.Schema" --telegram-output "$teleflow\TeleFlow.Telegram.Client"
```

Run the full pipeline:

```powershell
dotnet run -- all --url https://core.telegram.org/bots/api --raw-output "$teleflow\schema\telegram-bot-api\raw\telegram-bot-api.raw.json" --normalized-output "$teleflow\schema\telegram-bot-api\normalized\telegram-bot-api.normalized.json" --generated-output "$teleflow\TeleFlow.Telegram.Schema" --telegram-output "$teleflow\TeleFlow.Telegram.Client"
```

## Review Order
1. raw snapshot diff
2. normalized snapshot diff
3. generator tool changes
4. generated schema output
5. generated Telegram runtime client extension output

## Version Bumps
- Bump `SchemaVersion` when extraction or normalization semantics change.
- Bump `GeneratorVersion` when generated C# output contract changes.
- Do not bump versions only because Telegram documentation content changed.
- The current metadata/header contract is `SchemaVersion = 6` and `GeneratorVersion = 8`.

## Maintenance Expectations
- Treat `raw -> normalized -> generated` as one compiler pipeline.
- Keep document parsing, schema extraction, normalization, and generation logically separate.
- If Telegram docs introduce a new section shape, update the parser or extractor explicitly instead of adding a silent fallback.
- Keep generated header emission centralized so provenance format cannot drift between output kinds.
- Keep `ClientMethod` header emission consistent with schema output headers.
- Keep Telegram Bot API version metadata extracted from the official changelog section; do not hardcode it in generated files.
- Keep named Telegram union families as typed case wrappers with explicit match metadata.
- Keep discriminator literal values in normalized snapshots; do not derive them from generated C# names.
- Keep anonymous union wrappers typed; do not reintroduce public `object Value`.
- Keep `InputFile` as an upload pseudo-type in schema and leave multipart execution to `TeleFlow.Telegram`.
- Normalize upload-capable `String` fields that mention `attach://` or multipart upload into `InputFile or String`.
- Keep public generated declarations readable; prefer normal `using` directives or aliases over public `global::...` qualification.
- Keep generated runtime client extensions transport-free and dispatcher-free: no retry, HTTP, polling, or context-specific logic belongs there.
- Bot default application is allowed in generated runtime extensions because it is part of the `TeleFlow.Telegram` method-construction DX layer, not schema or transport behavior.
