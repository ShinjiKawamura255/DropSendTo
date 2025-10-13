# Repository Guidelines

## Project Structure & Module Organization
- `src/DropSendTo/`: Main .NET 8 Windows app (WPF/WinUI style).
- `tests/DropSendTo.Tests/`: Unit/integration tests mirroring `src` namespaces.
- `assets/`: Icons and UI resources (no large binaries in Git).
- `docs/`: REQUIREMENTS, SPEC, DESIGN, TESTPLAN and architecture notes.

## Build, Test, and Development Commands
- Build: `dotnet build` (Windows .NET 8 SDK; not WSL).
- Run: `dotnet run --project src/DropSendTo`.
- Test: `dotnet test` (xUnit default).
- Format: `dotnet format` (requires `dotnet-format` in SDK workloads).
- **WSL での .NET 実行**: 本環境の WSL には .NET SDK が無いため、Windows 側の `dotnet` を PowerShell 経由で呼び出す（`powershell.exe` 経由の実行を実機で確認済み）。
  - 共通準備: `WIN_REPO=$(wslpath -w "$PWD")`
  - Build: `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$WIN_REPO'; dotnet build"`
  - Test: `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$WIN_REPO'; dotnet test"`
  - Format: `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$WIN_REPO'; dotnet format"`
  - 動作確認例: `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "dotnet --version"`

## Coding Style & Naming Conventions
- Language: C# 12, .NET 8, Windows-only APIs allowed.
- Indentation: 4 spaces; max line length ~120.
- Naming: `PascalCase` for types/methods, `camelCase` for locals/fields, `I...` for interfaces.
- Analyzer rules: enable nullable, treat warnings as build warnings; keep public APIs documented.

## Testing Guidelines
- Framework: xUnit; place tests under `tests/DropSendTo.Tests` with matching namespaces.
- Coverage: Aim ≥ 80% for services (launch/config/validation).
- Naming: File `ClassName.Tests.cs`; method `Method_ShouldExpectedBehavior_WhenCondition`.
- Run locally on Windows; avoid tests requiring UI focus when possible.

## Commit & Pull Request Guidelines
- Commits: Conventional Commits (e.g., `feat(ui): add slot grid`).
- PRs: Include purpose, linked issues, Before/After, and screenshots/GIFs for UI.
- All checks must pass (`build`, `test`, `format`). Update docs with behavior changes.

## Security & Configuration
- No secrets in repo. Store user config at `%AppData%/DropSendTo/config.json`.
- Ignore `bin/`, `obj/`, `.vs/`. Keep dependencies minimal; verify licenses.

## Agent Notes (Codex CLI)
- Keep patches minimal and focused; avoid unrelated renames.
- Prefer `dotnet` CLI; avoid WSL paths for build/run.
