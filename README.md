# DropSendTo

DropSendTo は .NET 8 (WPF) 製の常駐ランチャです。半透明のコンパクトなウィンドウにファイル/フォルダをドラッグ＆ドロップするだけで、登録済みコマンドやマクロを実行できます。エクスプローラーの「送る」同様に CLI 引数でも起動でき、グローバル Prefix+ショートカット入力で任意スロットを呼び出せます。

## 主要機能
- 4 レイヤー × 2〜8 行・2〜4 列のグリッド（最大 8×4×4 = 128 スロット）と常時最前面ウィンドウ
- Slot Size メニューで Small（1 行表示・最小縦幅）/ Medium（2 行表示）/ Large（3 行表示）を切り替え可能（既定は Medium）
- Slot Layout Edit Mode（ツールバーの ✎ / コンテキストメニュー）で左上にインジケーターを表示しつつスロットをドラッグ＆ドロップでスワップ。レイヤーボタン上にホバーすると表示レイヤーが切り替わり、空スロットとの入れ替え＝移動も行える
- ドロップ/クリック/CLI 引数/Prefix+ショートカットから共通のスロット起動フロー（マクロ → コマンド）
- Prefix インジケーターをウィンドウ左上に表示し、CTRL 押しっぱなしなど Prefix と同じ修飾キーを再度押し直さなくてもショートカットを検出
- オプションで Prefix+Ctrl+N / Prefix+Ctrl+P によるレイヤー前後移動を有効化でき、キーボードのみでレイヤーを巡回可能
- Prefix+Enter でウィンドウを呼び戻した後は、矢印キーでスロットを選択し Enter で実行できる（最初の矢印入力までは選択状態にならないため誤操作を防止）
- タスクトレイ常駐・最小化をサポートし、Prefix+Shift+Enter やメニューからワンクリックでウィンドウを格納
- Slot Edit ダイアログでタイトル/コマンド/引数テンプレート/マクロ/ショートカットを編集し、マクロ Tips をモードレスで参照可能
- マクロ実行モード（排他／割り込み実行／一時停止して割り込み）をコンテキストメニューから切り替え、再起動後も保持
- `%AppData%/DropSendTo/config.json` への設定保存、`.bak` バックアップ、7 日保持のローテーションログ

## 対応環境
- Windows 10 22H2 以降 / Windows 11
- .NET SDK 8.x（Windows 側でインストール済みであること）

## クイックスタート
1. リポジトリをクローン (`git clone ...`)
2. PowerShell または Windows ターミナルでビルド  
   `dotnet build`
3. UI を起動  
   `dotnet run --project src/DropSendTo`
4. ファイル/フォルダを任意スロットにドロップ → Slot Edit で設定したコマンドへ `{args}` を展開して渡します。

### CLI 起動
```
src/DropSendTo/bin/Debug/net8.0-windows/DropSendTo.exe "C:\path\to\file.txt"
```
最初にマッチした登録スロットを実行し、成功時は UI を開かず終了します。

### Prefix ショートカットの使い方
1. `Change Prefix...` メニューで Prefix を設定 (既定は `Ctrl+Q`)
2. Prefix を押すとウィンドウ左上のインジケーターが点灯し、4 秒以内に登録済みショートカットを入力すると該当スロットが発火します。
3. Prefix と同じ修飾キーを含むショートカット（例: Prefix `Ctrl+Q` → ショートカット `Ctrl+X`）は、Ctrl を押しっぱなしのまま `X` を押せば実行可能です。押し直しても動作します。
4. Prefix+Enter で DropSendTo ウィンドウを最前面に復帰させます（タスクトレイへ最小化している場合でも復帰）。
5. Prefix+Shift+Enter でウィンドウをタスクトレイへ最小化します。タスクトレイアイコンの左クリックでも復帰できます。
6. Prefix を再入力すると armed 状態を解除しつつ、Prefix キーを前面アプリへ送出します。

### マクロの概要
- `KEY`, `KEYDOWN`, `KEYUP`, `TEXT`, `WAIT`, `REPEAT`、各種マウス操作 (`MOUSELEFTCLICK` 等) に対応
- Macro Script 欄の `?` ボタンで Tips を別ウィンドウ表示 → 編集ダイアログを閉じずに参照できます
- マクロ実行後にコマンド起動（マクロのみ・コマンドのみも可）
- コンテキストメニューの「Macro 実行モード」で排他／割り込み実行／一時停止して割り込みを選択でき、割り込みは即キャンセル、一時停止は新しいマクロ完了後に自動復帰します
- `REPLACE <変数> "検索" "置換"` で変数内の文字列（半角空白を含む）を置換し、`""` を指定すれば削除できます（例: クリップボード文字列の整形）。

### 引数テンプレートのプレースホルダ
- `{args}`: ドロップ/CLI で渡されたパスをスペース区切り + 必要に応じて引用付きで展開
- `{clipboard}`: クリップボードの文字列をそのまま挿入（前後の空白は自動トリム）
- `{clipboard_args}`: 最新のクリップボードにあるテキスト（複数行なら行ごと）を引用付きで展開
  - 例) エクスプローラーでファイルをコピー → `{clipboard_args}` を引数に指定するとコピーしたパスを用いてアプリを起動
- `{clipboard_args:n}`: 直近 n 行（1〜20 行）のコピー履歴を古い順に引用付きで展開
  - 例) `{clipboard_args:3}` で最初の 3 件だけを引数に渡せます

## 設定とログ
- 設定: `%AppData%/DropSendTo/config.json`（保存時に `.bak` へバックアップ）
- ログ: `%AppData%/DropSendTo/logs/app.log`（約 1MB でローテーション、7 日保持）

## ビルド / テスト / 配布
- Build: `dotnet build`
- Test: `dotnet test`
- Release ビルド & ZIP 生成:  
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Rid win-x64 -SelfContained:$false -Version vX.Y.Z`
- テスト込みワークフロー（実行中プロセスを終了）:  
  `powershell -ExecutionPolicy Bypass -File .\scripts\Run-Tests-And-Build.ps1 -KillRunning`


## プロジェクトドキュメント
- 機能要件: `docs/REQUIREMENTS.md`
- 仕様: `docs/SPEC.md`
- 設計: `docs/DESIGN.md`
- テスト計画: `docs/TESTPLAN.md`

## コントリビュート
コーディング規約・テスト方針・PR テンプレートは `AGENTS.md` を参照してください。プルリクエストは Conventional Commits 形式、`dotnet build/test/format` 合格が必須です。
