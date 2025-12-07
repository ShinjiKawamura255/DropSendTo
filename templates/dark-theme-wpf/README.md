Dark theme resource templates for WPF. Copy these into another project (e.g., `Themes/`) and merge them in `App.xaml`.

## ファイル構成
- `Colors.xaml`: パレット定義。背景/前景/ボーダー/アクセントのブラシ。
- `Controls.xaml`: 共通のダークスタイル。Window/ボタン/テキスト入力/ComboBox(ポップアップ含む)/Menu/ContextMenu/Separator/ScrollBar。

## 使い方
1) プロジェクト内に `Themes/` などのフォルダを作り、`Colors.xaml` と `Controls.xaml` を配置。
2) `App.xaml` に以下のようにマージして読み込み（必ず Colors → Controls の順で追加）。

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceDictionary Source="pack://application:,,,/YourAssembly;component/Themes/Colors.xaml" />
      <ResourceDictionary Source="pack://application:,,,/YourAssembly;component/Themes/Controls.xaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

## メモ
- Menu/ContextMenu のシステムブラシを上書きするので、アプリ全体のメニューが即ダーク化されます。
- ScrollBar は ControlTemplate を含めているため、既存スタイルと競合しないように必要に応じてキー付きスタイルに変えてください。
- 必要に応じてカラーを変更する場合は `Colors.xaml` のブラシ値を調整してください（`Controls.xaml` の依存元です）。
