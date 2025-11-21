# DropSendTo ユーザーガイド

「DropSendTo でどこから手を付ければ良いか分からない」を解消するため、**基本編 → 応用編**の順でステップ形式にまとめました。まずは基本編をそのまま試し、慣れてきたら応用編でパワーユーザー向けの機能を取り入れてください。

---

## 1. 基本編 ― まずはここから

### Step 0: 動作環境とセットアップ
- 対応 OS: Windows 10 22H2 以降 / Windows 11  
- ランタイム: .NET 8 Desktop Runtime（同梱バンドル版を使う場合は不要）  
- セットアップ: 配布 ZIP を任意フォルダへ展開 → `DropSendTo.exe` を起動 → 初回起動時に `%AppData%\DropSendTo\config.json` を自動作成  
- SmartScreen が出たら「詳細情報 → 実行」を選択

### Step 1: ウィンドウを覚える
- 既定は **2×2 スロット**、レイヤーは 4 枚。右上の数字ボタンかマウスホイールで切り替えられます。
- `Slot Layout` メニューで最大 8 行×4 列（32 スロット/レイヤー）、`Slot Size` で Small/Medium/Large（既定 Medium）を選択。Small=1 行、Medium=2 行、Large=3 行表示。
- レイアウトを大きく入れ替えたいときはツールバーの ✎ を押して編集モードへ。モード中は左上に `EDIT MODE` インジケーターが出現し、スロットをドラッグ＆ドロップでスワップできます。ドラッグ中に現在のレイヤーボタンへ 0.8 秒以上ホバーするとそのレイヤーが自動的に表示され、空スロットへドロップすれば単純な移動として反映されます。入れ替え対象は元の位置にプレビュー表示され、ドロップした瞬間にのみ確定します（モード中はクリック/ファイルドロップ実行は無効化されます）。
- `Always on Top` のチェックを外すと通常ウィンドウ化、`Minimize to Tray` や Prefix+Shift+Enter でトレイへ格納できます。

### Step 2: スロットを登録する
1. 空きスロットを右クリック → `Edit...` を開く  
2. Title / Command / Arguments / Macro Script / Shortcut を入力  
   - `{args}` でドロップしたパスを展開  
   - Macro Script モードはマクロのみ、Command モードはコマンドのみ、Macro Script 拡張は両方を組み合わせ  
3. `?` ボタンからコマンドサンプル、`...` から実行ファイル参照が可能  
4. `OK` で保存すると `%AppData%\DropSendTo\config.json` に反映

### Step 3: 実行してみる
- ファイル/フォルダをスロットへドラッグ＆ドロップ → `{args}` を展開したコマンドが実行されます。  
- Macro Script 拡張モードでマクロ＋コマンドを両方登録しているスロットにドロップした場合は、まずマクロが実行され (`{{drop_args}}` / `{{drop_count}}` / `{{drop_path}}` / `{{drop_path:n}}` でドロップしたパスを参照可能)、そのマクロ内で `COMMAND` を呼ぶことで加工後の引数を登録コマンドへ受け渡せます。  
- スロットクリックでも同じ動作。Command モードならマクロ実行中でも並列で起動できます。  
- `DropSendTo.exe "<path>"` のように CLI 引数を渡すと、現在レイヤーで最初に登録されているスロットが自動で処理します。

### Step 4: ウィンドウ操作と設定
- `起動時のウィンドウ` で「常に表示」か「前回状態を復元」を切り替え。  
- `Macro 実行モード` で排他／割り込み／一時停止を選ぶと、その場でマクロ挙動が変わります。  
- 設定・ログの場所  
  - config: `%AppData%\DropSendTo\config.json`（バックアップは `.bak`）  
  - logs: `%AppData%\DropSendTo\logs\app.log`（約 1MB でローテーション）
- コンテキストメニューの `ショートカット一覧...` から、Slot ID / Title / Shortcut の列をクリックしてソートしながら登録済みショートカットを確認できます（Prefix 後に使えるスロットだけを表示）。Status 列には `競合`（同じショートカットが複数スロットに存在）、`被覆`（短いショートカットが先に発動するためこのスロットが実質呼び出せない）、`解析エラー` が表示され、該当行は淡い色でハイライトされます。Window を開いたまま main window を操作できるので、内容を見ながら編集が可能です。

ここまでで「登録 → ドラッグ → 起動」の基本は完了です。次はもっと便利にする応用編へ。

---

## 2. 応用編 ― さらに便利に使う

### 2.1 Prefix & グローバルショートカット
1. `Change Prefix...` で Prefix（既定 `Ctrl+Q`）を設定。  
2. Prefix を押すと 4 秒間 armed 状態になり、左上にインジケーターが点灯します。  
3. Prefix と同じ修飾キーを含むスロットショートカット（例: Prefix = `Ctrl+Q`, Shortcut = `Ctrl+X`）は **Ctrl を押したまま** `Q → X` と連続で押すだけで実行できます。もちろん Ctrl を離して押し直しても動作します。複数キーのシーケンス（例: `B A`, `Ctrl+K D`）も登録でき、Prefix 後に 1 つ目が一致すると次のキー入力待ちになります。  
4. 組み合わせ一覧（いずれも Prefix 押下後）  
   | キー | 動作 | 備考 |
   | --- | --- | --- |
   | `Enter` | DropSendTo を前面に復帰 | マウス追従モードならカーソル位置へ移動 |
   | `Shift+Enter` | 最小化してタスクトレイへ送る | Prefix+Shift+Enter |
   | `Alt+Enter` | 実行中マクロをキャンセル | マクロなしスロットには影響なし |
   | `Tab` | 表示モードを固定/マウス追従で切り替え | タスクトレイ中でも可 |
   | `Ctrl+N` / `Ctrl+P` | （オプション）次/前レイヤーへ移動 | メニュー「Prefix: Ctrl+N/P…」で有効化 |
   | Emacs/vi ライク | （オプション）`Ctrl+F/B/N/P/A/E/M/J`、`Ctrl+[`, `x`、`Alt+X` / `h/j/k/l`, `:` などでスロット移動・メニュー操作 | メニュー「キーボード操作」から Emacs / vi を個別に有効化（既定 ON） |
   | Prefix 再入力 | Prefix パススルー | Prefix キーを前面アプリへ送出 |
5. スロットのショートカット欄では空白・カンマ・ `>` で区切ると複数のキー列を登録できます（例: `B A`, `Ctrl+K D`）。最初のキーが一致すると入力は抑制され、候補が複数残っている場合は次のキーを待機します。待機中でも Tab/Enter など上表の特殊操作は優先され、異なるキーを押すと待機を解除します。
6. RDP/Citrix でリモート環境を操作する際はコンテキストメニューの「Prefix: リモートセッション優先 (RDP/Citrix)」を有効にすると、リモートウィンドウが前面の間はローカルの DropSendTo を自動停止し、リモート側の DropSendTo 操作を優先できます。リモート以外のウィンドウを前面にすると自動的にローカルへ戻ります。

### 2.2 キーボードによるスロット選択
- Prefix+Enter でウィンドウを呼び戻した直後に矢印キーを押すと、スロットにハイライトが付きます。  
- 矢印キーで移動 → Enter で実行。最初に矢印が押されるまでは選択状態にならないため、Prefix+Enter+Enter だけでは起動しません。  
- Esc またはマウス操作でキーボード選択モードを解除できます。

### 2.3 マクロと引数テンプレート
- `Macro Script` では `KEY`, `TEXT`, `WAIT`, `REPEAT`, 各種 `MOUSE*`、変数 (`SET/ADD/...`) など多数のコマンドを利用可能。  
- `PREFIX ARM` / `PREFIX SEND` / `PREFIX PASSTHROUGH` を使えばマクロの中から Prefix 操作も再現できます。  
- 引数テンプレートで使えるプレースホルダ  
  - `{args}`: ドロップ/CLI のパス  
- `{clipboard}`: 最新クリップボード文字列  
- `{clipboard_args}` / `{clipboard_args:n}`: クリップボード内のパスを引用付きで展開  
- `{clipboard:n}`: 直近 n 行をそのまま展開
- Macro Script 拡張モードのスロットへドロップした場合はマクロ内から `{{drop_args}}`（コマンド引数形式で連結）、`{{drop_count}}`（件数）、`{{drop_path}}` または `{{drop_path:n}}`（1=先頭）で個別パスを参照でき、`COMMAND "{{drop_path:1}}_processed"` のように加工して登録コマンドへ渡せます。
- 文字列置換は Ordinal 比較の `REPLACE`、拡張正規表現の `REPLACE_REGEX` の 2 種類を提供します。後者は `$1` 等のキャプチャや `IGNORECASE`/`MULTILINE`/`SINGLELINE`/`IGNOREWHITESPACE` オプションを空白区切りで指定できます。
- `IF <左辺> <演算子> <右辺>` / `ELSEIF <条件>`（`ELSE IF` も可）/ `ELSE` / `ENDIF` で条件分岐を記述できます。演算子は `==`/`!=`/`>`/`>=`/`<`/`<=`（整数比較）と `CONTAINS`/`NOTCONTAINS`/`STARTSWITH`/`ENDSWITH`（文字列比較）をサポートし、空白を含む値は `""` で囲んでください。`ELSEIF` は複数回使用でき、`ELSE` は 1 回のみです。親が偽、または前段の `IF`/`ELSEIF` が成立した場合は後続ブロックの条件式を評価しないため、未定義変数を参照しても安全です。

### 2.4 Macro Script クイックリファレンス
| コマンド | 説明 | 例 |
| --- | --- | --- |
| `KEY <Chord>` | 単発のキー入力 | `KEY Ctrl+S` |
| `KEYDOWN` / `KEYUP` | キーの押下/解放を分けて送信 | `KEYDOWN Ctrl` → `KEYUP Ctrl` |
| `TEXT <文字列>` | 文字列をそのまま入力 | `TEXT processed.` |
| `WAIT <ms>` | 指定ミリ秒待機 | `WAIT 300` |
| `MOUSELEFTCLICK` など | マウスクリック/ホイール/移動一式 | `MOUSEMOVEABS WIN_CENTER` |
| `SET/ADD/SUB/MUL/DIV <変数> <値>` | 64bit 整数の計算 | `SET Count 0` |
| `APPEND/PREPEND` | 文字列を結合 | `APPEND Cmd " --flag"` |
| `REPLACE <変数> "検索" "置換"` | 変数内文字列を空白含め置換（`""` で削除） | `REPLACE Body " " "_"` |
| `REPLACE_REGEX <変数> "正規表現" "置換" [オプション]` | 拡張正規表現で置換（`$1` 等のグループ可、`IGNORECASE`/`MULTILINE` などを指定可能） | `REPLACE_REGEX Body "\s+" "_" IGNORECASE` |
| `TESTPATH <変数> <パス>` | ファイル/フォルダの存在を `1` (存在) / `0` (未存在) として変数に格納 | `TESTPATH PathOk {{drop_path}}` |
| `POPUP "メッセージ"` | 任意のメッセージをポップアップ表示（閉じるまでマクロを一時停止） | `POPUP "{{drop_path}} が見つかりません"` |
| `REPEAT <n>` … `ENDREPEAT` | ブロックを繰り返し（0〜1000 回） | `REPEAT 3` → `KEY TAB` → `ENDREPEAT` |
| `COMMAND [args]` | Macro Script 拡張モードで登録済みコマンドを呼び出し | `COMMAND "{{clipboard}}"` |
| `{{drop_args}}` / `{{drop_path:n}}` | ドロップ時にパスを取得（`n` は 1 基点） | `SET First {{drop_path:1}}` |
| `IF <左> <演算子> <右>` | 条件分岐（`ELSEIF`/`ELSE`/`ENDIF` と組み合わせ、ネスト可） | `IF {{Count}} >= 10` |
| `ELSEIF <条件>` / `ELSE IF <条件>` | 追加条件ブロック（前段不成立時のみ評価） | `ELSEIF {{Mode}} == 2` |
| `ELSE` / `ENDIF` | `IF` ブロックの分岐・終了 | `ELSE` → `COMMAND "fallback"` |
| `PREFIX ARM/SEND/PASSTHROUGH` | Prefix 状態の制御と送出 | `PREFIX ARM` |

#### サンプル 1: クリップボードを検索サイトへ投げる
```
PREFIX ARM
KEY Ctrl+L
WAIT 200
TEXT https://www.bing.com/search?q={{clipboard}}
KEY Enter
```

#### サンプル 2: 指定アプリへファイル名をリネームして入力
```
SET Name "Processed_"
APPEND Name {{clipboard}}
KEY Ctrl+O
WAIT 200
TEXT {{Name}}
KEY Enter
```

より詳細なコマンド一覧はアプリ内の `?` ボタン（Macro Script ダイアログ右上）から参照できます。

### 2.4 CLI・自動化のヒント
- `DropSendTo.exe "<path>"` で現在レイヤーの最初の登録スロットを自動実行。PowerShell などからまとめて呼び出せます。  
- 設定を別環境へ移行したい場合はコンテキストメニューから `Export/Import Config...` を利用。暗号化付きで安全に持ち運び可能。  
- フォルダを開きたいときは Command を `explorer.exe`、Arguments に `"{args}"` または固定パスを指定してください（フォルダパス単体では動作しません）。

---

## 3. トラブルシューティング & FAQ
| 症状 | 対処 |
| --- | --- |
   | ショートカットが反応しない | Prefix 設定を見直し、ショートカット欄が空でないか確認。Ctrl など修飾キーの押し直し・長押しの両方を試す。 |
   | キー操作が想定と異なる | コンテキストメニュー「キーボード操作」で Emacs / vi の各トグルを確認し、不要なら OFF にする。 |
| Prefix 再入力で期待どおりに解除されない | Prefix を押したままマウス操作すると armed が解除されるため、再入力前にマウス操作を控える。 |
| スロットが実行されない | 設定ダイアログで Command/Arguments/Macro の必須項目を確認。`%AppData%\DropSendTo\logs` にエラーが出ていないかチェック。 |
| フォルダを開けない | Command に `explorer.exe`、Arguments に `"{args}"` または `"<フォルダパス>"` を記述する。 |
| ウィンドウが見つからない | `config.json` の `WindowLeft/WindowTop` を 0 に戻す、または Prefix+Enter で前面に復帰させる。 |
| 設定を初期化したい | アプリ終了後に `config.json` を削除（必要に応じて `.bak` から復元）。 |

---

## 4. サポート
- 既知の問題や要望は GitHub Issue に記載しています。  
- バグ報告時は発生手順と `%AppData%\DropSendTo\logs` を添付していただけると助かります。  
- 仕様更新やリリースノートは README を併せてご確認ください。

これで DropSendTo を段階的に学べます。基本編で土台を固め、応用編で自分好みのワークフローに仕上げてください。***
