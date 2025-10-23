# DESIGN

## DES-001 Architecture Overview
- Platform: .NET 8 / Windows デスクトップ（WPF）。単一プロセス常駐アプリ。
- Entry: `App` が起動時引数を評価し、CLI 処理成功時は UI を表示せずに終了。
- Persistence: `%AppData%/DropSendTo/config.json`（JSON + `.bak` バックアップ）。
- Logging: `%AppData%/DropSendTo/logs/app.log` にローテーション出力（1MB 超で世代化、7日保持）。

## DES-002 Components
- App: 例外ハンドラ登録、ログ初期化、CLI 引数処理、UI 起動制御を担う。
- MainWindow: 2〜4 行×2〜4 列のスロットグリッドを描画し、レイヤー切替・レイアウト変更・ドロップ/クリック/ショートカット起動・Prefix インジケーター（左上オーバーレイ表示）・設定保存を統括する。
- AppConfig / SlotModel: 設定スキーマ。バージョン管理、マクロスクリプト、クリック有効フラグ、常時最前面、位置、SlotRows/SlotColumns、ShortcutPrefix、各スロットの ShortcutKey を保持。
- ConfigService: JSON 読み書き、バリデーション、`.bak` バックアップ更新、バージョン 7 までのマイグレーションを実装し、行列分のスロット容量を保証する。
- LauncherService: `ArgumentTemplateExpander` を通じて `{args}`・`{clipboard}`・`{clipboard_args}` プレースホルダを展開し `ProcessStartInfo` を構築する。失敗時はメッセージ付きで返却。
- ArgumentTemplateExpander: 引数テンプレートを解析し、ドロップパスとクリップボードの文字列/パス展開を担当する純粋関数。
- KeyboardMacroService: 前面ウィンドウの変化をフックし、スクリプトをパースして SendInput API でキーストロークを送信。再入防止にセマフォを使用。
- ShortcutService: 低レベルキーボード/マウスフックで Prefix とスロットショートカットを検出し、Prefix インジケーター更新・Prefix パススルー・スロット起動をディスパッチする。
- KeyChordParser: `Ctrl+Shift+1` などのキー文字列を解析・正規化し、Prefix/ショートカット設定で利用する。
- RegisterDialog / PrefixDialog: スロット情報および Prefix の編集 UI。KeyChordParser で入力を検証し、正規化された結果を反映する。
- LoggerService: UTF-8 でログを追記し、1MB 超でタイムスタンプ付きへローテーション。7日より古いファイルを起動時に削除する。
- WindowPlacementService: 仮想スクリーン境界内にウィンドウ位置を収めるユーティリティ。

## DES-003 UI Flows
1) 起動: App が ConfigService で設定を読み込み SlotRows/SlotColumns を正規化、ログクリーンアップを実行。CLI 引数があれば優先スロットで LauncherService を呼び出し成功時は UI を表示せず終了。失敗または引数なしの場合は MainWindow を生成し、WindowPlacementService で位置を補正して表示。`SourceInitialized` で KeyboardMacroService と ShortcutService を初期化し、Prefix/ショートカット設定を登録する。
2) レイヤー切替: ボタン押下またはマウスホイールで `_currentLayer` を更新し、タイトルと UI を刷新。ドロップ中にレイヤーボタンへ 800ms 以上滞在した場合は DispatcherTimer で自動切替する。
3) ドロップ: Border の Drop イベントでファイルパス配列を取得。コマンド未設定なら情報ダイアログ、それ以外は LauncherService で `ArgumentsTemplate` を展開（`{args}` / `{clipboard}` / `{clipboard_args}`）して実行。失敗時はエラーダイアログ表示とログ出力。
4) スロットクリック: ClickEnabled が有効な場合、KeyboardMacroService により直前の外部ウィンドウへスクリプトを送信し、成功すれば LauncherService でコマンドを起動。マクロのみが設定されたスロットは逐次実行、コマンドのみのスロットはマクロ実行中でも即時に起動する。Any エラーはメッセージ表示でユーザーへ通知。
5) 登録/解除: スロット右クリック→ContextMenu から Edit/Clear/Click トグル。Edit ダイアログではタイトル/コマンド/引数テンプレート/マクロ/ショートカットを編集し、KeyChordParser でショートカット書式を検証・正規化。Macro Script 欄には `?` ボタンを配置し、クリックで MacroTipsWindow をモードレス表示（単一インスタンス再利用）してサポートされる命令と例を参照できる。保存後に ConfigService へ反映し UI を再描画。Clear は確認ダイアログ後に SlotModel を初期化する。
6) メニュー操作: メニューボタン/ウィンドウ右クリックで Open Config/Open Logs/Change Prefix/Slot Layout/常に最前面トグル/Exit を提供。Open Config/Logs は `Process.Start` with `UseShellExecute=true`。常に最前面トグルは Topmost と config を即時更新する。
7) レイアウト変更: Slot Layout サブメニューで行列を選択すると `_config.SlotRows/_config.SlotColumns` を更新し `ApplySlotLayout()` で UniformGrid を再生成、Window サイズとスロット数を再計算。設定保存後、全レイヤーのスロット数を行列分に揃える。
8) Prefix 変更: Change Prefix 選択で PrefixDialog を表示し、KeyChordParser で検証した結果を正規化して保存。ShortcutService に新しい Prefix を反映し、解析失敗時は Ctrl+Q を採用して MessageBox で通知する。
9) Prefix & グローバルショートカット: ShortcutService が低レベルフックで Prefix 入力を検出し、4 秒間 armed 状態を維持。armed 中に Prefix を再入力すると KeyboardMacroService 経由で前面ウィンドウへ送出し、以降の入力に MacroPassthrough タグを付けてショートカット検出を継続する。Prefix と同じ修飾キーを含むショートカットは修飾キーを押し直さずに検出し、必要に応じ再押下にも追従する。armed 中に登録済みショートカットを検出した場合は該当レイヤーへ切替後に `TriggerSlotAsync` を呼び出し、マクロ→コマンド順で実行。スリープ復帰やセッション切替では内部状態をリセットしてラッチを残さない。マクロ実行中は他スロットのマクロ起動をキャンセル・警告するが、コマンドのみのスロットはそのまま実行する。
10) 終了: Exit 選択またはウィンドウ閉鎖時に位置・レイヤー・常時最前面・行列設定を保存し、ShortcutService と KeyboardMacroService を破棄する。

## DES-004 API Contracts (examples)
- LauncherService
  - Input: `SlotModel slot`, `string[] paths`（0..n）。`ArgumentTemplateExpander` が `{args}` をクォート済みで展開し、`{clipboard}`/`{clipboard_args}` をクリップボード文字列から解決する。
  - Output: `LaunchResult`（Success/Message）。例外は捕捉してメッセージ化。
- ConfigService
  - `LoadOrCreate()`: JSON を読み込み、検証・マイグレーションを実行。失敗時は `.bak` または既定値にフォールバック。
  - `Save(AppConfig)`: バリデーション後、既存 `config.json` を `.bak` へコピーし、整形済み JSON を書き込む。
  - `GetConfigPath()`: Open Config 用の絶対パスを返す。
- KeyboardMacroService
  - `Initialize(WindowInteropHelper)`: フォアグラウンド変更フックを登録し、直近外部ウィンドウを追跡。
  - `RunMacroAsync(script)`: セマフォで逐次実行し、マクロスクリプトを解釈して SendInput を発行。結果に成功/失敗/スキップを含める。
  - SET/UNSET 命令で変数ディクショナリを管理し、各行のコマンド引数中に出現する `{{VarName}}` を解決してから `SendInput` やクリップボード処理を行う。ADD/SUB/MUL/DIV で 64bit 整数として演算し、APPEND/PREPEND で文字列結合を行う。`PREFIX [SEND|ARM|PASSTHROUGH]` で ShortcutService の Prefix 状態を操作し、MacroPassthrough 入力に切り替えてショートカット検出とアプリへの送出を両立する。未定義や書式不正は即座に失敗として復帰し、ログへ詳細を残す。
  - `Dispose()`: WinEventHook の解除・ロック解放。
- ShortcutService
  - `Initialize(string? prefixExpression)`: Prefix を解析・正規化し、低レベルフックをセットアップする。失敗時は既定 Prefix へフォールバックする。
  - `UpdatePrefix(string? prefixExpression)`: 実行中に Prefix を再解析し、armed 状態をリセットする。
  - `UpdateAvailableShortcuts(IEnumerable<string>)`: ショートカット文字列を解析・正規化して内部リストを更新する。不正値はログ警告。
  - `Dispose()`: キーボード/マウスフックを解除する。
- WindowPlacementService
  - `Clamp(left, top, bounds, width, height)` → 複数モニターを考慮した座標を返す。
- LoggerService
  - `Info/Warn/Error(string)`：レベル付きでログ追記。
  - `CleanupOldLogs()`: 起動時に呼び出し、古いファイルを削除。
  - `LogDirectory`: Open Logs メニュー向けに公開。

## DES-005 Errors/Timeout/Telemetry
- Errors: `LauncherService` は例外を捕捉してユーザー向けメッセージへ変換。`KeyboardMacroService` はターゲット取得失敗・未知コマンド・上限超過などを失敗として返す。`ShortcutService` は Prefix/ショートカット解析失敗を警告ログに記録し、Prefix 解析失敗時は既定値へフォールバックする。
- UI 通知: 失敗時は MessageBox で簡潔な文面を表示し、処理は継続。Prefix フォールバック時もメッセージで通知する。
- Logging: App 入口で未処理例外を捕捉し `ERROR` で記録。CLI 成功/失敗・マクロ開始/結果・スロットトリガー・コマンド起動・変数操作・設定読み込み失敗もロガーで記録する。ログは UTF-8 で 1 行 1 レコード。
- Telemetry: 専用メトリクスは未実装。必要な診断はログで代替。

## DES-006 Trade-offs
- WPF 採用で Windows 専用だが UI 制御が容易。WinUI3 への移行余地は保つ。
- マクロ送信は Win32 API（SendInput、SetWinEventHook）に依存し、UAC やフォーカス制御の制約を受けるが、他プロセスに依存せず完結する。
- グローバルショートカットは低レベルキーボード/マウスフックを使用するため管理者権限不要で常駐できるが、セキュリティソフトとの互換性や 4 秒タイムアウトなど UX 配慮が必要。

## Traceability (excerpt)
- DES-002 ← SP-001/002/006/009/010 → TC-010/025/065/080/085/086/087/090
- DES-003 ← SP-001/003/006/010 → TC-040/050/060/065/085/086/087
- DES-005 ← SP-004/007/010 → TC-030/035/095/085/086/087
