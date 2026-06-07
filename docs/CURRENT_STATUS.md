# CURRENT STATUS

## Repository State

DropSendTo is a Windows-only .NET 10 WPF launcher. The canonical implementation lives under `src/DropSendTo/`, with xUnit coverage under `tests/DropSendTo.Tests/`.

The current architecture and behavior are documented through the SDD/TDD set:

- Requirements: `docs/REQUIREMENTS.md`
- Spec: `docs/SPEC.md`
- Design: `docs/DESIGN.md`
- Detailed design: `docs/DETAILED_DESIGN.md`
- Test plan: `docs/TESTPLAN.md`
- Macro samples: `docs/MACRO_SAMPLES.md`

## Current Truth

- `AGENTS.md` is the agent entrypoint and contains repository guardrails, validation policy, and change coupling rules.
- `README.md` is the human quickstart and user-facing repository overview.
- `USER_GUIDE.md` is the user-facing operation guide and must be updated for feature or behavior changes.
- `docs/REQUIREMENTS.md`, `docs/SPEC.md`, `docs/DESIGN.md`, and `docs/TESTPLAN.md` are the normative SDD/TDD documents. Keep FR/NFR/CON, SP, DES, and TC traceability synchronized when behavior changes.
- Generated release output belongs under `dist/` and should not be edited directly.

## Daily Commands

Run commands on Windows with .NET SDK 10.x:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet format
```

From WSL, call Windows PowerShell as documented in `AGENTS.md` and `README.md`.

For the standard validation script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-Tests-And-Build.ps1
```

For release packaging:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Rid win-x64 -Version vX.Y.Z
```

## Validation Matrix

- Docs-only change: check affected links, headings, version mentions, and traceability references.
- Code change: run `dotnet test` and `dotnet build`; update SDD/TDD docs when behavior changes.
- UI change: run relevant automated tests and perform a Windows GUI smoke check where practical; include screenshots/GIFs for PRs.
- Config model change: update `ConfigTransferService` export/import snapshots and `tests/DropSendTo.Tests/ConfigTransferServiceTests.cs`.
- Macro command change: update `KeyboardMacroService` validation mode, `src/DropSendTo/MacroTipsWindow.xaml`, `src/DropSendTo/RegisterDialog.xaml.cs` snippet groups, unit tests, `docs/SPEC.md`, and `docs/MACRO_SAMPLES.md` when applicable.
- Release change: use `scripts/Build-Release.ps1`; do not hand-edit `dist/` output.

## Open Gaps

- README screenshot/GIF section is still marked as coming soon.
- There is no dedicated task board document in this repository; use issues or the current user request as the active work source unless one is added deliberately.
