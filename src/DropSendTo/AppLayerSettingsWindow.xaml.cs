using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

public partial class AppLayerSettingsWindow : Window, IConfirmableDialog
{
    private readonly bool _english;
    private readonly AppConfig _previewConfig;
    private readonly ObservableCollection<AppLayerRule> _rules;
    private ActiveWindowInfo? _target;
    private bool _closed;
    private readonly CancellationTokenSource _windowEnumeration = new();

    public bool IsConfirmed { get; private set; }
    public bool SwitchingEnabled => EnableBox.IsChecked == true;
    public int FallbackLayer => FallbackBox.SelectedValue is int layer ? layer : -1;
    public List<AppLayerRule> Rules => _rules.Select(rule => rule.Clone()).ToList();

    internal AppLayerSettingsWindow(AppConfig config, ActiveWindowInfo? target)
    {
        InitializeComponent();
        _english = config.Language == AppLanguage.English;
        _previewConfig = new AppConfig { Layers = config.Layers, CurrentLayer = config.CurrentLayer };
        _rules = new ObservableCollection<AppLayerRule>(config.AppLayerRules.Select(rule => rule.Clone()));
        Title = Text("アプリ別タブ切替", "App-based tab switching");
        EnableBox.Content = Text("アクティブなアプリに応じてタブを自動選択する", "Automatically select a tab for the active app");
        EnableBox.IsChecked = config.AppLayerSwitchingEnabled;
        DescriptionText.Text = Text(
            "セルをダブルクリックして編集します。上から最初に一致したルールを使用し、実行ファイル名は完全一致、タイトルは部分一致です（大小文字を区別しません）。空欄の条件は使用しません。DropSendTo上では自由にタブを切り替えられ、外部アプリへ戻ると追従を再開します。",
            "Double-click a cell to edit. The first matching rule wins. Executable names match exactly; titles match a substring (case-insensitive). Empty optional conditions are ignored. You can switch tabs in DropSendTo; following resumes when you return to an external app.");
        FallbackLabel.Text = Text("一致しないアプリの表示", "Unmatched apps");
        var choices = config.Layers.Select((layer, index) => new LayerChoice(index,
            $"{index + 1}: {(string.IsNullOrEmpty(layer.Name) ? Text("タブ", "Tab") : layer.Name)}")).ToList();
        FallbackBox.ItemsSource = new[] { new LayerChoice(-1, Text("最後に手動選択したタブ", "Last manually selected tab")) }.Concat(choices);
        FallbackBox.SelectedValue = config.AppLayerFallbackLayer;
        LayerColumn.ItemsSource = choices;
        RulesGrid.ItemsSource = _rules;
        EnabledColumn.Header = Text("有効", "On");
        ExecutableColumn.Header = Text("実行ファイル名", "Executable name");
        TitleColumn.Header = Text("タイトル条件", "Title contains");
        LayerColumn.Header = Text("切替先タブ", "Target tab");
        PathColumn.Header = Text("フルパス限定", "Full path");
        AddLastButton.Content = Text("直前のアプリから追加", "Add last external app");
        AddEmptyButton.Content = Text("手入力で追加", "Add manually");
        RefreshWindowsButton.Content = Text("ウィンドウ一覧を更新", "Refresh windows");
        WindowsBox.ToolTip = Text("開いているウィンドウ", "Open windows");
        AddWindowButton.Content = Text("選択したアプリを追加", "Add selected app");
        UpButton.Content = Text("↑ 優先順位を上げる", "↑ Move up");
        DownButton.Content = Text("↓ 優先順位を下げる", "↓ Move down");
        RemoveButton.Content = Text("削除", "Remove");
        SaveButton.Content = Text("保存", "Save");
        CancelButton.Content = Text("キャンセル", "Cancel");
        _rules.CollectionChanged += (_, _) => RefreshPreview();
        Closed += (_, _) =>
        {
            _closed = true;
            _windowEnumeration.Cancel();
            _windowEnumeration.Dispose();
        };
        UpdateTarget(target);
    }

    private string Text(string japanese, string english) => _english ? english : japanese;

    internal void UpdateTarget(ActiveWindowInfo? target)
    {
        _target = target;
        AddLastButton.IsEnabled = target != null;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_rules == null) return;
        _previewConfig.AppLayerRules = _rules.ToList();
        var match = AppLayerRuleMatcher.FindMatch(_previewConfig, _target);
        int layer = match.HasValue ? _rules[match.Value].LayerIndex :
            FallbackLayer >= 0 ? FallbackLayer : _previewConfig.CurrentLayer;
        TargetText.Text = _target == null ? Text("対象アプリを一度アクティブにしてください。", "Activate a target app first.") :
            Text($"現在の対象：{_target}\n一致ルール：{(match.HasValue ? (match.Value + 1).ToString() : "なし")} ／ 切替先：タブ {layer + 1}",
                $"Current target: {_target}\nMatching rule: {(match.HasValue ? (match.Value + 1).ToString() : "none")} / Target: tab {layer + 1}");
    }

    private void OnAddLast(object sender, RoutedEventArgs e) { if (_target != null) AddRule(_target); }
    private void OnAddEmpty(object sender, RoutedEventArgs e) => AddRule(null);
    private void OnAddWindow(object sender, RoutedEventArgs e)
    {
        if (WindowsBox.SelectedItem is ActiveWindowInfo info) AddRule(info);
    }

    private void AddRule(ActiveWindowInfo? window)
    {
        CommitEdits();
        var rule = new AppLayerRule { ExecutableName = window?.ExecutableName ?? string.Empty };
        _rules.Add(rule);
        RulesGrid.SelectedItem = rule;
        RulesGrid.ScrollIntoView(rule);
    }

    private async void OnRefreshWindows(object sender, RoutedEventArgs e)
    {
        RefreshWindowsButton.IsEnabled = false;
        try
        {
            var windows = await NativeActiveWindowSource.EnumerateAsync(_windowEnumeration.Token);
            if (_closed) return;
            WindowsBox.ItemsSource = windows;
            WindowsBox.SelectedIndex = windows.Count > 0 ? 0 : -1;
        }
        catch (Exception)
        {
            if (!_closed) ErrorText.Text = Text("ウィンドウ一覧を取得できませんでした。手入力で登録できます。",
                "Could not list windows. You can add a rule manually.");
        }
        finally { if (!_closed) RefreshWindowsButton.IsEnabled = true; }
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveRule(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveRule(1);
    private void MoveRule(int delta)
    {
        CommitEdits();
        int index = RulesGrid.SelectedIndex;
        if (index >= 0 && index + delta >= 0 && index + delta < _rules.Count) _rules.Move(index, index + delta);
    }
    private void OnRemove(object sender, RoutedEventArgs e)
    {
        CommitEdits();
        if (RulesGrid.SelectedItem is AppLayerRule rule) _rules.Remove(rule);
    }
    private void CommitEdits()
    {
        RulesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        CommitEdits();
        for (int i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            rule.ExecutableName = rule.ExecutableName.Trim();
            rule.TitleContains = rule.TitleContains.Trim();
            rule.ExecutablePath = rule.ExecutablePath.Trim();
            if (rule.Enabled && (string.IsNullOrWhiteSpace(rule.ExecutableName) ||
                rule.ExecutableName.IndexOfAny(new[] { '\\', '/' }) >= 0 ||
                rule.LayerIndex < 0 || rule.LayerIndex >= _previewConfig.Layers.Count ||
                (rule.ExecutablePath.Length > 0 && !Path.IsPathFullyQualified(rule.ExecutablePath))))
            {
                ErrorText.Text = Text($"ルール {i + 1}：実行ファイル名と切替先を確認してください。フルパス条件は絶対パスで指定します。",
                    $"Rule {i + 1}: check the executable name and target tab. The optional full path must be absolute.");
                return;
            }
        }
        IsConfirmed = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
    private void OnPreviewChanged(object sender, SelectionChangedEventArgs e) => RefreshPreview();
    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(RefreshPreview));

    private sealed record LayerChoice(int Index, string Name);
}
