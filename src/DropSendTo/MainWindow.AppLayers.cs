using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

public partial class MainWindow
{
    private AppLayerSelection _appLayerSelection = new(0);
    private ActiveWindowMonitor? _activeWindowMonitor;
    private AppLayerSettingsWindow? _appLayerSettingsWindow;
    private DispatcherTimer? _appLayerResumeTimer;
    private DateTime _appLayerDragUntilUtc;
    private bool _appLayerReadPending;

    private void InitializeAppLayerState()
    {
        _appLayerSelection = new AppLayerSelection(_currentLayer);
        _appLayerResumeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _appLayerResumeTimer.Tick += OnAppLayerResumeTick;
        // A short lease survives DragLeave/DragEnter transitions between child controls.
        AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(OnAppLayerDrag), true);
        AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(OnAppLayerDrag), true);
        AddHandler(DragDrop.PreviewDragLeaveEvent, new DragEventHandler(OnAppLayerDrag), true);
        AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(OnAppLayerDrop), true);
    }

    private void StartActiveWindowMonitor()
    {
        _activeWindowMonitor = new ActiveWindowMonitor(Dispatcher);
        _activeWindowMonitor.Changed += OnActiveWindowChanged;
        _activeWindowMonitor.ExternalTargetChanged += OnAppLayerExternalTargetChanged;
        _activeWindowMonitor.Failed += OnActiveWindowReadFailed;
        try { _activeWindowMonitor.Start(); }
        catch (Exception ex)
        {
            _logger.Warn($"Active window monitor setup failed ({ex.GetType().Name}).");
            DisposeAppLayerState();
        }
    }

    private void OnActiveWindowReadFailed(Exception ex)
    {
        _appLayerReadPending = false;
        _appLayerSelection.Observe(null, isOwnForeground: true);
        _appLayerSettingsWindow?.UpdateTarget(null);
        _logger.Warn($"Active window reader failed ({ex.GetType().Name}).");
        TryApplyAppLayer();
    }

    private void OnAppLayerExternalTargetChanged()
    {
        _appLayerReadPending = true;
        _appLayerSelection.RequestEvaluation();
        _appLayerResumeTimer?.Stop();
    }

    private void OnActiveWindowChanged(ActiveWindowInfo? window, bool ownForeground)
    {
        _appLayerReadPending = false;
        _appLayerSelection.Observe(window, ownForeground);
        _appLayerSettingsWindow?.UpdateTarget(window);
        TryApplyAppLayer();
    }

    private bool IsAppLayerChangeBlocked() => _appLayerReadPending || _searchLayerActive || _isSlotLayoutEditMode ||
        _shortcutService.IsPrefixArmed ||
        _slotLayoutDragSourceLayer >= 0 || DateTime.UtcNow < _appLayerDragUntilUtc ||
        ContextMenu?.IsOpen == true ||
        OwnedWindows.Cast<Window>().Any(window => window.IsVisible &&
            window != _layerNameOverlayWindow && window != _trayMenuHost);

    private void OnAppLayerResumeTick(object? sender, EventArgs e) => TryApplyAppLayer();

    private void TryApplyAppLayer()
    {
        _appLayerResumeTimer?.Stop();
        if (_config.AppLayerSwitchingEnabled && !_appLayerSelection.IsPinned &&
            _appLayerSelection.HasPendingEvaluation && IsAppLayerChangeBlocked())
        {
            _appLayerResumeTimer?.Start();
        }
        else if (_appLayerSelection.TakePendingLayer(_config, blocked: IsAppLayerChangeBlocked()) is int layer &&
            layer != _currentLayer)
        {
            SetLayer(layer, automatic: true);
        }
        UpdateAppLayerUi();
    }

    private void OnAppLayerDrag(object sender, DragEventArgs e) =>
        _appLayerDragUntilUtc = DateTime.UtcNow.AddMilliseconds(500);

    private void OnAppLayerDrop(object sender, DragEventArgs e)
    {
        _appLayerDragUntilUtc = DateTime.UtcNow.AddMilliseconds(150);
    }

    private void UpdateAppLayerUi()
    {
        if (AppLayerSettingsMenuItem == null) return;
        bool english = _currentLanguage == AppLanguage.English;
        AppLayerSettingsMenuItem.Header = english ? "App-based tab switching..." : "アプリ別タブ切替...";
        PinAppLayerMenuItem.Header = english ? "Temporarily pin this tab" : "このタブを一時固定";
        PinAppLayerMenuItem.IsChecked = _appLayerSelection.IsPinned;
        PinAppLayerMenuItem.IsEnabled = _config.AppLayerSwitchingEnabled;
        AppLayerStatusText.Text = !_config.AppLayerSwitchingEnabled ? string.Empty :
            _appLayerSelection.IsPinned ? (english ? "Pinned" : "固定") : (english ? "Auto" : "自動");
        var target = _appLayerSelection.LastExternalWindow;
        var match = AppLayerRuleMatcher.FindMatch(_config, target);
        AppLayerStatusText.ToolTip = english ?
            $"Target: {target?.ExecutableName ?? "unknown"}\nRule: {(match.HasValue ? (match.Value + 1).ToString() : "none")} / Display: tab {_currentLayer + 1}" :
            $"対象：{target?.ExecutableName ?? "未取得"}\nルール：{(match.HasValue ? (match.Value + 1).ToString() : "なし")} ／ 表示：タブ {_currentLayer + 1}";
    }

    private void OnPinAppLayer(object sender, RoutedEventArgs e)
    {
        _appLayerSelection.SetPinned(PinAppLayerMenuItem.IsChecked);
        TryApplyAppLayer();
    }

    private async void OnAppLayerSettings(object sender, RoutedEventArgs e)
    {
        if (_appLayerSettingsWindow != null)
        {
            _appLayerSettingsWindow.Activate();
            return;
        }
        var config = _config;
        var dialog = new AppLayerSettingsWindow(config, _appLayerSelection.LastExternalWindow) { Owner = this };
        _appLayerSettingsWindow = dialog;
        bool confirmed;
        try { confirmed = await dialog.ShowForResultAsync(); }
        finally { _appLayerSettingsWindow = null; }
        if (!confirmed) return;
        if (!ReferenceEquals(config, _config))
        {
            System.Windows.MessageBox.Show(_currentLanguage == AppLanguage.English ?
                "The configuration changed. Reopen these settings." : "設定が変更されました。画面を開き直してください。", Title);
            return;
        }
        var oldEnabled = config.AppLayerSwitchingEnabled;
        var oldFallback = config.AppLayerFallbackLayer;
        var oldRules = config.AppLayerRules;
        config.AppLayerSwitchingEnabled = dialog.SwitchingEnabled;
        config.AppLayerFallbackLayer = dialog.FallbackLayer;
        config.AppLayerRules = dialog.Rules;
        try { _configService.Save(config); }
        catch (Exception ex)
        {
            config.AppLayerSwitchingEnabled = oldEnabled;
            config.AppLayerFallbackLayer = oldFallback;
            config.AppLayerRules = oldRules;
            _logger.Warn($"App layer settings save failed ({ex.GetType().Name}).");
            System.Windows.MessageBox.Show(_currentLanguage == AppLanguage.English ?
                "Could not save the settings. The previous settings were retained." :
                "設定を保存できませんでした。元の設定を維持します。", Title);
            return;
        }
        if (!config.AppLayerSwitchingEnabled) _appLayerSelection.SetPinned(false);
        _appLayerSelection.RequestEvaluation();
        TryApplyAppLayer();
    }

    private void ResetAppLayerStateForConfig()
    {
        var last = _appLayerSelection.LastExternalWindow;
        _appLayerSelection = new AppLayerSelection(_currentLayer);
        _appLayerSelection.Observe(last, isOwnForeground: true);
        _appLayerSelection.RequestEvaluation();
        _appLayerSettingsWindow?.Close();
        _appLayerResumeTimer?.Start();
    }

    private void DisposeAppLayerState()
    {
        _appLayerResumeTimer?.Stop();
        if (_appLayerResumeTimer != null) _appLayerResumeTimer.Tick -= OnAppLayerResumeTick;
        if (_activeWindowMonitor != null)
        {
            _activeWindowMonitor.Changed -= OnActiveWindowChanged;
            _activeWindowMonitor.ExternalTargetChanged -= OnAppLayerExternalTargetChanged;
            _activeWindowMonitor.Failed -= OnActiveWindowReadFailed;
            _activeWindowMonitor.Dispose();
            _activeWindowMonitor = null;
        }
        _appLayerSettingsWindow?.Close();
    }
}
