# Documentation Index

このディレクトリの文書マップです。挙動変更時は責務に対応する正本だけを更新し、FR/NFR → SP → DES → TC の追跡を維持します。

## Current truth and navigation

- `CURRENT_STATUS.md`: 現在のリポジトリ状態、標準検証コマンド、既知の未解決事項。
- `../README.md`: 開発者・利用者向けの短い導入。
- `../USER_GUIDE.md`: エンドユーザー向けの操作方法とトラブルシューティング。
- `../AGENTS.md`: エージェントと保守者向けの作業規約。

## Normative SDD/TDD

- `REQUIREMENTS.md`: 目的、スコープ、FR/NFR、受入条件。実装方法は所有しない。
- `SPEC.md`: 外部から観測できる振る舞い、境界値、不変条件。内部構造は所有しない。
- `DESIGN.md`: コンポーネント境界、主要フロー、API 契約、エラー方針。
- `DETAILED_DESIGN.md`: 実装に即したモジュール詳細、複雑な制御フロー、運用・テスト戦略。
- `TESTPLAN.md`: TC、実行方法、環境、合否基準、追跡可能性。

## Focused references

- `MACRO_SAMPLES.md`: 現行 Macro Script だけで動くコピー＆ペースト用サンプル。
