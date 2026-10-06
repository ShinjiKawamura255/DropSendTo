using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DropSendTo.Models;

namespace DropSendTo;

internal enum SettingsOption
{
    SlotLayout, SlotSize, HideEmptySlotNames, AlwaysOnTop, Theme,
    KeyboardPlacement, MousePlacement, SearchPlacement,
    EmacsNavigation, ViNavigation, LayerCount, StartupBehavior,
    StartupRegistration, MacroConcurrency, Language
}

internal enum SettingsAction
{
    CustomSlotSize, Prefix, SearchHotkey, MouseGestures, LayerNames,
    AppLayers, ShowLayerPreferences, StartupSlots, MinimizeTriggers,
    ExportConfig, ImportConfig, OpenConfig, OpenLogs
}

internal sealed record SlotLayoutSelection(int Rows, int Columns);

public partial class SettingsWindow : Window
{
    private readonly Func<AppConfig> _readConfig;
    private readonly Func<bool> _readStartupRegistration;
    private readonly Action<SettingsOption, object> _apply;
    private readonly Func<SettingsAction, Task> _execute;
    private readonly Dictionary<SettingsOption, Control> _controls = new();
    private readonly List<Action<AppConfig>> _refreshers = new();
    private readonly List<StackPanel> _pages = new();
    private readonly HashSet<SettingsAction> _actions = new();
    private StackPanel _page = null!;
    private AppLanguage? _language;
    private bool _refreshing;
    private bool _actionRunning;

    internal int CategoryCount => Categories.Items.Count;
    internal int ActionCount => _actions.Count;
    internal int SelectedCategory => Categories.SelectedIndex;
    internal Control GetSettingControl(SettingsOption option) => _controls[option];
    internal void SelectCategory(int index) => Categories.SelectedIndex = index;

    internal SettingsWindow(Func<AppConfig> readConfig, Func<bool> readStartupRegistration,
        Action<SettingsOption, object> apply, Func<SettingsAction, Task> execute)
    {
        _readConfig = readConfig;
        _readStartupRegistration = readStartupRegistration;
        _apply = apply;
        _execute = execute;
        InitializeComponent();
        RefreshSettings();
        Activated += (_, _) => RefreshSettings();
    }

    internal void RefreshSettings()
    {
        var config = _readConfig();
        SettingsOption? focusedOption = null;
        bool languageChanged = _language != config.Language;
        if (languageChanged)
        {
            foreach (var pair in _controls)
                if (pair.Value.IsKeyboardFocusWithin) focusedOption = pair.Key;
        }
        _refreshing = true;
        try
        {
            if (_language != config.Language)
            {
                _language = config.Language;
                BuildPages();
            }
            foreach (var refresh in _refreshers) refresh(config);
            if (focusedOption is SettingsOption option) _controls[option].Focus();
        }
        finally { _refreshing = false; }
    }

    private string Text(string japanese, string english) =>
        _language == AppLanguage.English ? english : japanese;

    private void BuildPages()
    {
        int selected = Math.Max(0, Categories.SelectedIndex);
        _controls.Clear();
        _refreshers.Clear();
        _pages.Clear();
        _actions.Clear();
        Pages.Children.Clear();
        Categories.Items.Clear();
        Title = Text("設定", "Settings");
        HeadingText.Text = Text("DropSendTo の設定", "DropSendTo settings");
        HelpText.Text = Text("選択・チェックの変更はすぐに保存されます。行列・タブ数は「適用」で確定します。",
            "Choices and checkboxes are saved immediately. Use Apply for the grid and tab count.");
        CloseButton.Content = Text("閉じる", "Close");

        AddPage(Text("表示・レイアウト", "Display & layout"));
        AddLayout();
        AddChoice(SettingsOption.SlotSize, Text("スロットサイズ", "Slot size"),
            Text("文字の大きさとスロットの寸法を変更します。", "Change text size and slot dimensions."),
            c => c.SlotSize, new[]
            {
                (SlotSize.Small, Text("小", "Small")), (SlotSize.Medium, Text("中", "Medium")),
                (SlotSize.Large, Text("大", "Large")), (SlotSize.Custom, Text("カスタム", "Custom"))
            });
        AddAction(SettingsAction.CustomSlotSize, Text("カスタムサイズを編集…", "Edit custom size…"),
            Text("高さ・フォント・間隔を細かく調整できます。", "Fine-tune height, fonts and spacing."));
        AddCheck(SettingsOption.HideEmptySlotNames, Text("未登録スロット名を非表示", "Hide empty slot names"),
            c => c.HideEmptySlotNames);
        AddCheck(SettingsOption.AlwaysOnTop, Text("常に最前面に表示", "Always on top"), c => c.AlwaysOnTop);
        AddChoice(SettingsOption.Theme, Text("テーマ", "Theme"), "", c => c.Theme,
            new[] { (AppTheme.Dark, Text("ダーク", "Dark")), (AppTheme.Light, Text("ライト", "Light")) });
        AddPlacement(SettingsOption.KeyboardPlacement, Text("表示位置：キーボード", "Placement: keyboard"),
            c => (int)c.KeyboardPlacementMode, false);
        AddPlacement(SettingsOption.MousePlacement, Text("表示位置：マウスジェスチャ", "Placement: mouse gesture"),
            c => c.MousePlacementFollowsKeyboard ? -1 : (int)c.MousePlacementMode, true);
        AddPlacement(SettingsOption.SearchPlacement, Text("表示位置：検索", "Placement: search"),
            c => c.SearchPlacementFollowsKeyboard ? -1 : (int)c.SearchPlacementMode, true);

        AddPage(Text("キーボード・マウス", "Keyboard & mouse"));
        AddAction(SettingsAction.Prefix, Text("Prefix を変更…", "Change Prefix…"),
            c => Text("現在：", "Current: ") + (c.ShortcutPrefixDisabled ? Text("無効", "Disabled") : c.ShortcutPrefix));
        AddAction(SettingsAction.SearchHotkey, Text("検索ホットキー…", "Search hotkey…"),
            c => Text("現在：", "Current: ") + (c.SearchHotkeyEnabled ? c.SearchHotkey : Text("無効", "Disabled")));
        AddCheck(SettingsOption.EmacsNavigation, Text("Emacs ライク操作", "Emacs-like navigation"),
            c => c.EnableEmacsNavigation);
        AddCheck(SettingsOption.ViNavigation, Text("vi ライク操作", "vi-like navigation"), c => c.EnableViNavigation);
        AddAction(SettingsAction.MouseGestures, Text("マウスジェスチャ・ドロップ捕捉…", "Mouse gestures & drop capture…"),
            c => Text("ジェスチャ：", "Gestures: ") + (c.EnableMouseGestures ? Text("有効", "Enabled") : Text("無効", "Disabled")));

        AddPage(Text("タブ・切替", "Tabs & switching"));
        AddLayerCount();
        AddAction(SettingsAction.LayerNames, Text("タブ名を編集…", "Edit tab names…"),
            c => string.Join(" / ", c.Layers.Select((l, i) => string.IsNullOrWhiteSpace(l.Name) ? $"{i + 1}" : l.Name)));
        AddAction(SettingsAction.AppLayers, Text("アプリ別タブ切替…", "App-based tab switching…"),
            c => c.AppLayerSwitchingEnabled ? Text("自動切替：有効", "Automatic switching: enabled") :
                Text("自動切替：無効", "Automatic switching: disabled"));
        AddAction(SettingsAction.ShowLayerPreferences, Text("表示時のタブ切替…", "Tab selection when showing…"),
            Text("Prefix・マウス操作で表示するときのタブを選びます。", "Choose which tab appears for Prefix and mouse actions."));

        AddPage(Text("起動・実行", "Startup & execution"));
        AddCheck(SettingsOption.StartupRegistration, Text("Windows のサインイン時に起動", "Run at Windows sign-in"),
            _ => _readStartupRegistration());
        AddChoice(SettingsOption.StartupBehavior, Text("起動時のウィンドウ", "Startup window"),
            Text("次回起動時から使用します。", "Used the next time the app starts."), c => c.StartupBehavior,
            new[]
            {
                (StartupWindowBehavior.AlwaysShow, Text("常に表示", "Always show")),
                (StartupWindowBehavior.StartInTray, Text("タスクトレイで起動", "Start in tray")),
                (StartupWindowBehavior.RestoreLastState, Text("前回の状態を復元", "Restore last state"))
            });
        AddAction(SettingsAction.StartupSlots, Text("起動時実行スロット…", "Startup slots…"),
            Text("起動時に実行するスロットを編集します。", "Choose which slots run at startup."));
        AddAction(SettingsAction.MinimizeTriggers, Text("最小化条件…", "Minimize triggers…"),
            Text("スロット実行後にトレイへ格納する条件を編集します。", "Edit when slot execution sends the window to the tray."));
        AddChoice(SettingsOption.MacroConcurrency, Text("マクロ実行モード", "Macro execution mode"),
            Text("別のマクロを実行したときの扱いです。", "What happens when another macro is started."), c => c.MacroConcurrencyMode,
            new[]
            {
                (MacroConcurrencyMode.Exclusive, Text("排他（単一実行のみ）", "Exclusive (one at a time)")),
                (MacroConcurrencyMode.Interrupt, Text("実行中マクロを停止して割り込み", "Stop the current macro")),
                (MacroConcurrencyMode.SuspendAndResume, Text("一時停止して割り込み・完了後に再開", "Pause, interrupt and resume"))
            });

        AddPage(Text("一般・データ管理", "General & data"));
        AddChoice(SettingsOption.Language, "言語 / Language", "", c => c.Language,
            new[] { (AppLanguage.Japanese, "日本語"), (AppLanguage.English, "English") });
        AddAction(SettingsAction.ExportConfig, Text("設定をエクスポート…", "Export settings…"),
            Text("スロットを含む設定をパスワードで暗号化して保存します。", "Save settings and slots in a password-encrypted file."));
        AddAction(SettingsAction.ImportConfig, Text("設定をインポート…", "Import settings…"),
            Text("ファイルの設定を読み込み、内容を確認して置き換えます。", "Review and replace settings from an exported file."));
        AddAction(SettingsAction.OpenConfig, Text("設定ファイルを開く", "Open config file"),
            Text("手動編集はアプリを終了してから行ってください。", "Exit the app before editing the file manually."));
        AddAction(SettingsAction.OpenLogs, Text("ログフォルダーを開く", "Open log folder"),
            Text("動作の記録やエラーを確認します。", "Inspect activity and errors."));

        Categories.SelectedIndex = selected;
        ShowSelectedPage();
    }

    private void AddPage(string title)
    {
        Categories.Items.Add(title);
        _page = new StackPanel { Visibility = Visibility.Collapsed };
        _pages.Add(_page);
        Pages.Children.Add(_page);
        _page.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 18)
        });
    }

    private StackPanel AddRow(string title, string description)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        row.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (description.Length > 0)
            row.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        _page.Children.Add(row);
        return row;
    }

    private void AddCheck(SettingsOption option, string title, Func<AppConfig, bool> read)
    {
        var box = new CheckBox { Content = title, Margin = new Thickness(0, 0, 0, 18) };
        AutomationProperties.SetName(box, title);
        AutomationProperties.SetAutomationId(box, option.ToString());
        _controls.Add(option, box);
        _page.Children.Add(box);
        _refreshers.Add(c => box.IsChecked = read(c));
        box.Click += (_, _) => Apply(option, box.IsChecked == true);
    }

    private sealed record Choice<T>(T Value, string Label);

    private void AddChoice<T>(SettingsOption option, string title, string description,
        Func<AppConfig, T> read, IEnumerable<(T Value, string Label)> choices)
    {
        var row = AddRow(title, description);
        var combo = CreateChoice(title, choices);
        _controls.Add(option, combo);
        AutomationProperties.SetAutomationId(combo, option.ToString());
        row.Children.Add(combo);
        _refreshers.Add(c => combo.SelectedValue = read(c));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedValue is T value) Apply(option, value!);
        };
    }

    private static ComboBox CreateChoice<T>(string title, IEnumerable<(T Value, string Label)> choices)
    {
        var combo = new ComboBox
        {
            ItemsSource = choices.Select(c => new Choice<T>(c.Value, c.Label)).ToArray(),
            DisplayMemberPath = "Label",
            SelectedValuePath = "Value",
            MinHeight = 32,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(combo, title);
        return combo;
    }

    private void AddPlacement(SettingsOption option, string title, Func<AppConfig, int> read, bool allowFollow)
    {
        var choices = new List<(int, string)>();
        if (allowFollow) choices.Add((-1, Text("キーボード設定に追従", "Follow keyboard setting")));
        choices.Add((0, Text("固定位置", "Fixed position")));
        choices.Add((1, Text("マウスフォロー", "Follow mouse")));
        choices.Add((2, Text("画面中央（マウスがある画面）", "Center of the cursor's screen")));
        AddChoice(option, title, "", read, choices);
    }

    private void AddLayout()
    {
        var row = AddRow(Text("スロットの行列", "Slot grid"), Text("行・列はそれぞれ 2〜8 です。", "Rows and columns each range from 2 to 8."));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var choices = Enumerable.Range(2, 7).Select(i => (i, i.ToString())).ToArray();
        var rows = CreateChoice(Text("行", "Rows"), choices);
        var columns = CreateChoice(Text("列", "Columns"), choices);
        var label = new TextBlock { Text = "×", Margin = new Thickness(10, 8, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var button = ApplyButton();
        Grid.SetColumn(label, 1);
        Grid.SetColumn(columns, 2);
        Grid.SetColumn(button, 3);
        grid.Children.Add(rows);
        grid.Children.Add(label);
        grid.Children.Add(columns);
        grid.Children.Add(button);
        row.Children.Add(grid);
        _controls.Add(SettingsOption.SlotLayout, rows);
        _refreshers.Add(c => { rows.SelectedValue = c.SlotRows; columns.SelectedValue = c.SlotColumns; });
        button.Click += (_, _) =>
        {
            if (rows.SelectedValue is int r && columns.SelectedValue is int c)
                Apply(SettingsOption.SlotLayout, new SlotLayoutSelection(r, c));
        };
    }

    private void AddLayerCount()
    {
        var row = AddRow(Text("タブ数", "Tab count"),
            Text("4〜8 タブ。減らすと末尾のタブと登録スロットが削除されます。", "4 to 8 tabs. Reducing the count removes trailing tabs and their slots."));
        var panel = new DockPanel();
        var button = ApplyButton();
        DockPanel.SetDock(button, Dock.Right);
        panel.Children.Add(button);
        var combo = CreateChoice(Text("タブ数", "Tab count"), Enumerable.Range(4, 5).Select(i => (i, i.ToString())));
        panel.Children.Add(combo);
        row.Children.Add(panel);
        _controls.Add(SettingsOption.LayerCount, combo);
        _refreshers.Add(c => combo.SelectedValue = c.Layers.Count);
        button.Click += (_, _) =>
        {
            if (combo.SelectedValue is int count) Apply(SettingsOption.LayerCount, count);
        };
    }

    private Button ApplyButton() => new()
    {
        Content = Text("適用", "Apply"),
        MinWidth = 76,
        MinHeight = 32,
        Margin = new Thickness(10, 8, 0, 0),
        Padding = new Thickness(12, 6, 12, 6)
    };

    private void AddAction(SettingsAction action, string title, string description) =>
        AddAction(action, title, _ => description);

    private void AddAction(SettingsAction action, string title, Func<AppConfig, string> description)
    {
        _actions.Add(action);
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var button = new Button
        {
            Content = title,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 8, 12, 8),
            MinHeight = 34
        };
        AutomationProperties.SetAutomationId(button, action.ToString());
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(button);
        row.Children.Add(summary);
        _page.Children.Add(row);
        _refreshers.Add(c => summary.Text = description(c));
        button.Click += async (_, _) =>
        {
            if (_actionRunning) return;
            _actionRunning = true;
            Pages.IsEnabled = false;
            StatusText.Text = string.Empty;
            try { await _execute(action); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
            finally
            {
                _actionRunning = false;
                Pages.IsEnabled = true;
                if (IsVisible) RefreshSettings();
            }
        };
    }

    private void Apply(SettingsOption option, object value)
    {
        if (_refreshing || _actionRunning) return;
        StatusText.Text = string.Empty;
        try { _apply(option, value); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        RefreshSettings();
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e) => ShowSelectedPage();

    private void ShowSelectedPage()
    {
        for (int i = 0; i < _pages.Count; i++)
            _pages[i].Visibility = i == Categories.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        PageScroll.ScrollToTop();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
