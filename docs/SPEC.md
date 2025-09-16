# SPEC

## SP-001 UI/Window
- MUST: 2x2 グリッド表示、4 レイヤー切替（計 16 スロット）。
- MUST: 常時最前面、黒基調、ウィンドウ全体は 20–40% 透過。テキストは不透明で可読。

## SP-002 Slot Registration
- MUST: 右クリックから登録/編集/解除。対象は実行ファイル/ショートカット/任意引数を含む。
- MUST: 表示名/アイコン（取得可能なら）を表示。

## SP-003 Layer Control
- MUST: レイヤー 1–4 を切替（ホットキーまたはメニュー）。
- SHOULD: 最終選択レイヤーを記憶。

## SP-004 Launch Behavior
- MUST: ドロップされたパス（複数可）は登録先のコマンドへ引数で渡される。
- MUST: コマンドライン引数からも対象パスを受理し、直ちに同等起動が可能。
- MUST: 起動失敗時はエラーを表示し、イベント/ログを記録。

## SP-005 Persistence
- MUST: すべてのスロット/レイヤー設定は `%AppData%/DropSendTo/config.json` に保存。
- MUST: 起動時に検証し、破損時はバックアップから復元または既定値で再生成。

## SP-006 Context Menu
- MUST: スロット上の右クリックで登録/編集/解除、ウィンドウ右クリックで終了/設定/レイヤー切替。

## SP-007 Error Handling
- MUST: 例外は捕捉しユーザーに分かる文面で対処案を提示（パス不正、権限不足等）。
- SHOULD: 直近ログを添付できる場所を提示（ファイル保存先）。

## SP-008 Platform
- MUST: .NET 8（Windows）でビルド/実行。WSL の .NET は非対象。

### Examples
- 入力: `C:\path\file.txt` をスロット A（`notepad.exe {args}`）へドロップ → 出力: `notepad.exe "C:\path\file.txt"` が起動。
- 入力: `DropSendTo.exe "C:\path\file.txt"` → 同上。

### Invariants / Boundaries
- スロットは各レイヤー 4 件まで。空スロットは「未登録」表示。
- ドロップはファイル/フォルダ/ショートカットに限定。URL ドロップは未対応。

## Traceability (excerpt)
- SP-001 → DES-003 → TC-002/003
- SP-004 → DES-004 → TC-020/021
