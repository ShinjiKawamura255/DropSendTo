# TESTPLAN

## Scope & Levels
- Scope: ランチャ挙動、登録/永続化、レイヤー切替、キーボードマクロ、CLI フロー、ログアクセス、エラーハンドリング、UI 表示要件。
- Levels: unit（services）, integration（config+launcher+macro）, manual e2e（UI）。

## Framework & Runner
- Unit/Integration: xUnit（`dotnet test`）。
- Manual: チェックリストで確認（スクショ取得）。

## Test Cases (excerpt)
- TC-001 Platform: Windows の .NET 8 でビルド/実行できる。
- TC-010 Slots: 16 スロットの登録/解除/復元と各種フィールド（タイトル/コマンド/マクロ/クリック可否）の保存。
- TC-020 LaunchDrop: ファイル/フォルダのドロップで `{args}` がクォートされ、登録コマンドに渡る。
- TC-021 LaunchCli: CLI 引数で現在レイヤー優先→全レイヤー検索が行われる。失敗時は UI 表示とログ出力。
- TC-025 MacroScriptHappy: `KEY`/`WAIT`/`TEXT`/`REPEAT` など代表的な命令が期待通り展開・送信される。
- TC-026 MacroScriptValidation: 不正なキー名/REPEAT 上限超過/未閉鎖ブロックでエラーが返る。
- TC-030 Errors: 実行不可パスやマクロエラーでメッセージが表示され、ログに ERROR が残る。
- TC-040 UiTheme: 常時最前面が既定で ON、黒基調・半透明・角丸・ボタンスタイルが維持される。
- TC-045 AlwaysOnTopToggle: メニューから常時最前面を切り替え、設定保存後も状態が復元される。
- TC-050 Layer: ボタン/ホイールの循環切替、ドラッグホバー 0.8s で自動切替、ハイライト更新。
- TC-060 WindowPos: 再起動で位置復元し、画面外に移動しても可視範囲へ補正される。
- TC-070 SlotStates: hover/drag/クリックで視覚状態が変化する（UI レベル）。
- TC-080 ClickExec: クリック実行が有効なときのみマクロ→コマンドが動く。無効設定は保存される。
- TC-090 MenuAccess: Open Config/Open Logs/Exit/常に最前面トグルが機能し、Open Logs がディレクトリを開く。
- TC-095 LoggingRetention: ログが 1MB 超でローテーションし、7 日以上前の `app*.log` が削除される。

## Execution
- Unit: `dotnet test tests/DropSendTo.Tests -c Release`。
- Manual: 手順
  1) アプリ起動→常時最前面・半透明・角丸・ボタンスタイルを確認。
  2) スロットを Edit で登録（Browse 使用、タイトル反映）→再起動後も設定/クリック有効が保持されることを確認。
  3) ファイル/フォルダをドロップして `{args}` が正しく渡ることを確認。
  4) CLI 引数で起動し、優先スロットが実行され UI が終了することを確認。登録なしの場合は UI 継続とメッセージ表示。
  5) マクロスクリプト（例: `KEY Ctrl+C` → `WAIT 200` → `TEXT processed`）が直前ウィンドウへ送信された後にコマンドが実行されることを確認。
  6) 常に最前面トグルを OFF/ON し、切替直後と再起動後の状態を確認。
  7) レイヤーボタン/ホイールで循環切替し、ドラッグ中 0.8s で自動切替されることを確認。
  8) ウィンドウ位置を移動→再起動後に復元。画面外に移動しても補正されることを確認。
  9) スロット hover/drag/クリックの視覚変化を確認し、クリック有効/無効トグルが尊重されることを確認。
  10) メニューボタンから Open Config/Open Logs/Exit が動作することを確認（Open Logs はフォルダを開く）。
  11) `%AppData%/DropSendTo/logs` にテスト用ログを作成し、1MB 超でローテーションすることと、7 日より古いファイルが `CleanupOldLogs` 後に削除されることを確認（ファイルの最終更新日時を調整して検証）。

## Environment & Data
- OS: Windows 10 22H2+ / 11、.NET SDK 8.x。
- Data: 一時フォルダにテストファイル作成。`%AppData%/DropSendTo` はテスト毎にクリーン。
- Macro: マクロ試験では同一デスクトップの通常権限ウィンドウ（例: メモ帳）を事前にフォーカスしておく。

## Entry/Exit Criteria
- Entry: 主要仕様の実装、ログ/設定ディレクトリ作成可能。
- Exit: 重要 TC（010/020/021/025/030/040/045）合格、回帰なし。

## Reporting
- `dotnet test -l "trx;LogFileName=test_results.trx"` でレポート出力。

## Traceability (excerpt)
- FR-002 → SP-004 → DES-004 → TC-020/021
- FR-019 → SP-004/009 → DES-002/004 → TC-025/080
- FR-021 → SP-006/007 → DES-002/005 → TC-090/095
- NFR-003 → SP-001 → DES-003 → TC-040/045
