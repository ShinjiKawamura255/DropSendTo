# DESIGN

## DES-001 Architecture Overview
- Platform: .NET 10 / Windows デスクトップ（WPF）。単一プロセス常駐アプリ。
- Entry: `App` が起動時引数を評価し、CLI 処理成功時は UI を表示せずに終了。
- Persistence: `%AppData%/DropSendTo/config.json`（JSON + `.bak` バックアップ）。
- Logging: `%AppData%/DropSendTo/logs/app.log` にローテーション出力（1MB 超で世代化、7日保持）。

## DES-002 Components
- App: 例外ハンドラ登録、ログ初期化、CLI 引数処理、UI 起動制御を担う。
- MainWindow: 2〜8 行×2〜8 列のスロットグリッドを描画し、レイヤー切替・レイアウト変更・ドロップ/クリック/ショートカット起動・Prefix インジケーター（左上オーバーレイ表示）・設定保存を統括する。Slot Size メニューで Small/Medium/Large/Custom を切り替え（既定は Medium）。Small=1 行（ステータスオーバーレイ）、Medium=2 行、Large=3 行のタイトル表示を実現し、Custom ではスロット高さ・フォントサイズ・余白・行列ステップを指定したメトリクスで再構成する。タスクトレイアイコンとのやり取りと最小化状態 (`_isMinimizedToTray`) を管理し、Prefix+Shift+Enter やメニュー操作でウィンドウを格納/復帰する。起動時は `RunOnStartup` が有効なスロットを順に実行する。マクロ実行状態は `SlotRunContext` のスタックで管理し、割り込み/一時停止モード時に UI へ「キャンセル中...」/「一時停止中...」を表示しつつ、復帰後は直前のスロット状態を再描画する。検索レイヤーでは `SlotSearchService` が返す順序付き検索結果を表示スロットへマッピングし、MainWindow 自体は検索オーバーレイ、フォーカス、キーボード移動、表示更新に責務を限定する。Slot Setup Mode はツールバーの ✎ ボタンで `_isSlotLayoutEditMode` を切り替え、左上に「SLOT SETUP MODE」オーバーレイを表示してクリック/ドロップ起動を抑止する。モード中は `_slotLayoutDragSourceLayer/index` を保持し、`DragDrop.DoDragDrop` に `SlotLayoutDragData`（レイヤー+スロット番号）を渡して UI ベースのドラッグ＆ドロップを実現。`SlotVisual.DragPreviewHost` を重ね描画してターゲットスロットのプレビューを元位置に表示し、`LayerBtn` 上のホバータイマーを流用して別レイヤーへの切替・スワップも同一操作で行う。ドロップ完了時にのみ `_config.Layers` のスロットを入れ替え `ConfigService` で保存する。Language メニューで日本語/English を切り替えた場合は `UiText` 辞書からメニューと検索ラベルの文言を即時反映し、選択を config に保持する。
- DropCaptureWindow: ドラッグ中のホイールクリックで表示するドロップ専用ウィンドウ。ファイル/フォルダのドロップを受け取り、MainWindow にドロップパスを通知してインジケーター表示と `{args}` 展開のための状態を更新する。
- Window position persistence: 固定位置モードかつユーザーによる移動時のみ座標を保存し、`_suppressFixedCapture`/`_suppressFixedCaptureDuringSearch`/`_suppressFixedCaptureFromTransientShow`/`_blockLocationSave` を使って一時配置（マウスフォロー、画面中央、検索レイヤー表示、ドラッグ中のホイールクリック表示など）では保存を抑止する。
- AppConfig / SlotModel: 設定スキーマ。バージョン管理、マクロスクリプト、クリック有効フラグ、常時最前面、位置、SlotRows/SlotColumns、ShortcutPrefix、各スロットの ShortcutKey、Language（既定=Japanese）を保持する。
- ConfigService: JSON 読み書き、バリデーション、`.bak` バックアップ更新、バージョン 18 以前からのマイグレーションを実装し（Language を日本語で初期化）、行列分のスロット容量を保証する。
- ClipboardHistoryService: `WM_CLIPBOARDUPDATE` を購読してテキスト履歴を最大 20 行まで保持し、`{clipboard_args}` 系プレースホルダのために直近コピー内容を分解・正規化する。
- LauncherService: `ArgumentTemplateExpander` を通じて `{args}`・`{clipboard}`・`{clipboard_args}`・`{clipboard_args:n}` プレースホルダを展開し `ProcessStartInfo` を構築する。失敗時はメッセージ付きで返却。
- ArgumentTemplateExpander: 引数テンプレートを解析し、ドロップパスと ClipboardHistoryService が提供する履歴を基に `{args}`/`{drop_args}`/`{drop_count}`/`{drop_path}`/`{drop_path:n}`/`{clipboard}`/`{clipboard_args}`/`{clipboard_args:n}` を展開する純粋関数。
- SlotSearchService: 検索レイヤー向けに全レイヤーを順序通り走査し、空スロットを除外した `SlotSearchResult` を生成する純粋サービス。検索対象は現行互換として `SlotModel.Title` と `SlotModel.SearchKeywords` のみとし、Command/Arguments/Macro Script は対象に含めない。検索語は空白区切りの AND 条件で、大小文字・全角/半角・濁点を正規化し、ハイフンと長音記号を検索語側で除去する。日本語スロット名はかな→ローマ字の候補も生成し、部分一致または subsequence で照合する。空クエリでは全レイヤーの非空スロットをレイヤー順・スロット順で返す。
- MacroQuotedTextReader: Macro Script の通常クォート文字列を読み取る純粋サービス。`\n`/`\r`/`\t`、escaped quote、バックスラッシュ直後の終端クォート、コメント直前の終端判定を共通化し、`KeyboardMacroService` と `MacroConditionEvaluator` のクォート解釈を一致させる。Windows パス引数用のクォート処理はバックスラッシュ意味論が異なるため `KeyboardMacroService` 側に残す。
- MacroConditionEvaluator: 変数展開後の Macro Script 条件式を token 化し、truthy 判定、比較演算子、`AND`/`OR`（`AND` 優先）を評価する純粋サービス。条件式内のクォート文字列は Macro Script と同等の escape 処理を行い、引用符内の `AND`/`OR`/`#` は構文要素として扱わない。
- KeyboardMacroService: 前面ウィンドウの変化をフックし、スクリプトをパースして SendInput API でキーストロークを送信。`MacroExecutionSession` と `_macroStack` で実行中のマクロ/キャンセレーショントークンを追跡し、`_suspensionStack` で一時停止中セッションを管理する。排他モードはセマフォで直列化し、割り込みモードは `CancelAllRunningMacrosAsync` で段階的にキャンセル、一時停止モードは `SuspendCurrentMacroAsync` が `MacroSuspensionHandle` を返して外部処理中は入力を停止、`DisposeAsync`（再開）時にセマフォを再取得して処理を続行する。文字列変形は `SET`/`APPEND`/`PREPEND` のほか Ordinal 置換用の `REPLACE <Var> "検索" "置換"`、拡張正規表現用の `REPLACE_REGEX <Var> "正規表現" "置換" [オプション]` をサポートしており、後者は .NET Regex の `IGNORECASE`/`MULTILINE` 等をオプション指定できる。ユーザー入力を受けたい場合は `PROMPT` でメッセージ付きダイアログを開き、初期値は変数展開後に渡し、`TIMEOUT` 指定時は一定時間で自動クローズしてタイムアウト値を採用する。検証モードはダイアログを出さず初期値のみ設定する。Macro Script 拡張では `COMMAND_APP` で一時的に実行ファイルを差し替えてから `COMMAND` を呼び出せる。条件分岐は `IF`/`ELSEIF`/`ELSE`/`ENDIF` を再帰的に処理する `Stack<IfBlockState>` で管理し、親ブロックが非アクティブな場合は子の条件式を評価せずスキップできるよう `inactiveIfDepth` カウンタでスキップ状態を追跡する。条件式は変数展開後に `MacroConditionEvaluator` へ委譲する。
- ShortcutRemoteSessionMatcher: リモートデスクトップ/Citrix 系のウィンドウクラス名・プロセス名を exact/wildcard で判定する純粋サービス。`ShortcutService` は Win32 から前景ウィンドウ、root owner、root のクラス名/プロセス名を取得し、判定のみをこのサービスへ委譲する。
- ShortcutService: 低レベルキーボード/マウスフックで Prefix とスロットショートカットを検出し、Prefix インジケーター更新・Prefix パススルー・スロット起動をディスパッチする。Prefix+Enter でウィンドウ復帰、Prefix+Alt+Space で検索レイヤーを開きつつ復帰、設定で有効化されている場合は Alt+Space など任意の検索ホットキーでも検索レイヤーを開く。Prefix+Shift+Enter でタスクトレイ最小化イベントを発火し、Prefix+Enter 後は MainWindow に矢印キー入力を通知してドロップシャドウによる選択状態を更新する。ショートカットの文字列は空白/カンマ/`>` 区切りで複数 KeyChord を表現でき、Prefix 後は 1 キーずつシーケンス状態を進める。部分一致中は入力を抑止し、候補がなくなるか特殊操作が押されると待機を解除する。
- WindowPlacementService: ScreenBoundsResolver が取得したモニターの作業領域を使い、保存済みのウィンドウ位置をタスクバー等の予約領域に重ならないよう Clamp する。NaN/Infinity が渡された場合は作業領域の左上へ戻す。
- KeyChordParser: `Ctrl+Shift+1` などのキー文字列を解析・正規化し、Prefix/ショートカット設定で利用する。
- RegisterDialog / PrefixDialog: スロット情報および Prefix の編集 UI。KeyChordParser で入力を検証し、正規化された結果を反映する。
- LoggerService: UTF-8 でログを追記し、1MB 超でタイムスタンプ付きへローテーション。7日より古いファイルを起動時に削除する。
- WindowPlacementService: 仮想スクリーン境界内にウィンドウ位置を収めるユーティリティ。

## DES-003 UI Flows
1) 起動: App が ConfigService で設定を読み込み SlotRows/SlotColumns を正規化、ログクリーンアップを実行。CLI 引数があれば優先スロットで LauncherService を呼び出し成功時は UI を表示せず終了。失敗または引数なしの場合は MainWindow を生成し、WindowPlacementService で位置を補正して表示。`SourceInitialized` で KeyboardMacroService と ShortcutService を初期化し、Prefix/ショートカット設定を登録する。読み込んだ `_config.Language` を基に `ApplyLanguageToUi` でメニュー/検索ラベルの文言を初期化し、起動時実行フラグが有効なスロットを順に起動する。起動時の挙動が「常にタスクトレイで起動」の場合は `Loaded` 後に `MinimizeWindowToTray` を呼び、前回状態を復元する場合は `LastWindowVisibility` が Tray なら同じ処理を行う。
2) レイヤー切替: ボタン押下またはマウスホイールで `_currentLayer` を更新し、タイトルと UI を刷新。ドロップ中にレイヤーボタンへ 800ms 以上滞在した場合は DispatcherTimer で自動切替する。
3) ドロップ: Border の Drop イベントでファイルパス配列を取得。コマンド未設定なら情報ダイアログ、`Macro Script` モードは引き続きドロップ禁止。`Macro Script 拡張` かつマクロ/コマンド両方が設定されている場合は `TriggerSlotAsync(..., SlotTriggerSource.Drop, paths)` を呼び出し、ドロップパスを `MacroExecutionContext.DroppedPaths` としてマクロへ渡す。マクロ内の `{{drop_args}}` / `{{drop_count}}` / `{{drop_path}}` / `{{drop_path:n}}` で参照でき、`COMMAND` から引数省略で呼び出した場合はドロップパス付きの `ArgumentsTemplate` が展開される。その他のモードでは LauncherService が ClipboardHistoryService から取得した履歴を含めて `ArgumentsTemplate` を展開（`{args}` / `{drop_args}` / `{drop_count}` / `{drop_path}` / `{drop_path:n}` / `{clipboard}` / `{clipboard_args}` / `{clipboard_args:n}`）し実行。失敗時はエラーダイアログ表示とログ出力。ドラッグ中のホイールクリック時は DropCaptureWindow を表示し、そこで受けたドロップパスを `_pendingDropPaths` として保持し、左上インジケーターを点灯させた上でメインウィンドウを表示する。
4) スロットクリック: ClickEnabled が有効な場合、モードに応じて実行する。`Macro Script`/`Macro Script 拡張` では KeyboardMacroService が直前の外部ウィンドウへスクリプトを送信し、拡張モードではマクロ内の `COMMAND` 命令から LauncherService を呼び出せる。`Command` モードはマクロを介さずに LauncherService を起動する。マクロ系スロットは逐次実行し、コマンドのみのスロットはマクロ実行中でも即時に起動できる。Any エラーはメッセージ表示でユーザーへ通知。
5) 登録/解除: スロット右クリック→ContextMenu から Edit/Clear/Click トグル。Edit ダイアログではモード選択 ComboBox で `Command` / `Macro Script` / `Macro Script 拡張` を切り替え、タイトル/コマンド/引数テンプレート/マクロ/ショートカットを編集する。KeyChordParser でショートカット書式を検証・正規化し、Macro Script 欄には `?` ボタンを配置して MacroTipsWindow をモードレス表示（単一インスタンス再利用）できる。保存後に ConfigService へ反映し UI を再描画。Clear は確認ダイアログ後に SlotModel を初期化する。
6) メニュー操作: メニューボタン/ウィンドウ右クリックで Open Config/Open Logs/Change Prefix/Slot Layout/Language/起動時のウィンドウ/常に最前面トグル/Exit を提供。Open Config/Logs は `Process.Start` with `UseShellExecute=true`。常に最前面トグルは Topmost と config を即時更新し、Language メニューでは Japanese/English を選択するとメニューと検索ラベルの文言を即時切り替えた上で `_config.Language` とメニューのチェック状態を保存する。起動時のウィンドウは「常に表示」「前回の状態を復元」「常にタスクトレイで起動」の 3 つから選択し、設定を即時保存する。
7) レイアウト変更: Slot Layout サブメニューで行列を選択すると `_config.SlotRows/_config.SlotColumns` を更新し `ApplySlotLayout()` で UniformGrid を再生成、Window サイズとスロット数を再計算。設定保存後、全レイヤーのスロット数を行列分に揃える。
8) Slot Layout Edit Mode: ツールバーの ✎ ボタンかメニューで `_isSlotLayoutEditMode` を ON にすると `EditModeIndicator` を表示し、クリック/ファイルドロップ起動を抑止する。スロット左ドラッグで `_slotLayoutDragSourceLayer/index` と `SlotLayoutDragData` を構築し、`DragDrop.DoDragDrop` を開始。`OnSlotDragEnter/Over` で `_slotLayoutPreview*` を更新し `SlotVisual.DragPreviewHost` にターゲット概要を描画する。ドラッグ中に LayerBtn に 0.8 秒ホバーすると `_hoverTargetLayer` を通じて `SetLayer` が呼ばれ別レイヤーへ切替。Drop イベントで `CompleteSlotSwap` が両レイヤーの `SlotModel` を入れ替え `ConfigService.Save`、キャンセル時はプレビューとドラッグ状態をクリアする。
9) Prefix 変更: Change Prefix 選択で PrefixDialog を表示し、KeyChordParser で検証した結果を正規化して保存。ShortcutService に新しい Prefix を反映し、解析失敗時は Ctrl+Q を採用して MessageBox で通知する。
10) Prefix & グローバルショートカット: ShortcutService が低レベルフックで Prefix 入力を検出し、4 秒間 armed 状態を維持。armed 中に Prefix を再入力すると KeyboardMacroService 経由で前面ウィンドウへ送出し、以降の入力に MacroPassthrough タグを付けてショートカット検出を継続する。armed 中に修飾なしの `Enter` を受け取った場合は MainWindow へ復帰イベントを通知し、ウィンドウを前面にアクティブ化する（常時最前面設定は変更しない）。Prefix と同じ修飾キーを含むショートカットは修飾キーを押し直さずに検出し、必要に応じ再押下にも追従する。armed 中に登録済みショートカットを検出した場合は該当レイヤーへ切替後に `TriggerSlotAsync` を呼び出し、マクロ→コマンド順で実行。スリープ復帰やセッション切替では内部状態をリセットしてラッチを残さない。マクロ実行中は他スロットのマクロ起動をキャンセル・警告するが、コマンドのみのスロットはそのまま実行する。
11) 終了: Exit 選択またはウィンドウ閉鎖時に位置・レイヤー・常時最前面・行列設定を保存し、ShortcutService と KeyboardMacroService を破棄する。
12) タスクトレイ最小化: `Minimize to Tray` メニューまたは Prefix+Shift+Enter を受けると `_isMinimizedToTray` を true に設定し `Hide()` でウィンドウを非表示にする。Prefix+Enter やタスクトレイアイコン左クリックで復帰要求が届いた場合は `Show()` と `WindowState=Normal` で再表示し `_isMinimizedToTray` を false に戻す。
13) キーボード操作トグル: コンテキストメニュー「キーボード操作」で `_config.EnableEmacsNavigation` / `_config.EnableViNavigation` を切り替え、ウィンドウアクティブ時のみ Emacs/vi ライクの移動キーを受け付ける。トグルは Enter/Ctrl+J/Ctrl+M でも確定し設定へ保存され、無効化時は矢印キーと Enter/Esc の基本操作のみ有効。

## DES-004 API Contracts (examples)
- LauncherService
  - Input: `SlotModel slot`, `string[] paths`（0..n）。`ArgumentTemplateExpander` が `{args}`/`{drop_args}`/`{drop_count}`/`{drop_path}`/`{drop_path:n}` をクォート済みで展開し、`{clipboard}`/`{clipboard_args}`/`{clipboard_args:n}` を ClipboardHistoryService が返す履歴から解決する（`{clipboard_args:n}` は直近 n 行を古い順で展開）。
  - Output: `LaunchResult`（Success/Message）。例外は捕捉してメッセージ化。
- ConfigService
  - `LoadOrCreate()`: JSON を読み込み、検証・マイグレーションを実行。失敗時は `.bak` または既定値にフォールバック。
  - `Save(AppConfig)`: バリデーション後、既存 `config.json` を `.bak` へコピーし、整形済み JSON を書き込む。
  - `GetConfigPath()`: Open Config 用の絶対パスを返す。
- KeyboardMacroService
  - `Initialize(WindowInteropHelper)`: フォアグラウンド変更フックを登録し、直近外部ウィンドウを追跡。
- `RunMacroAsync(script, context)`: セマフォで逐次実行し、マクロスクリプトを解釈して SendInput を発行。`context` は `Macro Script 拡張` モード時にコマンド起動デリゲートを渡し、マクロ内の `COMMAND` 命令から LauncherService を呼び出せる。結果に成功/失敗/スキップを含める。
- SET/UNSET 命令で変数ディクショナリを管理し、各行のコマンド引数中に出現する `{{VarName}}` を解決してから `SendInput` やクリップボード処理を行う。ADD/SUB/MUL/DIV で 64bit 整数として演算し、APPEND/PREPEND で文字列結合を行う。`PREFIX [SEND|ARM|PASSTHROUGH]` で ShortcutService の Prefix 状態を操作し、MacroPassthrough 入力に切り替えてショートカット検出とアプリへの送出を両立する。拡張モード専用の `COMMAND` 命令は入力バッファをフラッシュした後にコンテキスト経由の LauncherService を呼び出し、コンテキスト未提供時は失敗として復帰する。未定義や書式不正は即座に失敗として復帰し、ログへ詳細を残す。
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
- DES-002 ← SP-001/002/006/009/010 → TC-010/025/037/065/080/085/086/087/090/108/111/112/113/114
- DES-002 ← SP-002/004 → TC-073
- DES-003 ← SP-001/003/006/010/013 → TC-040/050/060/065/085/086/087/108/109/110
- DES-005 ← SP-004/007/010 → TC-030/035/095/085/086/087
