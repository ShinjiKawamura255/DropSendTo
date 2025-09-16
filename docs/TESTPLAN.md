# TESTPLAN

## Scope & Levels
- Scope: ランチャ挙動、登録/永続化、レイヤー切替、エラーハンドリング、UI 表示要件。
- Levels: unit（services）, integration（config+launcher）, manual e2e（UI）。

## Framework & Runner
- Unit/Integration: xUnit（`dotnet test`）。
- Manual: チェックリストで確認（スクショ取得）。

## Test Cases (excerpt)
- TC-001 Platform: Windows の .NET 8 でビルド/実行できる。
- TC-010 Slots: 16 スロットの登録/解除/復元。
- TC-020 Launch: ファイル/フォルダのドロップで引数渡し起動。
- TC-021 CLI: コマンドライン引数で同等起動。
- TC-030 Errors: 実行不可パス/権限不足/タイムアウト時のユーザー通知とログ出力。
- TC-040 UI: 常時最前面、黒基調、半透明（テキスト可読）。

## Execution
- Unit: `dotnet test tests/DropSendTo.Tests -c Release`。
- Manual: 手順
  1) アプリ起動→最前面/半透明確認。
  2) スロット登録→再起動後も保持。
  3) ドロップ/CLI 起動の双方で期待動作。
  4) 想定エラーで通知/ログ確認。

## Environment & Data
- OS: Windows 10 22H2+ / 11、.NET SDK 8.x。
- Data: 一時フォルダにテストファイル作成。`%AppData%/DropSendTo` はテスト毎にクリーン。

## Entry/Exit Criteria
- Entry: 主要仕様の実装、ログ/設定ディレクトリ作成可能。
- Exit: 重要 TC（010/020/030/040）合格、回帰なし。

## Reporting
- `dotnet test -l "trx;LogFileName=test_results.trx"` でレポート出力。

## Traceability (excerpt)
- FR-002 → SP-004 → DES-004 → TC-020/021
- NFR-003 → SP-001 → DES-003 → TC-040
