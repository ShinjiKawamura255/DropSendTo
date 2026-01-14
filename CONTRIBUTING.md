# Contributing

ありがとうございます。DropSendTo への貢献方法をまとめています。

## 開発環境
- Windows 10/11
- .NET SDK 8.x

WSL からビルド/テストする場合は PowerShell 経由で実行してください。

```
WIN_REPO=$(wslpath -w "$PWD")
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$WIN_REPO'; dotnet test"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Set-Location -LiteralPath '$WIN_REPO'; dotnet build"
```

## 進め方
1. 目的に近い Issue があればコメントし、なければ新規 Issue を作成してください。
2. 変更は小さく保ち、関連する変更だけを含めてください。
3. 実装変更がある場合はテストを追加/更新してください。
4. UI 変更がある場合はスクリーンショット/GIF を添付してください。

## コーディング規約
- C# 12 / .NET 8
- インデント: 4 spaces
- public API は XML ドキュメントを付与
- UI 文言の既定は日本語。追加する場合は英語も併記

## コミット/PR
- Conventional Commits を使用 (例: `feat(ui): add slot grid`)
- 原則「1 対応 = 1 コミット」
- PR では目的、影響範囲、Before/After、テスト結果を明記

## テスト
```
dotnet test
dotnet build
```

## 仕様/設計ドキュメント
機能追加や仕様変更がある場合は `docs/` 配下の REQUIREMENTS/SPEC/DESIGN/TESTPLAN を更新してください。

## 参考
詳細な方針は `AGENTS.md` を参照してください。
