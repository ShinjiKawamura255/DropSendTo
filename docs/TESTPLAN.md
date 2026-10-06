# TESTPLAN

## Scope & Levels
- Scope: ランチャ挙動、登録/永続化、レイヤー・レイアウト切替、キーボードマクロ、Prefix ベースのグローバルショートカット、CLI フロー、ログアクセス、エラーハンドリング、UI 表示要件。
- Levels: unit（services）, integration（config+launcher+macro）, manual e2e（UI）。

## Framework & Runner
- Settings GUI probe: `scripts/Test-SettingsWindow.ps1`（Windows）。通常の単一起動処理を使わず実際の WPF スタイル・テーマを読み込み、隔離データ領域で保存・反映とモデルレス開閉を検証する。`TestResults/SettingsWindow/` に日英×明暗×通常/最小サイズの全カテゴリ画像とレポートを保存する。トレイの実クリック、物理キーボード操作、スタートアップ登録の変更、混在 DPI は別途実機で確認する。
- Unit/Integration: xUnit（`dotnet test`）。
- Manual: チェックリストで確認（スクショ取得）。

## Test Cases (excerpt)
- TC-044 SettingsWindow: `SettingsWindowTests` で初期化・再同期が変更通知を出さないこと、設定オブジェクト差し替えへの追従、選択変更の一度だけの反映、日英の全カテゴリ・詳細操作への到達、マウス／検索のキーボード追従、言語切替時のカテゴリ保持を確認する。実表示ではメニューとトレイからの起動・再選択時の単一インスタンス・最小化復元、詳細編集後の現在値、設定インポート後の同期、明暗テーマと日英、660×460 のスクロール、Tab/Space/矢印による操作、閉じた後も保存値が維持されることを確認する。スタートアップ登録の変更は隔離環境で実施する。
- TC-001 Platform: Windows の .NET 10 でビルド/実行できる。
- TC-010 Slots: 現在の行列構成（2〜8 × 2〜8）で全スロットの登録/解除/復元が可能で、タイトル/コマンド/引数/マクロ/ショートカット/クリック可否が保存される。
- TC-020 LaunchDrop: ファイル/フォルダのドロップで `{args}` がクォートされ、登録コマンドに渡る。
- TC-021 LaunchCli: CLI 引数で現在レイヤー優先→全レイヤー検索が行われる。失敗時は UI 表示とログ出力。
- TC-025 MacroScriptHappy: `KEY`/`WAIT`/`WAIT_UNTIL`/`TEXT`/`REPEAT`/`FOREACH_DROP` など代表的な命令が期待通り展開・送信され、複数ドロップ時に各パスへ `SET`/`REPLACE` を適用できる。
- TC-026 MacroScriptValidation: 不正なキー名/REPEAT 上限超過/未閉鎖ブロック/FOREACH_DROP の書式不正や未閉鎖でエラーが返る。
- TC-027 MacroScriptVariables: `SET`/`UNSET`/`ADD`/`SUB`/`MUL`/`DIV`/`APPEND`/`PREPEND` で変数を操作し、`{{Var}}` 展開が成功する。未定義・不正名・整数でない値・0 除算は失敗およびログ記録となる。
- TC-028 MacroScriptExtended: Macro Script 拡張モードでマクロが実行され、`COMMAND` 命令から登録済みコマンドが呼び出される。引数省略時はスロットの引数テンプレートが展開され、引数を指定した場合は変数展開後の文字列がそのまま渡される。さらにスロットへファイルをドロップした際はマクロ内で `{{drop_args}}` / `{{drop_count}}` / `{{drop_path}}` / `{{drop_path:n}}` が参照でき、`drop_path:n` の不正指定でマクロが失敗すること、加工後の文字列を `COMMAND` 引数に渡すと登録済みコマンドへそのまま引き継がれることを確認する。
- TC-029 MacroCommandValidation: `COMMAND` 命令を Macro Script 拡張モード以外で使用するとエラーとなり、ログに失敗理由が出力される。
- TC-030 Errors: 実行不可パスやマクロエラーでメッセージが表示され、ログに ERROR が残る。
- TC-126 ConfigAtomicPersistence: write/flush/replace の各失敗で既存 primary/backup が維持され、backup promotion 失敗時は旧 primary が復元されること、promotion と rollback の両方が失敗しても旧内容の recovery artifact が残ること、stale temp が次回保存前に除去されること、破損 primary を正常 backup から修復しても backup 自体を上書きしないことを `ConfigServiceTests` で確認する。
- TC-127 LoggingPrivacy: sentinel をコマンド、引数テンプレート、タイトル、drop path、マクロ変数、`RETURN` 値へ設定しても `LauncherService` / `KeyboardMacroService` の通常 capture log に現れないことを `LoggingPrivacyTests` で確認する。clipboard placeholder は同じ展開済み引数ログ経路を使うこと、MainWindow の統合ログも値を直接出さないことを静的監査する。OS/API 例外メッセージが残り得ることは共有時の確認対象とする。
- TC-128 ReleaseIdentity: `Test-Release-Version.ps1` の 9 ケースで clean exact tag、post-tag、dirty、タグなし、明示 override 等の識別子を検証する。CI が version test、format verify、test/build を実行し、生成した TRX が `.gitignore` 対象であることも確認する。
- TC-129 ConfigTransferCompleteness: `AppConfig`、`Layer`、`SlotModel`、`SlotMinimizeOptions` の公開永続プロパティが対応 snapshot に存在することを reflection inventory で保証する。さらに `CustomSlotSizeOptions` を含む全 writable property を再帰的に非既定の有効値へ設定し、暗号化 round-trip 後の完全 equivalence を確認する。未知の追加型に sentinel を生成できない場合は明示的に失敗させ、export/import いずれか片方向の代入漏れも検知する。旧 payload の既定値を確認し、package UTF-8 16 MiB、KDF 1,000,000、Salt/Nonce/Tag 固定長、CipherText 8 MiB の違反は復号前に拒否する。
- TC-130 ConfigImportTrustAndRuntime: 復号候補を `ConfigService.NormalizeForUse` で正規化し、コマンド/マクロ/起動時実行件数を日英のモデルレス確認画面に表示する。拒否時は無変更とする。承認後は Core/Theme/Shortcuts/MouseGestures/LayoutAndWindow/LanguageAndMenus の runtime 段階を適用してから候補を保存し、runtime 適用失敗時は disk を変更せず旧 runtime を復元する。候補保存失敗時は旧 disk/runtime を個別に復元し、各復元失敗を注入して日英メッセージが未復元対象を明示することを確認する。成功時は各段階が一度ずつ適用され、最後に候補が保存されることを確認する。
- TC-131 WarmStartup: Release 実行ファイルを既存 instance なし・隔離した空 data root で起動し、warm-up 1 回を除外して 5 回以上、process start から WPF process の input-idle までを測定する。各 sample、median、max を出力し `max < 1000ms` を要求する。解決済み config/log が一時 root 内にあること、子プロセスを必ず終了すること、実ユーザー config/log の hash・mtime が前後で不変であることも確認する。
- TC-031 MacroReplace: `REPLACE` コマンドで `{clipboard}` を変数へ取り込み、半角空白や特定文字列を `""` や別文字列へ置換できる。検索文字列が空のときはエラーになる。また `REPLACE_REGEX` で `IGNORECASE`/`MULTILINE` 等のオプションや `$1` を使用した正規表現置換が機能すること、無効なパターン/オプションでマクロが失敗することを確認する。
- TC-032 MacroConditional: `IF`/`ELSEIF`（`ELSE IF`）/`ELSE`/`ENDIF` で条件分岐を書き、`==`/`!=`/`>`/`<`/`>=`/`<=` の数値比較および `CONTAINS`/`NOTCONTAINS`/`STARTSWITH`/`ENDSWITH` の文字列比較が期待通り評価されること、`AND`/`OR` の結合が期待通り評価され（`AND` 優先）、`IF {{Flag}}` の真偽値評価（空/0/false が偽）が期待通りであること、`ELSE` を 2 回書くとエラーになること、`ELSEIF` が前段成立後は評価されず未定義変数でも失敗しないこと、親の `IF` が偽のとき内側の `IF` 条件式は評価されないこと、`ENDIF` が不足するとマクロ全体が失敗することを確認する。
- TC-112 MacroConditionEvaluator: 変数展開済みの条件式評価を単体テストし、比較演算子/別名、truthy 値、`AND` 優先 `OR`、UNC literal、escaped quote、末尾 backslash、未閉じ quote、引用符内の `AND`/`OR`/`#` が期待通り扱われることを確認する。
- TC-114 MacroQuotedTextReader: Macro Script の通常クォート文字列 reader を単体テストし、成功時 index、標準 escape、奇数/偶数 backslash + quote、コメント/EOL 直前の終端 quote、未閉じ quote のエラーを確認する。合わせて path 専用 quoted reader が末尾 backslash を含むパス引数を維持することを確認する。
- TC-033 MacroRename: `RENAME` でファイル/フォルダ名を変数展開後に変更でき、存在しない元パスやアクセス不可/重複等でマクロが失敗することを確認する。
- TC-034 MacroPrompt: `PROMPT <変数> "メッセージ" [DEFAULT "初期値"] [TIMEOUT <ms> "タイムアウト値"]` でメッセージと初期値が表示され、OK を押すと入力値が変数へ格納され `RETURN "{{変数}}"` 等で参照できる。DEFAULT 省略時は空文字が入ること、`TIMEOUT` 指定時に指定時間経過でタイムアウト値が格納されること、キャンセル時はマクロが失敗することを確認する。
- TC-038 MacroWaitUntil: `WAIT_UNTIL <条件> TIMEOUT <ms> [INTERVAL <ms>]` で条件成立時に次へ進むこと、`TIMEOUT` 未指定や範囲外値が構文エラーになること、条件不成立のままタイムアウトした場合にマクロが失敗することを確認する。
- TC-118 MacroSamples: `docs/MACRO_SAMPLES.md` の各サンプルが現行実装済みコマンドだけを使っており、構文チェック可能な形になっていることを確認する。
- TC-119 MacroDatePath: `DATE`/`TIME`/`NOW` の既定形式、`UTC`/`LOCAL`、`FORMAT`、`FILENAME`、無効書式を確認し、`PATH_*` が UNC、root、末尾 separator、拡張子なしを期待通り分解することを確認する。
- TC-120 MacroFileOps: `MKDIR` が既存ディレクトリで成功し同名ファイルで失敗すること、`COPY`/`MOVE` がファイル/ディレクトリを処理し、宛先存在・親ディレクトリ未存在・ワイルドカード・検証モード副作用なしを確認する。
- TC-121 MacroProcessCapture: `COMMAND_WAIT` と `RUN_CAPTURE` が shell 無効で起動し、作業ディレクトリ、timeout、終了コード、stdout/stderr、出力上限、検証モード副作用なし、stdout/stderr 非ログを確認する。
- TC-122 MacroTryCatch: `TRY`/`CATCH`/`ENDTRY` が実行時エラーを捕捉し、エラー変数を格納すること、構文エラーとキャンセルは捕捉しないこと、ネストと未閉鎖検証を確認する。
- TC-123 MacroWindow: `WINDOW_FIND` が title/class/process/pid で検索でき、複数一致は既定失敗、`INDEX` 指定で選択、`WINDOW_ACTIVATE` は成功確認できない場合に失敗し、検証モードで副作用がないことを確認する。
- TC-135 MacroDisplayMove: `MacroDisplayMoveTests` / `NativeMacroDisplayAdapterTests` / `KeyboardMacroServiceMouseCommandTests` で9点・オフセット・負座標・解像度・境界、番号選択、複製表示、未接続・不正領域・不正構文での停止、変数展開、命令ごとの領域取得、既存絶対移動経路との一致、検証モードで列挙/入力送信しないことを確認する。Windows 実機では「識別」の番号と対象ディスプレイを照合し、拡張表示・複製表示・異なる DPI・接続変更後にクリックを伴わない移動で確認する。Tips の説明・使用例と挿入メニューの2項目が表示され、スクロールして参照/挿入できることも確認する。
- TC-124 MacroLineSplit: `FOREACH_LINE` と `SPLIT` が CRLF/LF/CR、空要素の保持/除外、INDEX、上限超過、未閉鎖ブロック、ネストを期待通り扱うことを確認する。
- TC-036 MacroCommandApp: Macro Script 拡張モードで `COMMAND_APP <パス>` を指定すると以降の `COMMAND` の実行ファイルが差し替わり、`RESET` または `CLEAR` で元に戻ることを確認する。変数展開後のパスが空の場合は失敗することを併せて確認。
- TC-037 MacroWifiSsid: `WIFI_SSID <変数>` で接続中 SSID が変数へ格納され、未接続時は空文字が入ることを確認する。SSID バイト列の UTF-8 デコードと 32 バイト上限を単体テストし、OS 表示言語や `netsh` 出力形式へ依存しないことを確認する。
- TC-035 PrefixFallback: Change Prefix ダイアログの不正入力は保存前にエラー表示で拒否されることを確認する。既存またはインポート済み設定の Prefix が実行時に解析できない場合は Ctrl+Q へフォールバックし、ユーザー通知・警告ログが残ることを確認する。
- TC-113 ShortcutRemoteSessionMatcher: リモートセッション判定用のウィンドウクラス名・プロセス名について、全 exact 候補、wildcard 代表、大小文字差、null/空白、不一致ケースを単体テストで確認する。
- TC-115 ShortcutSequenceMatcher: 登録済みショートカット sequence の照合を単体テストし、単一 chord 完了、複数 chord の partial/completed、no-match 時の候補クリア、単一完了と partial が同時成立する場合の complete 優先、初回 chord の Prefix residue 利用、2 chord 目以降の residue 不使用、余分な修飾キー拒否を確認する。
- TC-116 ShortcutSpecialCommandResolver: Prefix 特殊操作の解決を単体テストし、Tab/Enter/Alt+Space/Alt+Enter/Shift+Enter/Ctrl+D、有効/無効フラグ、Alt+Space と Ctrl+D の residue 許可、Tab/Enter/Alt+Enter/Shift+Enter の residue 拒否、余分な修飾キー拒否を確認する。合わせて `ProcessKeyDown` の薄い統合テストで resolver 結果が `ShortcutAction` へ mapping されることを確認する。
- TC-117 ShortcutPresentationModeDetector: PowerPoint slideshow heuristic を単体テストし、PowerPoint process + slideshow class/title は true、非 PowerPoint process と通常 PowerPoint window は false になることを確認する。mouse gesture のプレゼン中抑止では show だけを抑止し hide を許可する policy が維持されることを差分確認する。
- TC-040 UiTheme: 常時最前面が既定で ON、黒基調・半透明・角丸・ボタンスタイルが維持される。
- TC-045 AlwaysOnTopToggle: 設定画面から常時最前面を切り替え、設定保存後も状態が復元される。
- TC-050 Layer: ボタン/ホイールの循環切替、ドラッグホバー 0.8s で自動切替、ハイライト更新。
- TC-060 WindowPos: 再起動で位置復元し、画面外に移動しても可視範囲へ補正される。
- TC-061 WindowPosTransient: ドラッグ中のホイールクリックでドロップ専用ウィンドウを表示し、ドロップ後にウィンドウを表示しても固定位置の保存値が更新されないことを確認する。
- TC-062 CursorPlacementCoordinates: `ScreenBoundsResolverTests` と `CursorWindowPlacementServiceTests` で 100/125/150/200% の倍率、負の座標、5 画面の列挙順反転、画面境界での補正、作業領域中央、ドラッグ時の上下選択を検証する。実機では倍率の異なる 4〜5 画面を左右・上下に配置し、各画面の中央・境界・タスクバー上で Prefix+Enter、検索、ドラッグ時の表示を確認する。表示中とトレイ格納中を往復し、カーソルの画面から別画面へ飛ばないこと、画面移動後も中央/16 DIP の余白が合うこと、固定位置の保存値が変わらないことを確認する。
- TC-065 LayoutMenu: 設定 → 表示・レイアウトで行/列（2〜8）を選択して適用すると即時 UI が再構成され、再起動後も構成が保持される。上限超過・下限未満を選択できないことを確認する。
- TC-066 SlotSize: 設定 → 表示・レイアウトのスロットサイズで 小/中/大/カスタム を切り替えるとウィンドウ寸法とスロット表示が即時更新される。Small/Medium/Large ではタイトル行数が 1/2/3 行に制限され、Small 時はステータスを同一行にオーバーレイ表示。Custom では高さ/フォント/余白/行列ステップを指定して保存し、再起動しても保持されることを確認する。
- TC-069 PrefixArrowSelect: Prefix+Enter でウィンドウを前面に復帰した後、矢印キーでスロットが順次選択され Enter で実行されること、矢印を押すまで選択枠が表示されず、Esc/マウス操作でキーボード選択モードが解除されることを確認する。
- TC-074 KeyboardModeToggle: 設定 → キーボード・マウスで Emacs/vi トグルを ON/OFF すると即時反映され、設定が保存・復元されることを確認する。ON 時は各モードのキー（Emacs: Ctrl+F/B/N/P/A/E/M/J, Alt+X, Ctrl+[→X / vi: h/j/k/l, :）がウィンドウアクティブ時のみ動作し、OFF 時は矢印キー+Enter/Esc のみが有効になる。設定画面内でキーボードフォーカスを移動し、Space でもチェックを切り替えられることを確認する。
- TC-070 SlotStates: hover/drag/クリックで視覚状態が変化する（UI レベル）。
- TC-071 SlotSetupMode: ツールバーの ✎/メニューで Slot Setup Mode を有効にすると左上インジケーターが「SLOT SETUP MODE」に変わりクリック/ファイルドロップが無効化されること、スロットをドラッグすると元位置にプレビューが表示されドロップ時のみ入れ替えが確定すること、ドラッグ中にレイヤーボタンへ 0.8s ホバーすると表示レイヤーが切り替わり別レイヤーとのスワップ/空スロットへの移動が行えることを確認する。
- TC-072 SlotCopyMoveVisible: Slot Layout を小さくした状態で Copy to/Move to を選択し、候補に現在の行列設定で表示可能なスロットのみが列挙され、表示範囲外の空スロットは選択肢に含まれないことを確認する。
- TC-073 StartupSlots: 起動時実行スロット一覧でチェックを保存し、再起動後に対象スロットが自動実行されること（最小化トリガーが適用されないこと）を確認する。
- TC-080 ClickExec: クリック実行が有効なときのみ起動する。Command モードはコマンドのみ、Macro Script モードはマクロのみ、Macro Script 拡張モードはマクロ内で `COMMAND` によりコマンドが起動する。無効設定は保存される。
- TC-085 ShortcutTrigger: Prefix 押下でインジケーターが点灯し、4 秒以内の登録ショートカットでスロットがレイヤー自動切替込みで起動する。Prefix 再入力で前面ウィンドウへ送出される。
- TC-086 ShortcutSharedModifier: Prefix と同じ修飾キーを含むショートカット（例: Prefix `Ctrl+Q` → `Ctrl+X`）は修飾キーを押し直さなくても発火し、再押下でも動作する。
- TC-088 MacroPrefixCommand: マクロスクリプトで `PREFIX ARM`（または SEND）を挟んだ後に `KEY` でショートカットが発動し、`PREFIX PASSTHROUGH` で前面アプリに送出される。
- TC-089 PrefixActivate: Prefix 待機中に `Enter` を押すと DropSendTo ウィンドウが前面へ復帰してアクティブ化され、常時最前面設定が変化しない。
- TC-099 PrefixSearch: Prefix 待機中に `Alt+Space` を押すとタスクトレイ格納中でも復帰し検索レイヤーが開いて検索入力へフォーカスすること、スロットのショートカット欄へ `Alt+Space` を含むキーを入力すると検証エラーで保存できないことを確認する。
- TC-100 PrefixSearchRestore: Prefix+Alt+Space で検索レイヤーを開いた状態から Esc（または Emacs ライク有効時の Ctrl+G）で閉じたとき、呼び出し直前がタスクトレイ格納なら再び最小化され、表示中にマウス追従などで移動した場合でも元の位置へ戻ることを確認する。
- TC-101 SearchHotkeyOption: 検索ホットキー設定ダイアログが既定で無効になっていること、注意書きに Prefix+Alt+Space でも呼び出せることと Alt+Space 競合の警告があることを確認する。Alt+Space 等を指定して有効化すると Prefix を押さずに検索レイヤーが開き、無効化すると元に戻ること、Prefix+Alt+Space が引き続き利用できることを確認する。
- TC-111 SlotSearchService: 検索サービスが空クエリで全レイヤーの非空スロットを順序保持して返し、空スロットを除外することを確認する。検索対象は `Title` と `SearchKeywords` のみで、Command/ArgumentsTemplate/KeyboardMacroScript にだけ含まれる語ではヒットしないことを確認する。複数 token AND、大小文字・全角/半角・濁点正規化、検索語側のハイフン/長音記号除去、かな→ローマ字（促音・拗音・長音含む）を単体テストで確認する。
- TC-108 LanguageSettings: 設定 → 一般・データ管理で Japanese/English を切り替えるとメニューと検索ラベルの文言が即時更新され、config に保存されることを確認する。既定は日本語で、再起動後も選択した言語が復元される。
- TC-109 DragDropCapture: ドラッグ中にホイールクリックでドロップ専用ウィンドウが表示され、ドロップ後に左上の Dropped インジケーターが表示されること、スロットのクリック/キーボード選択/検索起動で `{args}` に展開されることを確認する。
- TC-110 StartupWindowBehavior: 設定 → 起動・実行で「常に表示」「前回の状態を復元」「常にタスクトレイで起動」を切り替えられ、選択が保存・復元されることを確認する。`常にタスクトレイで起動` 選択時は再起動後にウィンドウがタスクトレイへ格納された状態で開始し、`前回の状態を復元` は最後に Tray だった場合のみ最小化で開始することを確認する。
- TC-125 StartupRegistration: メニューの「スタートアップに登録」を ON/OFF して HKCU の Run 値が作成/削除され、設定画面の再表示時に実状態がチェックへ反映されることを確認する。標準ユーザー権限で登録でき、登録値が引用符付きの現在の起動コマンドになることを確認する。サービスの登録/解除/Windows パス引用は `StartupRegistrationServiceTests` で検証する。
- TC-087 ClipboardArgs: `{clipboard}` と `{clipboard_args}` / `{clipboard_args:n}` がクリップボード文字列/パスを期待通り展開し、直近の指定行数のみが引用付きで渡される。
- TC-090 MenuAccess: 設定・一時固定・ショートカット一覧・最小化・終了が機能する。設定画面から設定ファイル/ログフォルダーを開き、Prefix/行列/最前面を変更できる。
- TC-095 LoggingRetention: current log は 1MB ちょうどでは維持し、1MB を超えた後の書込でローテーションする。同秒の反復 rotation と既存 archive 衝突では一意名を使い、rotation 失敗時も current log が書込可能なら今回のメッセージを失わない。UTC 最終更新が7日ちょうどの `app*.log` は保持し、7日を超えたものだけ削除することを `LoggerServiceTests` で確認する。
- TC-097 CommandOnlyParallel: マクロ実行中（排他/割り込み/一時停止いずれのモードでも）でもコマンドのみのスロットはショートカット/クリックから並列に実行でき、ログには「Command-only slot triggered while macro is active」が記録される。
- TC-102 MacroModeExclusive: モード=排他のとき、実行中スロットを再トリガーするとキャンセルが発行され UI が「キャンセル中...」表示になる。他スロットからの実行は警告ダイアログで拒否され、設定を切り替えても再起動後に保持される。
- TC-103 MacroModeInterrupt: モード=割り込み実行のとき、新規マクロ要求で実行中マクロがキャンセルされ、ログに interrupt が記録されてから新しいマクロが開始される。旧マクロが完全に停止するまで新マクロが開始されないことを確認。
- TC-104 MacroModeSuspendResume: モード=一時停止して割り込みのとき、実行中マクロが安全ポイントで停止しスロットが「一時停止中...」表示になる。新規マクロ完了後に自動再開し、状態表示が「実行中」に戻る。多段ネストでも LIFO 順で再開されることを確認。
- TC-098 MinimizeToTray: `Minimize to Tray` メニューおよび Prefix+Shift+Enter でウィンドウがタスクトレイへ格納され、Prefix+Enter やタスクトレイアイコン左クリックで復帰する。

- TC-132 AppLayerPolicy: `AppLayerSelectionTests` で優先順位、AND条件、同一アプリ復帰、手動選択と共通タブ、保留解除、一時固定、ショートカット表示切替方針、削除済みタブの無効化を確認する。`ConfigTransferServiceTests` の全永続項目guardと旧payload既定値で転送互換性を確認する。
- TC-133 AppLayerMonitor: `ActiveWindowMonitorTests` の制御可能な読取で、処理中の新規通知、古い応答の破棄と最新要求への集約、所有ウィンドウ除外、初期化失敗時cleanup、処理中終了のキャンセル・冪等cleanup・終了時間を確認する。
- TC-134 AppLayerGui: Windowsで通常設定とは別のdata rootを使用し、設定画面の日英・明暗テーマ・最小サイズ・スクロール・エラー・キャンセルを実表示で確認する。テスト用外部アプリを使い、前面切替と同じウィンドウのタイトル変更、手動切替後の復帰、固定解除、検索／ドラッグ／編集後の保留解除を確認する。別タブのショートカットが表示を変えず動作すること、OFF時の従来切替、設定再読込と削減後の無効ルールも確認する。

## Execution
- Unit: `dotnet test tests/DropSendTo.Tests -c Release`。
- Release/CI contract: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Release-Version.ps1`、`dotnet format --verify-no-changes`、Release test/build を実行する。
- Warm-start: Release build 後に `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Measure-WarmStartup.ps1` を実行し、TC-131 の隔離・不変・閾値を検証する。
- Manual: 手順
  1) アプリ起動→常時最前面・半透明・角丸・ボタンスタイルを確認。
  2) スロットを Edit で登録（Browse 使用、タイトル反映、ショートカットとマクロ設定）→再起動後も設定/ショートカット/クリック有効が保持されることを確認。
  3) ファイル/フォルダをドロップして `{args}` が正しく渡ることを確認。
  4) CLI 引数で起動し、優先スロットが実行され UI が終了することを確認。登録なしの場合は UI 継続とメッセージ表示。
  5) マクロスクリプト（例: `KEY Ctrl+C` → `WAIT 200` → `TEXT processed`）が直前ウィンドウへ送信された後にコマンドが実行されることを確認し、マウスコマンド（例: `MOUSEMOVEABS 200 300` → `MOUSELEFTCLICK` → `MOUSESCROLLDOWN 2`）およびアクティブウィンドウ予約語（例: `MOUSEMOVEABS WIN_TOPRIGHT` → `MOUSELEFTCLICK`）も意図した操作になることを確認。予約語の `_X` / `_Y` サフィックスで座標成分を取得し、`SET BaseX WIN_TOPLEFT_X` → `SET BaseY WIN_TOPLEFT_Y` → `ADD BaseX 40` → `ADD BaseY 20` → `MOUSEMOVEWIN {{BaseX}} {{BaseY}}` のようにオフセットした座標へ移動できることも確認する。`SET Count 0` → `ADD Count 1` → `TEXT {{Count}}` や `SET Message Hello` → `APPEND Message !` → `TEXT {{Message}}` を実行して演算結果が展開されること、0 除算などはエラーになることを確認。Macro Script 欄の「?」ボタンで Tips を開き、ダイアログ操作を継続しながら内容を参照できることを確認。
  6) Macro Script モード右側の「記録開始」→「記録停止」でキーボード・マウス操作がリアルタイムに Macro Script に追記され、ダイアログ上の操作（記録開始/停止のクリック等）は無視されること、記録終了時に追加行数がステータスへ表示されることを確認。
  7) 常に最前面トグルを OFF/ON し、切替直後と再起動後の状態を確認。
  8) レイヤーボタン/ホイールで循環切替し、ドラッグ中 0.8s で自動切替されることを確認。
  9) 設定 → 表示・レイアウトで別の行列（例: 3x3）を選択・適用し、即時に UI が再構成され再起動後も構成が保持されることを確認。元のレイアウトへ戻す。
 10) ウィンドウ位置を移動→再起動後に復元。画面外に移動しても補正されることを確認。
 11) スロット hover/drag/クリックの視覚変化を確認し、クリック有効/無効トグルが尊重されることを確認。
 12) メニューの「設定…」から 5 カテゴリを開けること、設定ファイル・ログ・Prefix・行列・スタートアップ登録が適切なカテゴリにあることを確認する。スタートアップ登録は隔離環境で ON/OFF とサインイン後の起動を確認する。
 13) Prefix（例: Ctrl+Q）を押下して左上インジケーター点灯→修飾キーを押し直さずに `X` を押し `Ctrl+X` ショートカットが起動すること、必要に応じ修飾キーを離して押し直しても動作すること、Prefix 再入力で前面ウィンドウへ送出されることを確認する。Prefix 変更ダイアログの不正入力はエラー表示後も保存されないこと、別途不正な保存値を読み込ませた場合だけ既定値へフォールバックして通知・警告ログが残ることを確認する。さらにマクロスクリプトに `PREFIX ARM` → `KEY X` → `PREFIX PASSTHROUGH` を記述し、Prefix 待機の擬似入力からショートカット発動→前面アプリ送出まで自動化できること、ログにマクロ PREFIX 操作が記録されることを確認。Prefix 待機中に `Enter` を入力すると DropSendTo ウィンドウが前面に復帰し（タスクトレイ格納時も復帰）、常時最前面設定が変化しないことも確認する。
 14) Prefix 待機中に `Shift+Enter` を押してウィンドウがタスクトレイへ最小化されること、タスクトレイアイコンの左クリックでウィンドウが復帰すること、`Minimize to Tray` メニューからも同じ結果になることを確認する。
 15) エクスプローラーでファイル/フォルダを複数コピーし、`ArgumentsTemplate` に `{clipboard_args}` を指定したスロットをショートカット起動して全行が引用付きで渡されること、`{clipboard_args:2}` 指定で直近 2 行のみが古い順に渡されること、および `{clipboard}` 指定で生文字列が渡されることを確認。
 16) `%AppData%/DropSendTo/logs` にテスト用ログを作成し、1MB 超でローテーションすることと、7 日より古いファイルが `CleanupOldLogs` 後に削除されることを確認（ファイルの最終更新日時を調整して検証）。
 17) マクロ付きスロットを起動（長めの WAIT を含める）し、その実行中に別スロットのマクロ起動を試みて警告が表示されること、同じ状態でコマンドのみスロットを起動すると即時でコマンドが実行されること、およびログに並列許可の記録が残ることを確認。
 18) Prefix 待機中に `Alt+Space` を押して DropSendTo が前面復帰し検索レイヤーが開いて検索入力へフォーカスされることを確認する。スロット編集ダイアログのショートカット欄に `Alt+Space` を入力した場合は検証エラーで保存できないことを併せて確認する。
 19) Prefix+Alt+Space で検索レイヤーを開いた後に Esc（または Emacs ライク有効時の Ctrl+G）で閉じ、呼び出し直前がタスクトレイ格納なら再最小化され、表示中に移動していた場合は元の位置へ戻ることを確認する。
 20) 設定 → キーボード・マウスの「検索ホットキー…」でダイアログを開き、既定が無効であることと注意書きを確認する。Alt+Space などを設定して有効化し、Prefix を押さずに検索レイヤーが開くこと、無効化後は呼び出しできないこと、Prefix+Alt+Space は引き続き動作することを確認する。
 21) 固定位置モードでファイルをドラッグ中にホイールクリックでウィンドウを表示し、次回の復帰や再起動後も固定位置が更新されていないことを確認する。
 22) ファイルをドラッグ中にホイールクリックでドロップ専用ウィンドウが表示されること、ドロップ後に Dropped インジケーターが表示され、スロットのクリック/矢印+Enter/検索から起動すると `{args}` に展開されることを確認する。

## Environment & Data
- OS: Windows 10 22H2+ / 11、.NET SDK 10.x。
- Data: 一時フォルダにテストファイル作成。unit test は明示した一時 base directory を使用する。TC-131/134 は測定 marker と絶対 data root を子プロセスへ渡し、通常の `%AppData%/DropSendTo` を変更しない。
- Macro: マクロ試験では同一デスクトップの通常権限ウィンドウ（例: メモ帳）を事前にフォーカスしておく。

## Entry/Exit Criteria
- Entry: 主要仕様の実装、ログ/設定ディレクトリ作成可能。
- Exit: 重要 TC（010/020/021/025/030/035/040/045/065/085/086/087/095/119〜131）合格、回帰なし。

## Reporting
- 必要時は `dotnet test --results-directory TestResults -l "trx;LogFileName=test_results.trx"` で追跡対象外の場所へレポート出力する。

## Traceability (excerpt)
- FR-001 → SP-001 → DES-002/003 → TC-010/065
- FR-002 → SP-004 → DES-004 → TC-020/021
- FR-004 → SP-005 → DES-002/004/005 → TC-126/129/130
- FR-006 → SP-007/015 → DES-005 → TC-030/127
- FR-019 → SP-004/009 → DES-002/004 → TC-025/027/080/112/114
- FR-019 → SP-009 → DES-004 → TC-135
- FR-021 → SP-006/007 → DES-002/005 → TC-090/095
- FR-022 → SP-001/006 → DES-002/003 → TC-065
- FR-023 → SP-010 → DES-002/003 → TC-085/086/088/089/099/100/101/111/113/115/116/117
- FR-024 → SP-010 → DES-002/005 → TC-035/085/086/088/089/099/100/101
- FR-025 → SP-004/010 → DES-002/004 → TC-025/087
- FR-019 → SP-009 → DES-003/004 → TC-037/080/097
- FR-026 → SP-001 → DES-002 → TC-098
- FR-032 → SP-010 → DES-002/003 → TC-074
- FR-033 → SP-009 → DES-002/004 → TC-034
- FR-034 → SP-009 → DES-002/004 → TC-036
- FR-035 → SP-006 → DES-003 → TC-108
- FR-036 → SP-013 → DES-003 → TC-109
- FR-037 → SP-002/004 → DES-002 → TC-073
- FR-038 → SP-005/006 → DES-002/003 → TC-110
- FR-042 → SP-016 → DES-007 → TC-132/133/134
- NFR-001 → SP-008 → DES-001/005 → TC-001/128
- NFR-002 → SP-008 → DES-001/005 → TC-131
- NFR-003 → SP-001 → DES-003 → TC-040/045

## TC-136/137 Update Check and Release Identity
- TC-136 (SP-017 / DES-008): `UpdateCheckServiceTests` で版の数値比較、開発版・試験版、404、通信失敗、タイムアウト、レート制限、不正応答、呼び出し側キャンセル、固定 URL、認証なしのリクエスト、埋め込みバージョン取得を検証する。`AboutWindowTests` で日英文言、状態ごとのボタン表示、失敗後の再確認、確認中の重複抑止・画面終了を検証する。
- TC-136 manual: メインとトレイのメニューから表示でき、再操作で重複画面を作らず前面へ戻ること、通信中もメイン操作と閉じる操作ができることを確認する。日英・明暗テーマで文字/ボタンのコントラスト、折り返し、Tab/Enter/Esc を確認する。新版のボタンが該当 GitHub ページを開くこと、通信失敗時に再確認できること、通常ビルドが開発版として表示されることを確認する。
- TC-137 (SP-017 / DES-008): `scripts/Test-Release-Version.ps1` で既存の識別子生成、配布用埋め込みプロパティ、正式版タグ形式、一致/不一致、追跡/未追跡変更の拒否を検証する。正式公開前の配布物起動・バージョン一致・旧版/新版からの更新確認は [RELEASE.md](RELEASE.md) の手順で実施する。
- Trace: FR-043 → SP-017 → DES-008 → TC-136/137。
