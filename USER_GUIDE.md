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
- `Ctrl+F` で検索レイヤーを呼び出せます（Emacs ライク操作が有効なら `Ctrl+S`、vi ライク操作が有効なら `/`）。レイヤー名オーバーレイと同じ位置に検索ボックスが出現し、Title と Search Keywords を部分一致/サブシーケンスで検索した結果だけを 1 番から詰めて表示します。Esc で閉じられます。検索中は Tab に加えて、検索ボックスがウィンドウ上側にあるときは ↓（Emacs: `Ctrl+N`）でスロットへ移り、最上段で ↑（`Ctrl+P`）を押すと検索ボックスへ戻ります。検索ボックスが下側に回り込んだときはこの上下が逆転します。vi の `j/k` では検索ボックスとの行き来は行わず、スロット内の移動のみです。
- コンテキストメニューの「検索ホットキー...」から、Prefix を押さずに検索レイヤーを開くグローバルホットキーを設定できます（既定は無効）。Prefix+Alt+Space でも呼び出せるため、Alt+Space などは他のランチャーと競合しやすい旨の警告を確認しつつ慎重に設定してください。

### Step 2: スロットを登録する
1. 空きスロットを右クリック → `Edit...` を開く  
2. Title / Command / Arguments / Macro Script / Shortcut を入力  
   - `{args}` でドロップしたパスを展開  
   - Macro Script モードはマクロのみ、Command モードはコマンドのみ、Macro Script 拡張は両方を組み合わせ  
   - Search Keywords 欄にスペース区切りで検索用キーワードを入れておくと、検索レイヤーから素早く見つけられます（Title と合わせて部分一致検索）
3. `?` ボタンからコマンドサンプル、`...` から実行ファイル参照が可能  
4. `OK` で保存すると `%AppData%\DropSendTo\config.json` に反映
5. 「実行後に最小化」でウィンドウを閉じる条件を細かく指定できます（Command のみ / Macro Script のみ / Macro Script 拡張のみを切り替え、クリック / ショートカット / ドロップ / キーボード操作ごとに適用可）。ドロップ時のみ最小化しない、といった使い分けが可能です。

### Step 3: 実行してみる
- ファイル/フォルダをスロットへドラッグ＆ドロップ → `{args}` を展開したコマンドが実行されます。  
- Macro Script 拡張モードでマクロ＋コマンドを両方登録しているスロットにドロップした場合は、まずマクロが実行され (`{{drop_args}}` / `{{drop_count}}` / `{{drop_path}}` / `{{drop_path:n}}` でドロップしたパスを参照可能)、そのマクロ内で `COMMAND` を呼ぶことで加工後の引数を登録コマンドへ受け渡せます。  
- スロットクリックでも同じ動作。Command モードならマクロ実行中でも並列で起動できます。  
- `DropSendTo.exe "<path>"` のように CLI 引数を渡すと、現在レイヤーで最初に登録されているスロットが自動で処理します。

### Step 4: ウィンドウ操作と設定
- `起動時のウィンドウ` で「常に表示」か「前回状態を復元」を切り替え。  
- `Macro 実行モード` で排他／割り込み／一時停止を選ぶと、その場でマクロ挙動が変わります。  
- マウスでウィンドウを呼び出す場合はコンテキストメニューの「マウスジェスチャ (表示/最小化)...」から設定できます。既定は「時計回り 3 周で表示、反時計回り 2 周で最小化」。方向の入れ替え、表示ジェスチャのみ Ctrl 同時押しを必須にする設定、発表モード時の無効化に加え、ジェスチャ判定の半径もスライダーで調整できます（ドラッグ中はカーソルを中心にガイド円が表示され、半径制限のチェックを外すと制限を無効化できます）。  
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
   | `Alt+Space` | 検索レイヤーを開いて DropSendTo を前面に復帰 | 検索入力へフォーカス |
   | `Ctrl+N` / `Ctrl+P` | （オプション）次/前レイヤーへ移動 | メニュー「Prefix: Ctrl+N/P…」で有効化 |
   | Emacs/vi ライク | （オプション）`Ctrl+F/B/N/P/A/E/M/J`、`Ctrl+[`, `x`、`Alt+X` / `h/j/k/l`, `:` などでスロット移動・メニュー操作 | メニュー「キーボード操作」から Emacs / vi を個別に有効化（既定 OFF） |
   | Prefix 再入力 | Prefix パススルー | Prefix キーを前面アプリへ送出 |
5. スロットのショートカット欄では空白・カンマ・ `>` で区切ると複数のキー列を登録できます（例: `B A`, `Ctrl+K D`）。最初のキーが一致すると入力は抑制され、候補が複数残っている場合は次のキーを待機します。待機中でも Tab/Enter など上表の特殊操作は優先され、異なるキーを押すと待機を解除します。検索ショートカットに使用するため、`Alt+Space` は登録できません。
6. RDP/Citrix でリモート環境を操作する際はコンテキストメニューの「Prefix: リモートセッション優先 (RDP/Citrix)」を有効にすると、リモートウィンドウが前面の間はローカルの DropSendTo を自動停止し、リモート側の DropSendTo 操作を優先できます。リモート以外のウィンドウを前面にすると自動的にローカルへ戻ります。

### 2.2 キーボードによるスロット選択
- Prefix+Enter でウィンドウを呼び戻した直後に矢印キーを押すと、スロットにハイライトが付きます。  
- 矢印キーで移動 → Enter で実行。最初に矢印が押されるまでは選択状態にならないため、Prefix+Enter+Enter だけでは起動しません。  
- Esc またはマウス操作でキーボード選択モードを解除できます。
- Prefix+Alt+Space で検索レイヤーを開いた場合、Esc（または Emacs ライク操作有効時の Ctrl+G）で検索を閉じると、呼び出し直前の状態へ戻ります。タスクトレイから呼び出していた場合は再度トレイへ最小化され、表示中にマウス追従などで位置が変わっても元の位置に戻ります。

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
| `RENAME <元パス> <新しいパス>` | ファイル/フォルダの名前を変更（変数展開・クォート可） | `RENAME "{{drop_path}}" "{{drop_path}}.bak"` |
| `TESTPATH <変数> <パス>` | ファイル/フォルダの存在を `1` (存在) / `0` (未存在) として変数に格納 | `TESTPATH PathOk {{drop_path}}` |
| `READFILE <変数> <パス> [MAX <バイト>]` | ファイル内容を変数へ読み込み（既定 4096 バイト。超える場合は MAX で明示） | `READFILE Prompt C:\notes\prompt.txt` |
| `RETURN ["メッセージ"]` | マクロを即終了（メッセージは任意でログ/結果に残る） | `RETURN "条件を満たさないため中止"` |
| `POPUP "メッセージ"` | 任意のメッセージをポップアップ表示（閉じるまでマクロを一時停止） | `POPUP "{{drop_path}} が見つかりません"` |
| `REPEAT <n>` … `ENDREPEAT` | ブロックを繰り返し（0〜1000 回） | `REPEAT 3` → `KEY TAB` → `ENDREPEAT` |
| `COMMAND [args]` | Macro Script 拡張モードで登録済みコマンドを呼び出し | `COMMAND "{{clipboard}}"` |
| `{{drop_args}}` / `{{drop_path:n}}` | ドロップ時にパスを取得（`n` は 1 基点） | `SET First {{drop_path:1}}` |
| `IF <左> <演算子> <右>` | 条件分岐（`ELSEIF`/`ELSE`/`ENDIF` と組み合わせ、ネスト可） | `IF {{Count}} >= 10` |
| `ELSEIF <条件>` / `ELSE IF <条件>` | 追加条件ブロック（前段不成立時のみ評価） | `ELSEIF {{Mode}} == 2` |
| `ELSE` / `ENDIF` | `IF` ブロックの分岐・終了 | `ELSE` → `COMMAND "fallback"` |
| `PREFIX ARM/SEND/PASSTHROUGH` | Prefix 状態の制御と送出 | `PREFIX ARM` |

### 2.5 Macro Script サンプル集
以下はそのまま貼り付けて使える小さなレシピです（必要に応じてショートカットやドロップと組み合わせてください）。

- **複数ファイルのファイル名を加工して列挙（FOREACH_DROP）**  
  ドロップした各パスのファイル名スペースを `_` に置換し、番号付きで出力します。ドロップが 0 件ならスキップされます。  
  ```
  FOREACH_DROP Item INDEX i
      SET Name {{Item}}
      REPLACE Name " " "_"
      TEXT [{{i}}] {{Name}}
  ENDFOREACH
  ```

- **ドラッグしたファイルを順にコマンドへ流す（Macro Script 拡張 + COMMAND）**  
  ドロップした各パスを加工しつつ `COMMAND` に渡します（ArgumentsTemplate は `{args}` のままで OK）。  
  ```
  FOREACH_DROP FilePath INDEX idx
      SET Clean {{FilePath}}
      REPLACE_REGEX Clean "\\s+" "_" IGNORECASE
      COMMAND "{{Clean}} --index={{idx}}"
  ENDFOREACH
  ```

- **前面アプリの入力欄へクリップボードを貼り付け、改行で区切る**  
  ```
  PREFIX ARM
  KEY Ctrl+L
  WAIT 200
  TEXT {{clipboard}}
  KEY Enter
  ```

- **簡易テンプレ: 3 回クリックして待機**  
  ```
  REPEAT 3
      MOUSELEFTCLICK
      WAIT 150
  ENDREPEAT
  ```

- **ドラッグしたファイルを拡張子ごとにフォルダ分けして移動（Macro Script 拡張 + PowerShell）**  
  スロットの Command を `powershell.exe`、Arguments を空にしておき、ドロップした各ファイルを拡張子別フォルダ（例: `C:\Sorted\pdf`）へ移動します。  
  ```
  FOREACH_DROP Item
      SET Ext {{Item}}
      REPLACE_REGEX Ext "^.*\\.([^.\\\\/]+)$" "$1" IGNORECASE
      SET Target "C:\\Sorted\\{{Ext}}"
      COMMAND "-NoProfile -ExecutionPolicy Bypass -Command \"New-Item -ItemType Directory -Force -Path '{{Target}}'; Move-Item -LiteralPath '{{Item}}' -Destination '{{Target}}' -Force\""
  ENDFOREACH
  ```
  ※ `Target` のパスは用途に合わせて変更してください。UNC/ネットワークパスでも同様に動作します。

- **ファイル名を書き換えてリネーム（REPLACE + RENAME）**  
  元名から空白を `_` に置換し、末尾に `_done` を付けてリネームします。  
  ```
  SET Original {{drop_path}}
  SET NewName {{Original}}
  REPLACE_REGEX NewName "\\s+" "_" IGNORECASE
  APPEND NewName _done
  RENAME "{{Original}}" "{{NewName}}"
  ```

### 2.6 .NET 正規表現クイックリファレンス（REPLACE_REGEX 用）
- 文字: `abc` / 数字: `[0-9]` / 英数字: `[A-Za-z0-9]` / 空白: `\s` / 非空白: `\S` / 任意1文字: `.`  
- 境界: 行頭 `^` / 行末 `$` / 単語境界 `\b` / 非境界 `\B`  
- 繰り返し: `*`(0回以上) / `+`(1回以上) / `?`(0か1回) / `{n}` / `{n,}` / `{n,m}`（デフォルト貪欲、末尾に `?` で非貪欲）  
- グループ: `(...)`（キャプチャ、置換で `$1` など） / `(?:...)`（非キャプチャ） / 選択 `|`（例: `png|jpg`）  
- 先読み: `(?=...)`（肯定）、`(?!...)`（否定）  ※先読みのみサポート、後読みは .NET なので使用可だが複雑なパターンは避けると安全。  
- オプション（REPLACE_REGEX の末尾に空白区切りで指定）:  
  - `IGNORECASE`/`I`: 大文字小文字を無視  
  - `MULTILINE`/`M`: `^`/`$` が行頭/行末にマッチ  
  - `SINGLELINE`/`S`: `.` が改行にもマッチ  
  - `IGNOREWHITESPACE`/`X`: パターン内の空白と `#` コメントを無視  
- エスケープ: バックスラッシュと引用符は二重にする（例: `\\d+`、`\"`）。DropSendTo のマクロ文字列内でも `\` をさらにエスケープする必要があるので `\\d+` のように書いてください。  
- 例:  
  - 拡張子を取得: `^.*\.([^.\\/]+)$` → `$1` が拡張子  
  - 連続空白を1つに: `\s+` → 置換 `_`  
  - 先頭/末尾空白を削除: `^\s+|\s+$` → 置換 `""`

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

### 2.7 CLI・自動化のヒント
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
