# Changelog

TeleFlow Telegram Schema Generator follows SemVer for the generator CLI, schema normalization contract, and generated C# output contract.

## Unreleased

### Added

- Generated grouped constants for union discriminator literals such as chat member statuses, BotCommandScope types, and PassportElementError sources.
- Initial standalone repository baseline for the Telegram Bot API schema extraction and code generation tool.
- Solution-based `src/` and `tests/` repository layout.
- Local verification script at `eng/verify.ps1`.
- Telegram Bot API monitor workflow foundation for opening generated schema update pull requests in `IWFTech/TeleFlow`.
- Generated TeleFlow badge metadata updates for the supported Telegram Bot API version.
