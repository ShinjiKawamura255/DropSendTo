# DESIGN

## DES-001 Architecture Overview
- Platform: .NET 8 / Windows デスクトップ（WPF）。単一プロセス常駐アプリ。
- Entry: `App` が起動時引数を評価し、CLI 処理成功時は UI を表示せずに終了。
- Persistence: `%AppData%/DropSendTo/config.json`（JSON + `.bak` バックアップ）。
- Logging: `%AppData%/DropSendTo/logs/app.log` にローテーション出力（1MB 超で世代化、7日保持）。

## DES-002 Components
- App: 例外ハンドラ登録、ログ初期化、CLI 引数処理、UI 起動制御を担う。
- MainWindow: 2x2 スロット UI、レイヤー切替、ドロップ/クリック/メニュー操作、設定保存を統括する。
- AppConfig / SlotModel: 設定スキーマ。バージョン管理、マクロスクリプト、クリック有効フラグ、常時最前面、位置などを保持。
- ConfigService: JSON 読み書き、バリデーション、`.bak` バックアップ更新、バージョン 4 までのマイグレーションを実装。
- LauncherService: `{args}` プレースホルダを展開して `ProcessStartInfo` を構築し、シェル実行する。失敗時はメッセージ付きで返却。
- KeyboardMacroService: 前面ウィンドウの変化をフックし、スクリプトをパースして SendInput API でキーストロークを送信。再入防止にセマフォを使用。
- LoggerService: UTF-8 でログを追記し、1MB 超でタイムスタンプ付きへローテーション。7日より古いファイルを起動時に削除する。
- WindowPlacementService: 仮想スクリーン境界内にウィンドウ位置を収めるユーティリティ。

## DES-003 UI Flows
1) 起動: App が ConfigService で設定を読み込み、ログクリーンアップを実行。CLI 引数があれば優先スロットで LauncherService を呼び出し、成功時は UI を表示せず終了。失敗または引数なしの場合は MainWindow を生成し、WindowPlacementService で位置を補正して表示する。
2) レイヤー切替: ボタン押下またはマウスホイールで `_currentLayer` を更新し、タイトルと UI を刷新。ドロップ中にレイヤーボタンへ 800ms 以上滞在した場合は DispatcherTimer で自動切替する。
3) ドロップ: Border の Drop イベントでファイルパス配列を取得。コマンド未設定なら情報ダイアログ、それ以外は LauncherService で `{args}` を展開し実行。失敗時はエラーダイアログ表示とログ出力。
4) スロットクリック: ClickEnabled が有効な場合、KeyboardMacroService により直前の外部ウィンドウへスクリプトを送信し、成功すれば LauncherService でコマンドを起動。Any エラーはメッセージ表示でユーザーへ通知。
5) 登録/解除: スロット右クリック→ContextMenu から Edit/Clear/Click トグル。Edit ダイアログはコマンドまたはマクロのいずれかが必須。保存すると config を更新し UI を再描画。Clear は確認ダイアログ後に SlotModel を初期化する。
6) メニュー操作: メニューボタン/ウィンドウ右クリックで Open Config/Open Logs/常に最前面トグル/Exit を提供。Open Config/Logs は `Process.Start` with `UseShellExecute=true`。常に最前面トグルは Topmost と config を即時更新する。
7) 終了: Exit 選択またはウィンドウ閉鎖時に位置・レイヤー・常時最前面を保存し、KeyboardMacroService を破棄する。

## DES-004 API Contracts (examples)
- LauncherService
  - Input: `SlotModel slot`, `string[] paths`（0..n）。`BuildArguments` が `{args}` をクォート済みで展開。
  - Output: `LaunchResult`（Success/Message）。例外は捕捉してメッセージ化。
- ConfigService
  - `LoadOrCreate()`: JSON を読み込み、検証・マイグレーションを実行。失敗時は `.bak` または既定値にフォールバック。
  - `Save(AppConfig)`: バリデーション後、既存 `config.json` を `.bak` へコピーし、整形済み JSON を書き込む。
  - `GetConfigPath()`: Open Config 用の絶対パスを返す。
- KeyboardMacroService
  - `Initialize(WindowInteropHelper)`: フォアグラウンド変更フックを登録し、直近外部ウィンドウを追跡。
  - `RunMacroAsync(script)`: セマフォで逐次実行し、マクロスクリプトを解釈して SendInput を発行。結果に成功/失敗/スキップを含める。
  - `Dispose()`: WinEventHook の解除・ロック解放。
- WindowPlacementService
  - `Clamp(left, top, bounds, width, height)` → 複数モニターを考慮した座標を返す。
- LoggerService
  - `Info/Warn/Error(string)`：レベル付きでログ追記。
  - `CleanupOldLogs()`: 起動時に呼び出し、古いファイルを削除。
  - `LogDirectory`: Open Logs メニュー向けに公開。

## DES-005 Errors/Timeout/Telemetry
- Errors: `LauncherService` は例外を捕捉してユーザー向けメッセージへ変換。`KeyboardMacroService` はターゲット取得失敗・未知コマンド・上限超過などを失敗として返す。
- UI 通知: 失敗時は MessageBox で簡潔な文面を表示し、処理は継続。
- Logging: App 入口で未処理例外を捕捉し `ERROR` で記録。CLI 成功/失敗・マクロエラー・設定読み込み失敗もロガーで記録する。ログは UTF-8 で 1 行 1 レコード。
- Telemetry: 専用メトリクスは未実装。必要な診断はログで代替。

## DES-006 Trade-offs
- WPF 採用で Windows 専用だが UI 制御が容易。WinUI3 への移行余地は保つ。
- マクロ送信は Win32 API（SendInput、SetWinEventHook）に依存し、UAC やフォーカス制御の制約を受けるが、他プロセスに依存せず完結する。

## Traceability (excerpt)
- DES-002 ← SP-001/002/006/009 → TC-010/025/080/090
- DES-003 ← SP-001/003 → TC-040/050/060
- DES-005 ← SP-004/007 → TC-030/095
