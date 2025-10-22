# SPEC

## SP-001 UI/Window
- MUST: スロットグリッドは設定で決まる行列（2〜4 行 × 2〜4 列）を使用し、既定値は 2x2。常に 4 レイヤーを切り替えて表示する。
- MUST: 既定で常時最前面、黒基調、ウィンドウ全体は 20–40% 透過。テキストは不透明で可読。
- MUST: タイトルバー/システムボタンを非表示。終了はメニューボタンまたはウィンドウコンテキストメニュー経由のみ。
- MUST: ウィンドウは角丸。メニュー/レイヤーボタンはスロットと統一したスタイル（暗色、角丸、ホバー/アクティブ表現）。
- MUST: 常に最前面の ON/OFF をメニューで切り替えた際は直ちに反映され、設定が保存される。
- SHOULD: ウィンドウタイトルに現在のレイヤー番号を表示する。

## SP-002 Slot Registration
- MUST: スロット右クリックのコンテキストメニューから Edit/Clear/クリック実行トグルを提供する。
- MUST: Edit ではタイトル/コマンド/引数テンプレート/マクロスクリプト/ショートカットを編集でき、コマンドかマクロのいずれか一方以上が必須。
- MUST: ショートカット欄は `Ctrl+Shift+1` など KeyChordParser 書式を受け付け、保存時は正規化表記（例: `Ctrl+Shift+1`）へ整形する。不正書式は保存できない。
- MUST: Browse ボタンで Windows ファイル選択ダイアログを開き、選択したファイル名をタイトルへ自動反映する。
- MUST: Clear 選択時は現在設定が存在する場合に確認ダイアログを表示し、初期状態へ戻す。
- SHOULD: 引数テンプレートは `{args}` プレースホルダを含み、未設定時は既定値 `{args}` を採用する。

## SP-003 Layer Control
- MUST: レイヤー 1–4 を UI ボタンで切替。ボタンは現在レイヤーを強調表示。
- MUST: マウスホイール上下で前後のレイヤーに循環切替。
- SHOULD: 最終選択レイヤーを記憶。
- MUST: ドラッグ中、レイヤーボタン上へ ≥0.8s 滞在でそのレイヤーに自動切替（通常時はクリック動作）。

## SP-004 Launch & Macro Behavior
- MUST: ドロップされたパス（複数可）は登録先のコマンドへ `{args}` を展開して渡される。
- MUST: スロットクリック時は、設定されていればマクロスクリプトを最初に実行し、成功した場合のみコマンドを起動する。マクロのみ／コマンドのみも許容する。
- MUST: マクロは直前にアクティブだった外部ウィンドウへキーストロークを送る。ターゲットが見つからない場合はエラー扱い。
- MUST: マクロは `KEY`/`KEYDOWN`/`KEYUP`/`TEXT`/`WAIT`/`REPEAT`/`ENDREPEAT` 命令をサポートし、REPEAT 回数は 0〜1000 に制限する。
- MUST: マクロはマウス操作命令 `MOUSEMOVEABS`/`MOUSEMOVEREL`/`MOUSELEFTDOWN`/`MOUSELEFTUP`/`MOUSERIGHTDOWN`/`MOUSERIGHTUP`/`MOUSEMIDDLEDOWN`/`MOUSEMIDDLEUP`/`MOUSELEFTCLICK`/`MOUSERIGHTCLICK`/`MOUSEMIDDLECLICK`/`MOUSELEFTDOUBLECLICK`/`MOUSESCROLLUP`/`MOUSESCROLLDOWN`/`MOUSESCROLLLEFT`/`MOUSESCROLLRIGHT` をサポートする。
- MUST: Slot 編集ダイアログにはマクロスクリプトの書式と例を確認できるヘルプボタン（?）を配置し、押下で Tips ウィンドウをモードレス表示しながら編集を継続できる。
- MUST: Arguments Template は `{args}`（ドロップ/CLI パス群）、`{clipboard}`（クリップボード文字列）、`{clipboard_args}`（クリップボード内のパス群を引用付きで展開）をサポートし、プレースホルダが存在しない場合は空文字を挿入する。
- MUST: CLI 起動時は現在レイヤー内で最初に登録されたスロットを優先し、存在しない場合は全レイヤーから最初の登録スロットを選んで引数を渡す。失敗時はエラーダイアログを表示し UI を継続する。
- SHOULD: コマンド未登録スロットへドロップした場合は情報メッセージで通知する。

## SP-005 Persistence
- MUST: すべてのスロット/レイヤー設定・マクロ・クリック有効状態・常時最前面フラグ・現在レイヤーを `%AppData%/DropSendTo/config.json` に保存する。
- MUST: 起動時に JSON を検証し、破損時は `config.json.bak` から復元し、それでも不可なら既定値で再生成する。
- MUST: 設定は保存時に `.bak` を更新し、SlotRows/SlotColumns を 2〜4 に正規化した上で各レイヤーに行×列分のスロットを確保する（不足分は初期化する）。
- MUST: ウィンドウ位置（Left/Top）を保存し、起動時に復元。画面外の場合は可視範囲に補正する。

## SP-006 Menus
- MUST: ウィンドウ右上のメニューボタンおよびウィンドウ右クリックメニューに、Open Config/Open Logs/Change Prefix/Slot Layout/常に最前面トグル/Exit を提供する。
- MUST: メニューが開いた時点で常に最前面メニューのチェック状態を現在の設定に同期する。
- MUST: 上記メニューの Open Config は `%AppData%/DropSendTo/config.json` を既定アプリで開き、Open Logs は `%AppData%/DropSendTo/logs` フォルダをエクスプローラで開く。
- MUST: Slot Layout サブメニューは 2〜4 行×2〜4 列の組み合わせのみを表示し、選択と同時に UI を更新して設定へ保存する。
- MUST: Change Prefix を選択すると Prefix ダイアログが表示され、正規化した Prefix を保存する。不正な入力はエラーメッセージで拒否する。

## SP-007 Error Handling
- MUST: 例外や起動失敗時はユーザーに分かる文面でダイアログ表示し、ログへ `ERROR` レベルで書き込む。
- MUST: ログは `%AppData%/DropSendTo/logs/app.log` に UTF-8 で追記し、約 1MB 超でタイムスタンプ付きファイルへローテーションする。
- MUST: 起動時に 7 日より古い `app*.log` を削除する。
- SHOULD: CLI 起動失敗時もログに詳細を残す。

## SP-008 Platform
- MUST: .NET 8（Windows）でビルド/実行。WSL の .NET は非対象。

## SP-009 Macro Script Syntax
- MUST: マクロ行は先頭/末尾の空白を除いて解釈し、空行と `#` で始まる行は無視する。
- MUST: `KEY <修飾+キー>` は `Ctrl+Shift+S` のような組み合わせを一括送信する。修飾キーは Ctrl/Shift/Alt/Win/LWin/RWin を認識する。
- MUST: `KEYDOWN <キー>` / `KEYUP <キー>` で個別に押下/解放を制御できる。キー名は VK 名（例: `A`, `F4`, `Enter`, `Tab`）を受け付ける。
- MUST: `TEXT <文字列>` は Unicode 文字列をそのまま送信する。
- MUST: `WAIT <ミリ秒>` は 0〜60000 の整数のみ受け付け、マクロ実行を一時停止する。
- MUST: `SET <名前> <値>` は変数を定義し、値の中で `{{Name}}` 形式の他変数を参照できる。変数名は英数字と `_` のみで構成し、先頭は文字または `_` とする。大文字小文字は区別しない。
- MUST: `UNSET <名前>` は変数を削除し、未定義の名前が指定されてもエラーとはせず実行ログに通知を残す。
- MUST: `ADD`/`SUB`/`MUL`/`DIV <名前> <整数>` は対象変数を 64bit 整数として読み出し、指定した整数を加算・減算・乗算・除算する。`DIV` の除数に 0 は指定できず、演算結果が範囲外の場合はエラーとする。
- MUST: `APPEND`/`PREPEND <名前> <値>` は対象変数に文字列を末尾/先頭へ結合する。変数が未定義の場合は空文字列として扱う。
- MUST: 任意のコマンド引数内で `{{名前}}` を使用すると変数を展開し、未定義または閉じ括弧の欠落はマクロ失敗として扱う。展開後の文字列は既存コマンドの書式に従い検証する。
- MUST: `MOUSEMOVEABS <X> <Y>` は仮想スクリーン座標（ピクセル）を絶対移動として送出し、範囲外指定は仮想スクリーン内へクランプする。`MOUSEMOVEREL <dX> <dY>` は相対移動を送出する。
- MUST: `MOUSELEFTDOWN/UP`・`MOUSERIGHTDOWN/UP`・`MOUSEMIDDLEDOWN/UP` で各ボタンの押下/解放を制御し、`MOUSELEFTCLICK`/`MOUSERIGHTCLICK`/`MOUSEMIDDLECLICK` は押下と解放をセットで送出する。`MOUSELEFTDOUBLECLICK` はダブルクリック相当の 2 回押下を送出する。
- MUST: `MOUSESCROLLUP`/`MOUSESCROLLDOWN`/`MOUSESCROLLLEFT`/`MOUSESCROLLRIGHT` はホイール量を 1 ステップ=120 として扱い、引数省略時は 1 ステップ送出する。0 以下の値はエラーとする。
- MUST: `REPEAT <回数>` と `ENDREPEAT` でブロックを繰り返し、回数は 0〜1000。入れ子構造もサポートする。

## SP-010 Shortcut Prefix & Global Shortcuts
- MUST: Prefix は修飾キーとメインキーの組み合わせで構成され、入力値を正規化して保存する。解析できない場合は Ctrl+Q にフォールバックし、ユーザーへ警告する。
- MUST: Prefix を押下すると最長 1.5 秒間 armed 状態になり、ウィンドウ左上のインジケーターに Prefix 文字列と armed 状態をオーバーレイ表示する。マウス操作やタイムアウトで自動解除する。
- MUST: Armed 中に Prefix を再度押下すると解除しつつ前面ウィンドウへ Prefix のキー入力を送る（Prefix パススルー）。
- MUST: Prefix と同じ修飾キーを含むショートカットは、修飾キーを押し直さずに Prefix 入力直後から検出でき、必要に応じて再押下しても正常に動作する。
- MUST: Armed 中に登録済みショートカットが押下された場合、該当スロットをレイヤー自動切替後にトリガーし、クリックと同一フロー（マクロ→コマンド）で実行する。
- MUST: 設定ファイルのショートカット文字列はトリム後に解析し、不正なエントリは警告ログを残して無視する。
- SHOULD: ショートカット起動はマクロ実行中のスロットと競合しないよう制御し、競合時はユーザーへ通知する。
- SHOULD: スリープ復帰やセッション切替後は内部状態をクリアし、Prefix/ショートカットのラッチを残さない。
### Examples
- 入力: `C:\path\file.txt` をスロット A（`notepad.exe {args}`）へドロップ → 出力: `notepad.exe "C:\path\file.txt"` が起動。
- 入力: `DropSendTo.exe "C:\path\file.txt"` → 現在レイヤーで最初に登録されたスロットが同等に起動し、成功すれば UI は終了する。
- 登録: Edit 選択 → ダイアログの `...` ボタンで Windows ファイル選択ダイアログを開きコマンドを指定 → タイトルへファイル名が自動反映される。
- マクロ: スロットに `KEY Ctrl+C`, `WAIT 200`, `TEXT processed` を設定 → クリックすると直前の外部ウィンドウに Ctrl+C が送信され、200ms 後に `processed` が入力された上でコマンドが起動する。
- ショートカット: Prefix（例: `Ctrl+Q`）押下でインジケーターが点灯し、Ctrl を押し直さずに `X` を入力すると `Ctrl+X` ショートカットが実行されレイヤーが自動切替される。必要であれば修飾キーを離して押し直しても成功する。
- クリップボード: エクスプローラーで `C:\data\report.xlsx` をコピー → `ArgumentsTemplate` に `{clipboard_args}` を指定したスロットを起動すると `"C:\data\report.xlsx"` が展開されコマンドに渡される。

### Invariants / Boundaries
- スロット数は各レイヤーで SlotRows×SlotColumns（2〜4 行×2〜4 列）に一致し、空スロットは「未登録」表示。
- ドロップはファイル/フォルダ/ショートカットに限定。URL ドロップは未対応。
- レイヤーは 1..4 を循環移動（4 の次は 1、1 の前は 4）。
- スロットの高さは固定（約 48px）。
- ログ保持期間は 7 日未満。古いファイルは起動時クリーンアップされる。
- Prefix armed 状態は 1.5 秒以内にショートカット入力が無いか、ポインタイベントを受け取ると自動解除される。

## Traceability (excerpt)
- SP-001 → DES-002/003 → TC-040/045/065
- SP-004 → DES-002/004 → TC-020/021/025/080/087
- SP-006 → DES-002/003 → TC-065/090
- SP-007 → DES-005 → TC-030/095
- SP-010 → DES-002/003/005 → TC-035/085/086
