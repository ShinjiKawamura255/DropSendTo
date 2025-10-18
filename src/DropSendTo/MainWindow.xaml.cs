using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly LauncherService _launcher;
    private readonly LoggerService _logger = LoggerService.Instance;
    private readonly WindowPlacementService _placement = new();
    private readonly System.Windows.Threading.DispatcherTimer _layerHoverTimer;
    private readonly KeyboardMacroService _macroService = new();
    private int _hoverTargetLayer = -1;
    private AppConfig _config;
    private int _currentLayer = 0; // 0..3
    private int? _runningSlotIndex;
    private bool _runningSlotCancellationRequested;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        _configService = new ConfigService();
        _launcher = new LauncherService();
        _config = _configService.LoadOrCreate();
        Topmost = _config.AlwaysOnTop;
        _currentLayer = Math.Clamp(_config.CurrentLayer, 0, 3);

        // Restore window position with clamping
        var bounds = new ScreenBounds(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (_config.WindowLeft.HasValue && _config.WindowTop.HasValue)
        {
            var (l, t) = _placement.Clamp(_config.WindowLeft.Value, _config.WindowTop.Value, bounds, this.Width, this.Height);
            Left = l; Top = t;
        }
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
        RefreshUi();
        this.Closing += (_, _) =>
        {
            var (l, t) = _placement.Clamp(this.Left, this.Top, bounds, this.Width, this.Height);
            _config.WindowLeft = l;
            _config.WindowTop = t;
            _config.CurrentLayer = _currentLayer;
            _config.AlwaysOnTop = this.Topmost;
            _configService.Save(_config);
        };

        _layerHoverTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = System.TimeSpan.FromMilliseconds(800)
        };
        _layerHoverTimer.Tick += (_, _) =>
        {
            if (_hoverTargetLayer >= 0)
            {
                SetLayer(_hoverTargetLayer);
            }
            _layerHoverTimer.Stop();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            _macroService.Initialize(helper);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to initialize keyboard macro service: {ex}");
            MessageBox.Show("マクロサービスの初期化に失敗しました。ログを確認してください。", "Macro Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        _config.CurrentLayer = _currentLayer;
        _config.AlwaysOnTop = this.Topmost;
        _configService.Save(_config);
        Close();
    }

    private void OnLayer1(object sender, RoutedEventArgs e) => SetLayer(0);
    private void OnLayer2(object sender, RoutedEventArgs e) => SetLayer(1);
    private void OnLayer3(object sender, RoutedEventArgs e) => SetLayer(2);
    private void OnLayer4(object sender, RoutedEventArgs e) => SetLayer(3);

    private void SetLayer(int index)
    {
        _currentLayer = index;
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
        RefreshUi();
    }

    private void OnSlotContextMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var cm = new ContextMenu();
        var miEdit = new MenuItem { Header = "Edit..." };
        miEdit.Click += (_, _) => EditSlot(fe);
        var miClear = new MenuItem { Header = "Clear..." };
        miClear.Click += (_, _) => ClearSlot(fe);
        cm.Items.Add(miEdit);
        cm.Items.Add(new Separator());
        cm.Items.Add(miClear);
        cm.Items.Add(new Separator());
        var idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        var toggle = new MenuItem { Header = slot.ClickEnabled ? "Disable Click Launch" : "Enable Click Launch" };
        toggle.Click += (_, _) => { slot.ClickEnabled = !slot.ClickEnabled; _configService.Save(_config); };
        cm.Items.Add(toggle);
        fe.ContextMenu = cm;
        cm.IsOpen = true;
    }

    private int GetSlotIndex(FrameworkElement fe)
    {
        int row = Grid.GetRow(fe);
        int col = Grid.GetColumn(fe);
        return row * 2 + col; // 0..3
    }

    private enum SlotMacroState
    {
        Idle,
        Running,
        Cancelling
    }

    private SlotMacroState GetSlotMacroState(int index)
    {
        if (_runningSlotIndex.HasValue && _runningSlotIndex.Value == index)
        {
            return _runningSlotCancellationRequested ? SlotMacroState.Cancelling : SlotMacroState.Running;
        }
        return SlotMacroState.Idle;
    }

    private (Border? border, TextBlock? status) GetSlotVisuals(int index) =>
        index switch
        {
            0 => (Slot1Border, Slot1Status),
            1 => (Slot2Border, Slot2Status),
            2 => (Slot3Border, Slot3Status),
            3 => (Slot4Border, Slot4Status),
            _ => (null, null)
        };

    private void RenderSlotMacroState(int index, SlotMacroState state)
    {
        var (border, status) = GetSlotVisuals(index);
        if (border == null || status == null) return;

        switch (state)
        {
            case SlotMacroState.Running:
                border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x2E, 0x1A));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5A, 0xD6, 0x6B));
                status.Text = "マクロ実行中...";
                status.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0xFF, 0xB0));
                status.Visibility = Visibility.Visible;
                break;
            case SlotMacroState.Cancelling:
                border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x28, 0x1A));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xC6, 0x4D));
                status.Text = "キャンセル中...";
                status.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x66));
                status.Visibility = Visibility.Visible;
                break;
            default:
                border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 17, 17));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51));
                status.Text = "マクロ実行中...";
                status.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0xFF, 0xB0));
                status.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void UpdateAllSlotMacroStates()
    {
        for (int i = 0; i < 4; i++)
        {
            RenderSlotMacroState(i, GetSlotMacroState(i));
        }
    }

    private void BeginSlotMacro(int index)
    {
        _runningSlotIndex = index;
        _runningSlotCancellationRequested = false;
        RenderSlotMacroState(index, SlotMacroState.Running);
    }

    private void MarkSlotMacroCanceling()
    {
        if (_runningSlotIndex.HasValue)
        {
            _runningSlotCancellationRequested = true;
            RenderSlotMacroState(_runningSlotIndex.Value, SlotMacroState.Cancelling);
        }
    }

    private void ClearSlotMacroState(int index)
    {
        if (_runningSlotIndex.HasValue && _runningSlotIndex.Value == index)
        {
            _runningSlotIndex = null;
            _runningSlotCancellationRequested = false;
        }
        RenderSlotMacroState(index, SlotMacroState.Idle);
    }

    private void EditSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        var dlg = new RegisterDialog(slot);
        if (dlg.ShowDialog() == true)
        {
            slot.Title = dlg.AppTitle;
            slot.Command = dlg.CommandPath;
            slot.ArgumentsTemplate = dlg.ArgumentsTemplate;
            slot.KeyboardMacroScript = dlg.MacroScript;
            _configService.Save(_config);
            RefreshUi();
        }
    }

    private void ClearSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        bool isEmpty = string.IsNullOrWhiteSpace(slot.Title) &&
                       string.IsNullOrWhiteSpace(slot.Command) &&
                       string.IsNullOrWhiteSpace(slot.KeyboardMacroScript) &&
                       string.Equals(slot.ArgumentsTemplate ?? string.Empty, "{args}", StringComparison.Ordinal) &&
                       slot.ClickEnabled;
        if (!isEmpty)
        {
            var result = MessageBox.Show(
                "このスロットの設定を初期化します。よろしいですか？",
                "Clear Slot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }
        _config.Layers[_currentLayer].Slots[idx] = new SlotModel();
        _configService.Save(_config);
        RefreshUi();
    }

    private void OnSlotDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (sender is not FrameworkElement fe) return;
            int idx = GetSlotIndex(fe);
            var slot = _config.Layers[_currentLayer].Slots[idx];
            if (string.IsNullOrWhiteSpace(slot.Command))
            {
                MessageBox.Show("No app registered for this slot.", "DropSendTo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var result = _launcher.Launch(slot, paths);
            if (!result.Success)
            {
                MessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Drop Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        if (this.ContextMenu != null)
        {
            AlwaysOnTopMenuItem.IsChecked = this.Topmost;
            this.ContextMenu.PlacementTarget = (UIElement)sender;
            this.ContextMenu.IsOpen = true;
        }
    }

    private void OnOpenConfig(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _configService.GetConfigPath();
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open Config", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = _logger.LogDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var psi = new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open Logs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnToggleAlwaysOnTop(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        Topmost = item.IsChecked;
        _config.AlwaysOnTop = Topmost;
        _configService.Save(_config);
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        AlwaysOnTopMenuItem.IsChecked = this.Topmost;
    }

    private void OnMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta > 0) SetLayer((_currentLayer + 3) % 4); else SetLayer((_currentLayer + 1) % 4);
    }

    private void OnDragMove(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* ignore */ }
        }
    }

    private void OnLayerDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (sender == LayerBtn1) _hoverTargetLayer = 0;
            else if (sender == LayerBtn2) _hoverTargetLayer = 1;
            else if (sender == LayerBtn3) _hoverTargetLayer = 2;
            else if (sender == LayerBtn4) _hoverTargetLayer = 3;
            _layerHoverTimer.Stop();
            _layerHoverTimer.Start();
            e.Handled = true;
        }
    }

    private void OnLayerDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (sender == LayerBtn1) _hoverTargetLayer = 0;
            else if (sender == LayerBtn2) _hoverTargetLayer = 1;
            else if (sender == LayerBtn3) _hoverTargetLayer = 2;
            else if (sender == LayerBtn4) _hoverTargetLayer = 3;
            if (!_layerHoverTimer.IsEnabled) _layerHoverTimer.Start();
            e.Effects = DragDropEffects.Link;
            e.Handled = true;
        }
    }

    private void OnLayerDragLeave(object sender, DragEventArgs e)
    {
        _hoverTargetLayer = -1;
        _layerHoverTimer.Stop();
    }

    private void OnSlotMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border b) return;
        int idx = GetSlotIndex(b);
        if (GetSlotMacroState(idx) != SlotMacroState.Idle) return;
        b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32));
        b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
    }
    private void OnSlotMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border b) return;
        int idx = GetSlotIndex(b);
        if (GetSlotMacroState(idx) != SlotMacroState.Idle) return;
        RenderSlotMacroState(idx, SlotMacroState.Idle);
    }
    private void OnSlotMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border b) return;
        int idx = GetSlotIndex(b);
        if (GetSlotMacroState(idx) != SlotMacroState.Idle) return;
        b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 48, 48));
        b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
    }

    private void OnSlotDragEnter(object sender, DragEventArgs e)
    {
        if (sender is not Border b) return;
        int idx = GetSlotIndex(b);
        if (GetSlotMacroState(idx) != SlotMacroState.Idle) return;
        b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 48, 48));
        b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
    }
    private void OnSlotDragLeave(object sender, DragEventArgs e)
    {
        OnSlotMouseLeave(sender, null!);
    }

    private async void OnSlotClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        int idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        if (!slot.ClickEnabled) return;
        var script = slot.KeyboardMacroScript ?? string.Empty;
        var hasMacro = !string.IsNullOrWhiteSpace(script);
        var hasCommand = !string.IsNullOrWhiteSpace(slot.Command);

        if (!hasMacro && !hasCommand) return;

        if (_macroService.IsMacroRunning)
        {
            if (_runningSlotIndex.HasValue && _runningSlotIndex.Value == idx && hasMacro)
            {
                if (_macroService.CancelCurrentMacro())
                {
                    MarkSlotMacroCanceling();
                }
            }
            else
            {
                MessageBox.Show("別のスロットのマクロが実行中です。完了または停止してから再度実行してください。", "Macro Running", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        if (hasMacro)
        {
            BeginSlotMacro(idx);
            try
            {
                var macroResult = await _macroService.RunMacroAsync(script);
                if (!macroResult.Success)
                {
                    if (!macroResult.IsCanceled)
                    {
                        MessageBox.Show(macroResult.Message, "Macro Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Macro execution failed: {ex}");
                MessageBox.Show("マクロの実行に失敗しました。ログを確認してください。", "Macro Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                ClearSlotMacroState(idx);
            }
        }

        if (hasCommand)
        {
            var result = _launcher.Launch(slot, Array.Empty<string>());
            if (!result.Success)
                MessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshUi()
    {
        var layer = _config.Layers[_currentLayer];
        int baseNo = _currentLayer * 4;
        Slot1Text.Text = string.IsNullOrWhiteSpace(layer.Slots[0].Title) ? $"Slot {baseNo + 1}" : layer.Slots[0].Title;
        Slot2Text.Text = string.IsNullOrWhiteSpace(layer.Slots[1].Title) ? $"Slot {baseNo + 2}" : layer.Slots[1].Title;
        Slot3Text.Text = string.IsNullOrWhiteSpace(layer.Slots[2].Title) ? $"Slot {baseNo + 3}" : layer.Slots[2].Title;
        Slot4Text.Text = string.IsNullOrWhiteSpace(layer.Slots[3].Title) ? $"Slot {baseNo + 4}" : layer.Slots[3].Title;
        // Layer button highlight with stronger contrast
        UpdateLayerButtonVisuals();
        UpdateAllSlotMacroStates();
    }

    private void UpdateLayerButtonVisuals()
    {
        void SetState(Button b, bool active)
        {
            if (active)
            {
                // Black-based emphasis: brighter gray background and strong border
                b.Opacity = 1.0;
                b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22));
                b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                b.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                b.FontWeight = System.Windows.FontWeights.SemiBold;
            }
            else
            {
                b.Opacity = 0.9;
                b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11));
                b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
                b.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                b.FontWeight = System.Windows.FontWeights.Normal;
            }
        }
        SetState(LayerBtn1, _currentLayer == 0);
        SetState(LayerBtn2, _currentLayer == 1);
        SetState(LayerBtn3, _currentLayer == 2);
        SetState(LayerBtn4, _currentLayer == 3);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _macroService.Dispose();
    }
}
