# DropSendTo

[![CI](https://github.com/ShinjiKawamura255/DropSendTo/actions/workflows/ci.yml/badge.svg)](https://github.com/ShinjiKawamura255/DropSendTo/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

DropSendTo は .NET 10 (WPF) 製の軽量ランチャーです。  
エクスプローラーの SendTo を拡張するように、ドロップ/CLI 引数をスロット実行へ流し込める構成を持ちつつ、クリックや Prefix ショートカットでも高速に操作できます。  
さらにドロップ専用ウィンドウを使って、ドラッグ中でも対象ファイルを保持したままスロット選択へつなげられます。

DropSendTo is a lightweight launcher built with .NET 10 (WPF).  
It is structured as a functional SendTo extension: drop/CLI arguments are routed into slot execution while keeping a fast launcher workflow via slot clicks and global prefix shortcuts.  
It also provides a dedicated drop-capture window so you can keep drag context and pick the target slot after dropping.

## スクリーンショット / GIF
- 準備中 / Coming soon

## 特徴 / Key Features
- SendTo 拡張として使える入力導線（ファイルドロップ / `DropSendTo.exe "<path>"`） / SendTo-style input path via file drop and CLI argument
- 4〜8 レイヤー × 2〜8 行・2〜8 列のグリッド（最大 512 スロット） / 4-8 layers, 2-8 rows × 2-8 columns (up to 512 slots)
- クリック/Prefix ショートカット中心の軽量ランチャーフロー / Lightweight launcher-first flow with click and prefix shortcuts
- ドロップ専用ウィンドウ（ドラッグ中ホイールクリック、Prefix+Ctrl+D） / Dedicated drop-capture window (drag middle click, Prefix+Ctrl+D)
- ドロップ専用ウィンドウの `Dropped` 状態を使った後選択実行（クリック/キーボード/検索） / Post-drop slot execution (click/keyboard/search) with `Dropped` state
- マクロ実行とコマンド起動を組み合わせ可能 / Combine macro execution and command launch
- タスクトレイ常駐・最小化 / Tray residency and minimize to tray
- Prefix インジケーターとキーボード操作 / Prefix indicator and keyboard navigation
- マウスジェスチャで表示/最小化 / Mouse gesture show/minimize
- 設定のエクスポート/インポート / Encrypted config export/import

## 対応環境 / Requirements
- Windows 10 22H2 以降 / Windows 11
- .NET SDK 10.x

## クイックスタート / Quickstart
1. リポジトリをクローン / Clone the repo
   ```
   git clone git@github.com:ShinjiKawamura255/DropSendTo.git
   ```
2. ビルド / Build
   ```
   dotnet build
   ```
3. 起動 / Run
   ```
   dotnet run --project src/DropSendTo
   ```

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
- スクリプトは `DOTNET_EXE` が指定されていればその `dotnet.exe` を使い、未指定でも `%USERPROFILE%\.dotnet\dotnet.exe` があれば優先します。
  ```
  $env:DOTNET_EXE="$env:USERPROFILE\.dotnet\dotnet.exe"
  powershell -ExecutionPolicy Bypass -File .\scripts\Run-Tests-And-Build.ps1
  ```

### WSL から実行する場合 / Running from WSL
WSL では Windows 側の `dotnet` を PowerShell 経由で実行してください。

```
WIN_REPO=$(wslpath -w "$PWD")
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$WIN_REPO'; dotnet test"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$WIN_REPO'; dotnet build"
```

## ドキュメント / Documentation
- 現在状態 / Current status: `docs/CURRENT_STATUS.md`
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
