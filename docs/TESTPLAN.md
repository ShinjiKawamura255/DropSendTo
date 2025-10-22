# TESTPLAN

## Scope & Levels
- Scope: ランチャ挙動、登録/永続化、レイヤー・レイアウト切替、キーボードマクロ、Prefix ベースのグローバルショートカット、CLI フロー、ログアクセス、エラーハンドリング、UI 表示要件。
- Levels: unit（services）, integration（config+launcher+macro）, manual e2e（UI）。

## Framework & Runner
- Unit/Integration: xUnit（`dotnet test`）。
- Manual: チェックリストで確認（スクショ取得）。

## Test Cases (excerpt)
- TC-001 Platform: Windows の .NET 8 でビルド/実行できる。
- TC-010 Slots: 現在の行列構成（2〜4 × 2〜4）で全スロットの登録/解除/復元が可能で、タイトル/コマンド/引数/マクロ/ショートカット/クリック可否が保存される。
- TC-020 LaunchDrop: ファイル/フォルダのドロップで `{args}` がクォートされ、登録コマンドに渡る。
- TC-021 LaunchCli: CLI 引数で現在レイヤー優先→全レイヤー検索が行われる。失敗時は UI 表示とログ出力。
- TC-025 MacroScriptHappy: `KEY`/`WAIT`/`TEXT`/`REPEAT` など代表的な命令が期待通り展開・送信される。
- TC-026 MacroScriptValidation: 不正なキー名/REPEAT 上限超過/未閉鎖ブロックでエラーが返る。
- TC-027 MacroScriptVariables: `SET`/`UNSET`/`ADD`/`SUB`/`MUL`/`DIV`/`APPEND`/`PREPEND` で変数を操作し、`{{Var}}` 展開が成功する。未定義・不正名・整数でない値・0 除算は失敗およびログ記録となる。
- TC-030 Errors: 実行不可パスやマクロエラーでメッセージが表示され、ログに ERROR が残る。
- TC-035 PrefixFallback: Prefix/ショートカット解析が失敗した場合に Ctrl+Q へフォールバックし、ユーザー通知・警告ログが残る。
- TC-040 UiTheme: 常時最前面が既定で ON、黒基調・半透明・角丸・ボタンスタイルが維持される。
- TC-045 AlwaysOnTopToggle: メニューから常時最前面を切り替え、設定保存後も状態が復元される。
- TC-050 Layer: ボタン/ホイールの循環切替、ドラッグホバー 0.8s で自動切替、ハイライト更新。
- TC-060 WindowPos: 再起動で位置復元し、画面外に移動しても可視範囲へ補正される。
- TC-065 LayoutMenu: Slot Layout メニューで各行列候補を選択でき、即時 UI が再構成され、再起動後も構成が保持される。
- TC-070 SlotStates: hover/drag/クリックで視覚状態が変化する（UI レベル）。
- TC-080 ClickExec: クリック実行が有効なときのみマクロ→コマンドが動く。無効設定は保存される。
- TC-085 ShortcutTrigger: Prefix 押下でインジケーターが点灯し、4 秒以内の登録ショートカットでスロットがレイヤー自動切替込みで起動する。Prefix 再入力で前面ウィンドウへ送出される。
- TC-086 ShortcutSharedModifier: Prefix と同じ修飾キーを含むショートカット（例: Prefix `Ctrl+Q` → `Ctrl+X`）は修飾キーを押し直さなくても発火し、再押下でも動作する。
- TC-087 ClipboardArgs: `{clipboard}` と `{clipboard_args}` がクリップボード文字列/パスを期待通り展開し、引用が適切に付与される。
- TC-090 MenuAccess: Open Config/Open Logs/Change Prefix/Slot Layout/常に最前面/Exit が機能し、Open Logs がディレクトリを開く。
- TC-095 LoggingRetention: ログが 1MB 超でローテーションし、7 日以上前の `app*.log` が削除される。

## Execution
- Unit: `dotnet test tests/DropSendTo.Tests -c Release`。
- Manual: 手順
  1) アプリ起動→常時最前面・半透明・角丸・ボタンスタイルを確認。
  2) スロットを Edit で登録（Browse 使用、タイトル反映、ショートカットとマクロ設定）→再起動後も設定/ショートカット/クリック有効が保持されることを確認。
  3) ファイル/フォルダをドロップして `{args}` が正しく渡ることを確認。
  4) CLI 引数で起動し、優先スロットが実行され UI が終了することを確認。登録なしの場合は UI 継続とメッセージ表示。
  5) マクロスクリプト（例: `KEY Ctrl+C` → `WAIT 200` → `TEXT processed`）が直前ウィンドウへ送信された後にコマンドが実行されることを確認し、マウスコマンド（例: `MOUSEMOVEABS 200 300` → `MOUSELEFTCLICK` → `MOUSESCROLLDOWN 2`）も意図した操作になることを確認。`SET Count 0` → `ADD Count 1` → `TEXT {{Count}}` や `SET Message Hello` → `APPEND Message !` → `TEXT {{Message}}` を実行して演算結果が展開されること、0 除算などはエラーになることを確認。Macro Script 欄の「?」ボタンで Tips を開き、ダイアログ操作を継続しながら内容を参照できることを確認。
  6) 常に最前面トグルを OFF/ON し、切替直後と再起動後の状態を確認。
  7) レイヤーボタン/ホイールで循環切替し、ドラッグ中 0.8s で自動切替されることを確認。
  8) Slot Layout メニューで別の行列（例: 3x3）を選択し、即時に UI が再構成され再起動後も構成が保持されることを確認。元のレイアウトへ戻す。
  9) ウィンドウ位置を移動→再起動後に復元。画面外に移動しても補正されることを確認。
  10) スロット hover/drag/クリックの視覚変化を確認し、クリック有効/無効トグルが尊重されることを確認。
  11) メニューボタンから Open Config/Open Logs/Change Prefix/Slot Layout/Exit が動作することを確認（Open Logs はフォルダを開く）。
 12) Prefix（例: Ctrl+Q）を押下して左上インジケーター点灯→修飾キーを押し直さずに `X` を押し `Ctrl+X` ショートカットが起動すること、必要に応じ修飾キーを離して押し直しても動作すること、Prefix 再入力で前面ウィンドウへ送出されること、Prefix 変更ダイアログで不正入力時にエラー表示・既定値フォールバックが行われることを確認。
 13) エクスプローラーでファイル/フォルダをコピーし、`ArgumentsTemplate` に `{clipboard_args}` を指定したスロットをショートカット起動してクリップボードのパスが引用付きで渡されること、および `{clipboard}` 指定で生文字列が渡されることを確認。
 14) `%AppData%/DropSendTo/logs` にテスト用ログを作成し、1MB 超でローテーションすることと、7 日より古いファイルが `CleanupOldLogs` 後に削除されることを確認（ファイルの最終更新日時を調整して検証）。

## Environment & Data
- OS: Windows 10 22H2+ / 11、.NET SDK 8.x。
- Data: 一時フォルダにテストファイル作成。`%AppData%/DropSendTo` はテスト毎にクリーン。
- Macro: マクロ試験では同一デスクトップの通常権限ウィンドウ（例: メモ帳）を事前にフォーカスしておく。

## Entry/Exit Criteria
- Entry: 主要仕様の実装、ログ/設定ディレクトリ作成可能。
- Exit: 重要 TC（010/020/021/025/030/035/040/045/065/085/086/087）合格、回帰なし。

## Reporting
- `dotnet test -l "trx;LogFileName=test_results.trx"` でレポート出力。

## Traceability (excerpt)
- FR-001 → SP-001 → DES-002/003 → TC-010/065
- FR-002 → SP-004 → DES-004 → TC-020/021
- FR-019 → SP-004/009 → DES-002/004 → TC-025/027/080
- FR-021 → SP-006/007 → DES-002/005 → TC-090/095
- FR-022 → SP-001/006 → DES-002/003 → TC-065
- FR-023 → SP-010 → DES-002/003 → TC-085/086
- FR-024 → SP-010 → DES-002/005 → TC-035/085/086
- FR-025 → SP-004/010 → DES-002/004 → TC-025/087
- NFR-003 → SP-001 → DES-003 → TC-040/045
