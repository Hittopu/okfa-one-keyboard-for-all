# Contributing

Thanks for contributing to `okfa`.

## Before you change code

- Keep platform-specific changes scoped to either `mac/` or `windows/`.
- Prefer small, reviewable changes.
- Do not commit build outputs or app bundles.

## Development expectations

- Keep user-facing naming aligned to `okfa`.
- Preserve the current BLE protocol unless you are intentionally changing it on both platforms.
- When changing UI, keep macOS and Windows branding consistent even if the native layout patterns differ.

## Validation

For Windows changes:

```powershell
cd windows\okfa.windows
dotnet build
```

For macOS changes:

```bash
cd mac
./build_mac_phase1.sh
```

## Pull requests

- Describe what changed
- Explain how you tested it
- Include screenshots for UI changes when possible
