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
  - 注意: Codex 環境では `powershell.exe` による初回呼び出し時に `Permission denied` が返る場合があります。Codex 側のアクセス制御が原因のため、同様のエラーが出た場合はユーザに実行許可を求めてから再実行してください。

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

## Control Flow Cheat Sheet
- 全体設計は `docs/DESIGN.md`、API 仕様とコマンド一覧は `docs/SPEC.md`、試験観点は `docs/TESTPLAN.md` を参照。
- UI 起点: `src/DropSendTo/MainWindow.xaml(.cs)`。スロット操作と Prefix 状態を制御し、マクロの呼び出しは `KeyboardMacroService` へ委譲。
- マクロ処理: `src/DropSendTo/Services/KeyboardMacroService.cs`。検索トークンは `RunMacroInternal`, `TryHandleMouseCommand`, `TryParseInt64OrWindowToken`。座標予約語の解決は `TryResolveWindowCoordinatePoint`/`*_ComponentToken`。
- 設定 I/O: `src/DropSendTo/Services/ConfigService.cs` が JSON を読み書きし、`SlotModel` がデータ構造を保持。
- 代表テスト:  
  - マクロ入力系 → `tests/DropSendTo.Tests/KeyboardMacroServiceMouseCommandTests.cs` / `KeyboardMacroServiceVariableTests.cs`  
  - 設定永続化 → `tests/DropSendTo.Tests/ConfigServiceTests.cs`  
  - UI ロジック → `tests/DropSendTo.Tests/MainWindow*` 系
- 新機能追加時は上記テストに追記しつつ、必要があれば `docs/` 配下の SPEC/DESIGN/TESTPLAN を更新する。

## Commit & Pull Request Guidelines
- Commits: Conventional Commits (e.g., `feat(ui): add slot grid`).
- PRs: Include purpose, linked issues, Before/After, and screenshots/GIFs for UI.
- All checks must pass (`build`, `test`, `format`). Update docs with behavior changes.
- ユーザー向けドキュメント（`USER_GUIDE.md`）は機能追加・仕様変更時に必ず更新し、必要ならリリースノートにも反映する。

## Security & Configuration
- No secrets in repo. Store user config at `%AppData%/DropSendTo/config.json`.
- Ignore `bin/`, `obj/`, `.vs/`. Keep dependencies minimal; verify licenses.

## Agent Notes (Codex CLI)
- Keep patches minimal and focused; avoid unrelated renames.
- Prefer `dotnet` CLI; avoid WSL paths for build/run.
