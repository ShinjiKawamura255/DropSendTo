# Repository Guidelines

## Project Structure & Module Organization
- `src/DropSendTo/`: Main .NET 8 Windows app (WPF/WinUI style).
- `tests/DropSendTo.Tests/`: Unit/integration tests mirroring `src` namespaces.
- `img/`: Icons・トレイ用リソース一式（過去の `assets/` は廃止済み）。
- `docs/`: REQUIREMENTS, SPEC, DESIGN, TESTPLAN and architecture notes.
- `scripts/`: PowerShell 自動化スクリプト（ビルド/配布/検証）。
- `dist/`: `scripts/Build-Release.ps1` が生成する配布成果物（`latest/` シンボリック コピー含む）。

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
- **PowerShell スクリプト**:
  - `scripts/Run-Tests-And-Build.ps1 [-Release] [-KillRunning] [-NoRestore]`
    - `-KillRunning` で常駐中の DropSendTo.exe を終了してから `dotnet restore/build/test` を実行。`-Release` 追加で Release ビルドを後続実行し、`test_results.trx` をルートに保存。
  - `scripts/Build-Release.ps1 [-Rid win-x64] [-Version vX.Y.Z] [-SelfContained] [-Portable] [-KillRunning] [-NoZip] [-InvariantGlobalization]`
    - 既定で Framework Dependent の単一ファイル配布を `dist/DropSendTo_<Rid>_<Version>/` に生成し、`dist/latest/` に最新コピーを展開。`-SelfContained` や `-Portable` で追加バリアントを作り、`USER_GUIDE.md` を成果物へ同梱。`-CertificatePath/-CertificatePassword` 指定で署名、`-NoZip` 無指定なら各バリアントを Zip 化する。
  - リリース工程では `dist/` 内を直接編集しない。必要に応じ `dist/latest/` の中身を配布用に再梱包する。

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
- UI 起点: `src/DropSendTo/MainWindow.xaml(.cs)`。スロット操作と Prefix 状態を制御し、マクロの呼び出しは `KeyboardMacroService` へ委譲。Slot Size／行列切替、タスクトレイ最小化、`ClipboardHistoryService.Instance.Initialize` のライフサイクル管理、`ConfigTransferService` を用いたエクスポート/インポートダイアログ、`WindowPlacementService` でウィンドウ位置を補正する。
- マクロ処理: `src/DropSendTo/Services/KeyboardMacroService.cs`。検索トークンは `RunMacroInternal`, `TryHandleMouseCommand`, `TryParseInt64OrWindowToken`。座標予約語の解決は `TryResolveWindowCoordinatePoint`/`*_ComponentToken`。`MacroRecordingService` → `MacroRecordingOptimizer` で録画イベントを `KEY/KEYDOWN/KEYUP` に最適化。
- クリップボード/引数展開: `ClipboardHistoryService` が `WM_CLIPBOARDUPDATE` を購読し、直近 20 個の履歴を `{clipboard}` / `{clipboard_args}` / `{clipboard_args:n}` 向けに保持。`LauncherService` + `ArgumentTemplateExpander` がドロップ/CLI パスと履歴を合成して `ProcessStartInfo` を生成し、失敗時はメッセージ文字列を返す。
- 設定 I/O/移行: `ConfigService` が JSON 読み書き、`.bak` バックアップ、バージョンアップマイグレーションを担当。`ConfigTransferService` は AES-GCM + PBKDF2（200k iterations）で暗号化したエクスポート payload を扱い、`PasswordPromptDialog` から渡されるパスワードで復号後に `AppConfig` へマッピングする。
- Config 項目を追加/変更したら、`ConfigTransferService` の Export/Import スナップショットと `tests/DropSendTo.Tests/ConfigTransferServiceTests.cs` を必ず更新し、バックアップ/エクスポートで設定が欠落しないようにする。
- レイヤー/ショートカット: `LayerManager` が 0〜3 のレイヤーインデックスを巡回し、`ShortcutService` が Prefix armed 状態と Prefix+Ctrl+N/P（任意設定）をディスパッチ。Prefix+Enter 後は矢印キー選択を `MainWindow` に送出して `_slotVisuals` を更新する。
- ウィンドウ配置: `ScreenBoundsResolver` がマルチモニターの DPI を考慮した `ScreenBounds` を求め、`WindowPlacementService.Clamp` が可視領域へ収める。座標に NaN/Infinity が来た場合も安全にデフォルトへ戻す。
- 代表テスト:
  - Config/設定系 → `tests/DropSendTo.Tests/ConfigServiceTests.cs`, `ConfigTransferServiceTests.cs`（暗号化 round-trip, パスワード誤り検証）
  - マクロ/ショートカット → `KeyboardMacroService*`（mouse/variable/key parsing）、`MacroRecordingOptimizerTests.cs`、`ShortcutServicePrefix*Tests.cs`（STA + Dispatcher 必須）
  - UI/ユーティリティ → `UiLayoutBudgetTests.cs`（XAML 定数のレイアウト保証）、`LayerManagerTests.cs`, `ScreenBoundsResolverTests.cs`, `WindowPlacementService` 系
- 新機能追加時は該当サービス配下のテストに追記しつつ、必要があれば `docs/` 配下の SPEC/DESIGN/TESTPLAN を更新する。STA 要求のテストは `Thread.SetApartmentState(ApartmentState.STA)` を忘れずに。

## Commit & Pull Request Guidelines
- Commits: Conventional Commits (e.g., `feat(ui): add slot grid`).
- 原則「1 対応＝1 コミット」。直前の修正が期待と異なり連続で手直しする場合は、直前コミットを amend して履歴をまとめる（不要な連番コミットを避ける）。
- PRs: Include purpose, linked issues, Before/After, and screenshots/GIFs for UI.
- All checks must pass (`build`, `test`, `format`). Update docs with behavior changes.
- ユーザー向けドキュメント（`USER_GUIDE.md`）は機能追加・仕様変更時に必ず更新し、必要ならリリースノートにも反映する。

## Security & Configuration
- No secrets in repo. Store user config at `%AppData%/DropSendTo/config.json`; `.bak` を自動生成するため Git へ持ち込まない。
- ログ出力は `%AppData%/DropSendTo/logs/app.log`（約 1MB 超でローテーション・7 日保持）。PII/認証情報が出力されないよう Logger 追加時は注意し、必要に応じてマスク。
- 設定エクスポート (`ConfigTransferService`) は AES-GCM + PBKDF2 200,000 iterations で暗号化するが、payload にはユーザスロット情報が丸ごと含まれるため共有時はパスワードの別チャネル連携・期限付き配布を徹底。Git/Issue などの恒久ストレージにパスワードと一緒に置かない。
- Ignore `bin/`, `obj`, `.vs/`, `dist/` 等のビルド成果物。Keep dependencies minimal; verify licenses.

## Agent Notes (Codex CLI)
- Keep patches minimal and focused; avoid unrelated renames.
- Prefer `dotnet` CLI; avoid WSL paths for build/run.

## UI ダイアログ方針
- メインウィンドウの操作をブロックしないよう、設定/メニューから起動するダイアログは `ShowDialog` を避けてモデルレスで開き、完了イベントや Task で結果を受け取る。Owner を設定しつつ非同期に閉じられる実装を徹底する。
- 背景色と文字色が同化して可読性を失わないよう、十分なコントラストを必ず確保する（暗背景なら前景は明色にするなど）。

## Macro Script Tips 更新ルール
- Macro Script の新規コマンド追加・挙動変更時は、`src/DropSendTo/MacroTipsWindow.xaml` の Tips に対応する説明と簡単な使用例を必ず追記する。利用者が UI 上で新コマンドに気付けるようにすること。
- Macro Script コマンドを追加したら、スニペット挿入メニュー（`src/DropSendTo/RegisterDialog.xaml.cs` の `MacroSnippetGroups`）にも適切なグループでエントリを追加すること。
- 新規コマンドを追加したら `KeyboardMacroService` の構文チェック（検証モード）にも必ず対応を入れ、変数が検証モードでも未定義扱いにならないようにする。合わせて単体テストと docs/SPEC.md にも反映する。
