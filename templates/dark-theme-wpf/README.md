Dark theme resource templates for WPF. Copy these into another project (e.g., `Themes/`) and merge them in `App.xaml`.

## ファイル構成
- `Colors.xaml`: パレット定義。背景/前景/ボーダー/アクセントのブラシ。
- `Controls.xaml`: 共通のダークスタイル。Window/ボタン/テキスト入力/ComboBox(ポップアップ含む)/Menu/ContextMenu/Separator/ScrollBar。
- `WindowChrome.xaml`: カスタムタイトルバー付きの Window スタイル。最小化/最大化/閉じるボタンを自前で描画。

## 使い方
1) プロジェクト内に `Themes/` などのフォルダを作り、`Colors.xaml` と `Controls.xaml` を配置。
   カスタムタイトルバーを使う場合は `WindowChrome.xaml` もコピーしてください。
2) `App.xaml` に以下のようにマージして読み込み（必ず Colors → Controls → WindowChrome の順で追加）。

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceDictionary Source="pack://application:,,,/YourAssembly;component/Themes/Colors.xaml" />
      <ResourceDictionary Source="pack://application:,,,/YourAssembly;component/Themes/Controls.xaml" />
      <ResourceDictionary Source="pack://application:,,,/YourAssembly;component/Themes/WindowChrome.xaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

3) カスタムタイトルバーを適用したい Window にスタイルを設定。

```xml
<Window x:Class="Sample.MainWindow"
        ...
        Style="{StaticResource DarkWindowChromeStyle}">
    <!-- コンテンツ -->
</Window>
```

4) SystemCommands のルーティングを有効にするために、Window のコードビハインドで CommandBindings を追加してください（XAML側でコマンドをバインド済みなのでハンドラはこれだけでOK）。

```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, e) => SystemCommands.CloseWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, e) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand,  (_, e) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, e) => SystemCommands.RestoreWindow(this)));
    }
}
```

## メモ
- Menu/ContextMenu のシステムブラシを上書きするので、アプリ全体のメニューが即ダーク化されます。
- ScrollBar は ControlTemplate を含めているため、既存スタイルと競合しないように必要に応じてキー付きスタイルに変えてください。
- 必要に応じてカラーを変更する場合は `Colors.xaml` のブラシ値を調整してください（`Controls.xaml` の依存元です）。
