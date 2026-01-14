# DropSendTo

DropSendTo は .NET 8 (WPF) 製の常駐ランチャです。ファイル/フォルダをドラッグ＆ドロップするだけで、登録済みコマンドやマクロを実行できます。CLI 引数でも起動でき、グローバル Prefix+ショートカットで任意スロットを呼び出せます。

DropSendTo is a resident launcher built with .NET 8 (WPF). Drag and drop files/folders to run registered commands or macros. It also supports CLI arguments and global prefix shortcuts.

## 特徴 / Key Features
- 4 レイヤー × 2〜8 行・2〜4 列のグリッド (最大 128 スロット) / 4 layers, 2–8 rows × 2–4 columns (up to 128 slots)
- ドロップ/クリック/CLI/Prefix から共通の起動フロー / Unified launch flow from drop, click, CLI, or prefix
- マクロ実行とコマンド起動を組み合わせ可能 / Combine macro execution and command launch
- タスクトレイ常駐・最小化 / Tray residency and minimize to tray
- Prefix インジケーターとキーボード操作 / Prefix indicator and keyboard navigation
- マウスジェスチャで表示/最小化 / Mouse gesture show/minimize
- 設定のエクスポート/インポート / Encrypted config export/import

## 対応環境 / Requirements
- Windows 10 22H2 以降 / Windows 11
- .NET SDK 8.x

## クイックスタート / Quickstart
1. リポジトリをクローン / Clone the repo
2. ビルド / Build
   ```
   dotnet build
   ```
3. 起動 / Run
   ```
   dotnet run --project src/DropSendTo
   ```

### CLI 起動 / CLI Launch
```
src/DropSendTo/bin/Debug/net8.0-windows/DropSendTo.exe "C:\path\to\file.txt"
```
最初にマッチした登録スロットを実行し、成功時は UI を開かず終了します。

Runs the first matching registered slot and exits without opening the UI on success.

## 使い方 / Usage
- 詳細な手順は `USER_GUIDE.md` を参照 / See `USER_GUIDE.md` for detailed usage
- Prefix ショートカットの設定と動作は UI のメニューから変更可能 / Configure prefix shortcuts from the UI menu

## 設定とログ / Configuration and Logs
- 設定: `%AppData%\DropSendTo\config.json`（`.bak` 自動生成）
- ログ: `%AppData%\DropSendTo\logs\app.log`（約 1MB でローテーション、7 日保持）

## ビルド / テスト / 配布 / Build, Test, Release
- Build: `dotnet build`
- Test: `dotnet test`
- Format: `dotnet format`
- Release ビルド & ZIP 生成 / Release build & zip:
  ```
  powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Rid win-x64 -SelfContained:$false -Version vX.Y.Z
  ```

### WSL から実行する場合 / Running from WSL
WSL では Windows 側の `dotnet` を PowerShell 経由で実行してください。

```
WIN_REPO=$(wslpath -w "$PWD")
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$WIN_REPO'; dotnet test"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$WIN_REPO'; dotnet build"
```

## ドキュメント / Documentation
- 要件 / Requirements: `docs/REQUIREMENTS.md`
- 仕様 / Spec: `docs/SPEC.md`
- 設計 / Design: `docs/DESIGN.md`
- テスト計画 / Test Plan: `docs/TESTPLAN.md`

## コントリビュート / Contributing
`CONTRIBUTING.md` を参照してください。

See `CONTRIBUTING.md` for details.

## ライセンス / License
MIT License. See `LICENSE`.

## セキュリティ / Security
脆弱性の報告は `SECURITY.md` を参照してください。

See `SECURITY.md` for reporting guidelines.
