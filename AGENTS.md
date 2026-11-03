# Repository Guidelines

## Project Structure & Module Organization
- `src/DropSendTo/`: Main .NET 10 Windows app (WPF).
- `tests/DropSendTo.Tests/`: Unit/integration tests mirroring `src` namespaces.
- `img/`: Icons・トレイ用リソース一式（過去の `assets/` は廃止済み）。
- `docs/`: CURRENT_STATUS, REQUIREMENTS, SPEC, DESIGN, TESTPLAN and architecture notes.
- `scripts/`: PowerShell 自動化スクリプト（ビルド/配布/検証）。
- `dist/`: `scripts/Build-Release.ps1` が生成する配布成果物（`latest/` シンボリック コピー含む）。

## Build, Test, and Development Commands
- Build: `dotnet build` (Windows .NET 10 SDK; not WSL).
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
  - 各スクリプトは `DOTNET_EXE` が指定されていればその `dotnet.exe` を使い、未指定でも `%USERPROFILE%\.dotnet\dotnet.exe` があれば優先する。既定の `dotnet` が .NET 10 SDK を指さない環境では、`DOTNET_EXE` に使用する `dotnet.exe` のフルパスを指定する。
  - リリース工程では `dist/` 内を直接編集しない。必要に応じ `dist/latest/` の中身を配布用に再梱包する。

## Coding Style & Naming Conventions
- Language: C# 14, .NET 10, Windows-only APIs allowed.
- Indentation: 4 spaces; max line length ~120.
- Naming: `PascalCase` for types/methods, `camelCase` for locals/fields, `I...` for interfaces.
- Analyzer rules: enable nullable, treat warnings as build warnings; keep public APIs documented.

## Testing Guidelines
- Framework: xUnit; place tests under `tests/DropSendTo.Tests` with matching namespaces.
- Coverage: Aim ≥ 80% for services (launch/config/validation).
- Naming: File `ClassName.Tests.cs`; method `Method_ShouldExpectedBehavior_WhenCondition`.
- Run locally on Windows; avoid tests requiring UI focus when possible.

## Language / Localization
- UI 文言の既定は日本語とする。Language メニューで English を選択可能にし、新規 UI 要素を追加する場合は日英の文言を揃えること。

## Control Flow Cheat Sheet
- 全体設計は `docs/DESIGN.md`、API 仕様とコマンド一覧は `docs/SPEC.md`、試験観点は `docs/TESTPLAN.md` を参照。
- UI 起点: `src/DropSendTo/MainWindow.xaml(.cs)`。スロット操作と Prefix 状態を制御し、マクロの呼び出しは `KeyboardMacroService` へ委譲。Slot Size／行列切替（2〜8 行 × 2〜8 列）、レイヤー数変更（4〜8）、タスクトレイ最小化、`ClipboardHistoryService.Instance.Initialize` のライフサイクル管理、`ConfigTransferService` を用いたエクスポート/インポートダイアログ、`WindowPlacementService` でウィンドウ位置を補正する。
- ドロップ専用ウィンドウ: `src/DropSendTo/DropCaptureWindow.xaml(.cs)`。ドラッグ中ホイールクリックまたは Prefix+Ctrl+D で表示し、ドロップ済みパスを MainWindow 側の pending 状態へ引き渡す。左上 `Dropped` インジケーター点灯中のクリック/キーボード選択/検索起動で `{args}` 展開を有効にする。
- マクロ処理: `src/DropSendTo/Services/KeyboardMacroService.cs`。検索トークンは `RunMacroInternal`, `TryHandleMouseCommand`, `TryParseInt64OrWindowToken`。座標予約語の解決は `TryResolveWindowCoordinatePoint`/`*_ComponentToken`。`MacroRecordingService` → `MacroRecordingOptimizer` で録画イベントを `KEY/KEYDOWN/KEYUP` に最適化。
- クリップボード/引数展開: `ClipboardHistoryService` が `WM_CLIPBOARDUPDATE` を購読し、直近 20 個の履歴を `{clipboard}` / `{clipboard_args}` / `{clipboard_args:n}` 向けに保持。`LauncherService` + `ArgumentTemplateExpander` がドロップ/CLI パスと履歴を合成して `ProcessStartInfo` を生成し、失敗時はメッセージ文字列を返す。
- 設定 I/O/移行: `ConfigService` が JSON 読み書き、`.bak` バックアップ、バージョンアップマイグレーションを担当。`ConfigTransferService` は AES-GCM + PBKDF2（200k iterations）で暗号化したエクスポート payload を扱い、`PasswordPromptDialog` から渡されるパスワードで復号後に `AppConfig` へマッピングする。
- Config 項目を追加/変更したら、`ConfigTransferService` の Export/Import スナップショットと `tests/DropSendTo.Tests/ConfigTransferServiceTests.cs` を必ず更新し、バックアップ/エクスポートで設定が欠落しないようにする。
- レイヤー/ショートカット: `LayerManager` が `0..(Layers.Count-1)` のレイヤーインデックスを巡回し、`ShortcutService` が Prefix armed 状態をディスパッチ。Prefix+Enter 後は矢印キー選択を `MainWindow` に送出して `_slotVisuals` を更新する。
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

## 開発のこだわりを適用する場面

既存仕様・必須品質を満たす範囲で、依頼に関係する設計や実装の選択に次を活かす。新機能、全面整理、毎回の性能計測や追加レビューを一律に要求するものではない。具体的な挙動は [SPEC](docs/SPEC.md)、利用者の操作は [USER_GUIDE](USER_GUIDE.md) を正本とする。

- **操作を止めず、速く、非同期結果を正しく反映する。** 設定/メニューのダイアログは下記のモデルレス方針と既存の [ShowForResultAsync](src/DropSendTo/DialogExtensions.cs) を使い、時間のかかる処理の待機でメイン操作を止めない。対象・要求・キャンセル状態を対応付け、閉じた画面や変更済みの対象へ遅い結果を適用しない。変更対象の検索・マクロなどで余分な走査や待機を減らし、速さを主張するときは対象処理の計測で確かめる。速度のために検索条件や順位、同順位のレイヤー/スロット順を変えない（[FR-044](docs/REQUIREMENTS.md)）。
- **一貫性を保ち、ファイル・フォルダと責務を整理する。** クリック・キーボード・検索から同じスロットを起動するときは、引数や pending drop の引き渡し規則を揃え、消費タイミングを勝手に変えない。追加コードは画面・XAML・`Services`・`Models` など既存の責務に合う場所と対応するテストに置く。UI は画面状態とサービスを仲介し、検索・起動・設定の判断はそれを所有するサービスへ寄せる（[Control Flow Cheat Sheet](#control-flow-cheat-sheet)、[DESIGN](docs/DESIGN.md)）。小変更のために `MainWindow` 全体を分割したり、無関係なファイルを移動したりしない。日英UI、マクロTips・スニペット・検証・仕様の既存更新規則も維持する。
- **手数を減らし、キーボードで完結できる操作を充実させる。** 頻用フローでは選び直しや再入力を減らし、既存の Prefix → 選択/検索 → 実行に沿って、新しい主要操作のキー経路、取消、復帰、ヘルプでの発見しやすさを考える。最初の矢印選択前は Enter だけで起動しない規則、Prefix パススルー、リモートセッション優先や既存キー割当を守る。自動レイヤー切替は手動選択・一時固定・検索/ドラッグ/編集を奪わない（[FR-042](docs/REQUIREMENTS.md)、[AppLayerSelection](src/DropSendTo/Services/AppLayerSelection.cs)）。手数削減のために確認・入力検証を省いたり、ショートカットを全操作へ機械的に追加したりしない。

## UI ダイアログ方針
- メインウィンドウの操作をブロックしないよう、設定/メニューから起動するダイアログは `ShowDialog` を避けてモデルレスで開き、完了イベントや Task で結果を受け取る。Owner を設定しつつ非同期に閉じられる実装を徹底する。
- 背景色と文字色が同化して可読性を失わないよう、十分なコントラストを必ず確保する（暗背景なら前景は明色にするなど）。

## Macro Script Tips 更新ルール
- Macro Script の新規コマンド追加・挙動変更時は、`src/DropSendTo/MacroTipsWindow.xaml` の Tips に対応する説明と簡単な使用例を必ず追記する。利用者が UI 上で新コマンドに気付けるようにすること。
- Macro Script コマンドを追加したら、スニペット挿入メニュー（`src/DropSendTo/RegisterDialog.xaml.cs` の `MacroSnippetGroups`）にも適切なグループでエントリを追加すること。
- 新規コマンドを追加したら `KeyboardMacroService` の構文チェック（検証モード）にも必ず対応を入れ、変数が検証モードでも未定義扱いにならないようにする。合わせて単体テストと docs/SPEC.md にも反映する。
