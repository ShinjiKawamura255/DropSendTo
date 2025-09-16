# DESIGN

## DES-001 Architecture Overview
- Platform: .NET 8 / Windows デスクトップ（WPF 推奨）。単一プロセス常駐アプリ。
- Persistence: `%AppData%/DropSendTo/config.json`（JSON）+ ログファイル。

## DES-002 Components
- AppHost: 起動/終了、単一インスタンス制御、例外ハンドラ。
- MainWindow (AlwaysOnTop): 黒基調、20–40% 透過、テキスト不透明。タイトルバー無し。右上メニューボタン＋右クリックメニュー。
- SlotGrid: 2x2 表示、LayerManager と連動。ホイールでレイヤー切替。
- LayerManager: 現在レイヤー管理、切替 API（Next/Prev/Set、循環）。
- WindowPlacementService: Left/Top の保存・復元、可視領域のクランプ（仮想スクリーン）。
- SlotModel: `Id`, `Title`, `Command`, `ArgumentsTemplate`, `IconPath`, `ClickEnabled`。
- ConfigService: 読み書き/検証/マイグレーション/バックアップ。
- LauncherService: `Launch(SlotModel slot, string[] paths)` を提供。`ProcessStartInfo` で起動、失敗を分類。
- DragDropService: ドロップ検証、複数パス対応、重複排除。
- MenuService: スロット/アプリ全体のコンテキストメニュー生成。
- Logging: `logs\app.log` にローテーション出力（サイズ上限）。

## DES-003 UI Flows
1) 起動: ConfigService が JSON をロード→WindowPlacementService で位置補正→MainWindow 表示（最前面）。
2) ドロップ: SlotGrid がパス配列を受け取り DragDropService 検証→LauncherService に渡す。hover/dragover でスタイル変化。
3) 登録: スロット右クリック→登録ダイアログ→保存→UI 更新。
4) レイヤー切替: 上部ボタン/ホイール/メニュー→LayerManager 更新→SlotGrid/ボタン強調更新。
5) クリック実行: スロットクリック→クリック実行が有効なら LauncherService 起動。
6) 終了: メニューから終了→WindowPlacementService が位置保存→終了。

## DES-004 API Contracts (examples)
- LauncherService
  - Input: `SlotModel` と `paths[]`（0..n）。`ArgumentsTemplate` 中の `{args}` をパス連結で置換。
  - Output: 成功/失敗（例外含む）を返却し、失敗はユーザー表示用メッセージを含む。
- ConfigService
  - `Load()`: 設定とバージョン; 破損時は `.bak` 復元または既定値生成。
  - `Save()`: バリデーション後に原子的書き換え。
- LayerManager
  - `Current`, `Set(i)`, `Next()`, `Prev()`（1..4 を循環）。
- WindowPlacementService
  - `Clamp(left, top, bounds)` → 可視範囲内の座標を返す。

## DES-005 Errors/Timeout/Telemetry
- Errors: パス不正、実行不可、タイムアウト（既定 15s）を分類。ユーザーには簡潔文面＋詳細はログ。
- Logging: 起動/終了/登録変更/起動失敗を Info/Warn/Error で記録。
- Metrics: 起動回数/失敗率（将来の改善向け、現時点ではログのみ）。

## DES-006 Trade-offs
- WPF 採用で Windows 専用だが UI 制御が容易。WinUI3 への移行余地は保つ。

## Traceability (excerpt)
- DES-002 ← SP-001/002/006 → TC-010/011
- DES-004 ← SP-004/005/007 → TC-020/030
