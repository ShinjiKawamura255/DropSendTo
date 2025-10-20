using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
    private readonly ShortcutService _shortcutService = new();
    private readonly List<ShortcutBinding> _shortcutBindings = new();
    private readonly List<SlotVisual> _slotVisuals = new();
    private static readonly SolidColorBrush PrefixArmedBackgroundBrush;
    private static readonly SolidColorBrush PrefixArmedBorderBrush;
    private static readonly SolidColorBrush PrefixArmedForegroundBrush;
    private static readonly (int rows, int columns)[] SlotLayoutOptions =
    {
        (2, 2), (2, 3), (2, 4),
        (3, 2), (3, 3), (3, 4),
        (4, 2), (4, 3), (4, 4)
    };
    static MainWindow()
    {
        PrefixArmedBackgroundBrush = CreateFrozenBrush(Color.FromRgb(0x1E, 0x82, 0x4C));
        PrefixArmedBorderBrush = CreateFrozenBrush(Color.FromRgb(0x7C, 0xFF, 0xB0));
        PrefixArmedForegroundBrush = CreateFrozenBrush(Colors.White);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private const double BaseWindowWidth = 234;
    private const double BaseWindowHeight = 148;
    private const double ColumnWidthStep = 95;
    private const double RowHeightStep = 60;
    private int _hoverTargetLayer = -1;
    private AppConfig _config;
    private int _currentLayer = 0; // 0..3
    private int? _runningSlotIndex;
    private int? _runningSlotLayerIndex;
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

        ApplySlotLayout();
        RestoreWindowPosition();
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
        RefreshUi();
        this.Closing += (_, _) =>
        {
            ClampWindowWithinBounds();
            _config.WindowLeft = this.Left;
            _config.WindowTop = this.Top;
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

    private void ApplySlotLayout()
    {
        int rows = Math.Clamp(_config.SlotRows, 2, 4);
        int columns = Math.Clamp(_config.SlotColumns, 2, 4);
        _config.SlotRows = rows;
        _config.SlotColumns = columns;

        int requiredSlots = rows * columns;
        foreach (var layer in _config.Layers)
        {
            EnsureLayerSlotCapacity(layer, requiredSlots);
        }

        SlotsPanel.Rows = rows;
        SlotsPanel.Columns = columns;
        SlotsPanel.Children.Clear();
        _slotVisuals.Clear();

        for (int index = 0; index < requiredSlots; index++)
        {
            var visual = CreateSlotVisual(index);
            _slotVisuals.Add(visual);
            SlotsPanel.Children.Add(visual.Border);
        }

        UpdateWindowSize(rows, columns);
        ClampWindowWithinBounds();
    }

    private void RestoreWindowPosition()
    {
        if (_config.WindowLeft.HasValue && _config.WindowTop.HasValue)
        {
            var bounds = GetVirtualScreenBounds();
            var (left, top) = _placement.Clamp(_config.WindowLeft.Value, _config.WindowTop.Value, bounds, this.Width, this.Height);
            Left = left;
            Top = top;
        }
        else
        {
            ClampWindowWithinBounds();
        }
    }

    private void ClampWindowWithinBounds()
    {
        var bounds = GetVirtualScreenBounds();
        var (left, top) = _placement.Clamp(this.Left, this.Top, bounds, this.Width, this.Height);
        Left = left;
        Top = top;
    }

    private static ScreenBounds GetVirtualScreenBounds() =>
        new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    private void UpdateWindowSize(int rows, int columns)
    {
        Width = BaseWindowWidth + (columns - 2) * ColumnWidthStep;
        Height = BaseWindowHeight + (rows - 2) * RowHeightStep;
    }

    private static void EnsureLayerSlotCapacity(Layer layer, int requiredSlots)
    {
        layer.Slots ??= new List<SlotModel>();
        while (layer.Slots.Count < requiredSlots)
        {
            layer.Slots.Add(new SlotModel());
        }
    }

    private SlotVisual CreateSlotVisual(int index)
    {
        var border = new Border
        {
            Margin = new Thickness(2),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11)),
            Height = 48,
            AllowDrop = true,
            Tag = index
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = index
        };

        var title = new TextBlock
        {
            Text = $"Slot {index + 1}",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = index
        };

        var status = new TextBlock
        {
            Text = "マクロ実行中...",
            FontSize = 11,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0xFF, 0xB0)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Tag = index
        };

        stack.Children.Add(title);
        stack.Children.Add(status);
        border.Child = stack;

        border.Drop += OnSlotDrop;
        border.MouseRightButtonUp += OnSlotContextMenu;
        border.MouseEnter += OnSlotMouseEnter;
        border.MouseLeave += OnSlotMouseLeave;
        border.DragEnter += OnSlotDragEnter;
        border.DragLeave += OnSlotDragLeave;
        border.MouseLeftButtonDown += OnSlotMouseDown;
        border.MouseLeftButtonUp += OnSlotClick;

        return new SlotVisual(border, title, status);
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

        try
        {
            _shortcutService.ShortcutTriggered += OnShortcutTriggered;
            _shortcutService.PrefixPassthroughRequested += OnPrefixPassthroughRequested;
            _shortcutService.PrefixStateChanged += OnPrefixStateChanged;
            _shortcutService.Initialize(_config.ShortcutPrefix);
            if (!_shortcutService.IsUsingFallbackPrefix)
            {
                var normalized = _shortcutService.CurrentPrefixText;
                if (!string.Equals(_config.ShortcutPrefix, normalized, StringComparison.Ordinal))
                {
                    _config.ShortcutPrefix = normalized;
                }
            }
            else
            {
                _logger.Warn("Configured shortcut prefix could not be parsed. Falling back to Ctrl+Q.");
                MessageBox.Show("設定ファイルの Prefix を解釈できなかったため、Ctrl+Q に戻しました。設定値を確認してください。", "Shortcut Prefix", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateShortcutRegistrations();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to initialize shortcut service: {ex}");
            MessageBox.Show("ショートカットサービスの初期化に失敗しました。ログを確認してください。", "Shortcut Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (fe.Tag is int index)
        {
            return index;
        }

        if (fe.Parent is FrameworkElement parent)
        {
            return GetSlotIndex(parent);
        }

        throw new InvalidOperationException("Slot index could not be resolved.");
    }

    private enum SlotMacroState
    {
        Idle,
        Running,
        Cancelling
    }

    private enum SlotTriggerSource
    {
        Mouse,
        Shortcut
    }

    private sealed record ShortcutBinding(string NormalizedKey, int LayerIndex, int SlotIndex);
    private sealed record SlotVisual(Border Border, TextBlock Title, TextBlock Status);

    private SlotMacroState GetSlotMacroState(int index)
    {
        if (_runningSlotLayerIndex.HasValue &&
            _runningSlotLayerIndex.Value == _currentLayer &&
            _runningSlotIndex.HasValue &&
            _runningSlotIndex.Value == index)
        {
            return _runningSlotCancellationRequested ? SlotMacroState.Cancelling : SlotMacroState.Running;
        }
        return SlotMacroState.Idle;
    }

    private void RenderSlotMacroState(int index, SlotMacroState state)
    {
        if (index < 0 || index >= _slotVisuals.Count) return;

        var visual = _slotVisuals[index];
        var border = visual.Border;
        var status = visual.Status;

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
                border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                status.Text = "マクロ実行中...";
                status.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0xFF, 0xB0));
                status.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void UpdateAllSlotMacroStates()
    {
        for (int i = 0; i < _slotVisuals.Count; i++)
        {
            RenderSlotMacroState(i, GetSlotMacroState(i));
        }
    }

    private void BeginSlotMacro(int layerIndex, int index)
    {
        _runningSlotLayerIndex = layerIndex;
        _runningSlotIndex = index;
        _runningSlotCancellationRequested = false;
        if (layerIndex == _currentLayer)
        {
            RenderSlotMacroState(index, SlotMacroState.Running);
        }
    }

    private void MarkSlotMacroCanceling(int layerIndex, int index)
    {
        if (_runningSlotLayerIndex.HasValue &&
            _runningSlotLayerIndex.Value == layerIndex &&
            _runningSlotIndex.HasValue &&
            _runningSlotIndex.Value == index)
        {
            _runningSlotCancellationRequested = true;
            if (layerIndex == _currentLayer)
            {
                RenderSlotMacroState(index, SlotMacroState.Cancelling);
            }
        }
    }

    private void ClearSlotMacroState(int layerIndex, int index)
    {
        if (_runningSlotLayerIndex.HasValue &&
            _runningSlotLayerIndex.Value == layerIndex &&
            _runningSlotIndex.HasValue &&
            _runningSlotIndex.Value == index)
        {
            _runningSlotLayerIndex = null;
            _runningSlotIndex = null;
            _runningSlotCancellationRequested = false;
        }
        if (layerIndex == _currentLayer)
        {
            RenderSlotMacroState(index, SlotMacroState.Idle);
        }
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
            slot.ShortcutKey = dlg.ShortcutChord;
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
                       string.IsNullOrWhiteSpace(slot.ShortcutKey) &&
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
            PopulateLayoutMenu(LayoutMenuItem);
            this.ContextMenu.PlacementTarget = (UIElement)sender;
            this.ContextMenu.IsOpen = true;
        }
    }

    private void OnLayoutMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        PopulateLayoutMenu(menuItem);
    }

    private void OnLayoutOptionSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not ValueTuple<int, int> layout) return;
        if (_config.SlotRows == layout.Item1 && _config.SlotColumns == layout.Item2) return;

        _config.SlotRows = layout.Item1;
        _config.SlotColumns = layout.Item2;
        ApplySlotLayout();
        RefreshUi();
        ClampWindowWithinBounds();
        _configService.Save(_config);
    }

    private void PopulateLayoutMenu(MenuItem menuItem)
    {
        if (menuItem == null) return;
        menuItem.Items.Clear();
        foreach (var option in SlotLayoutOptions)
        {
            var item = new MenuItem
            {
                Header = $"{option.rows}x{option.columns}",
                IsCheckable = true,
                IsChecked = option.rows == _config.SlotRows && option.columns == _config.SlotColumns,
                Tag = option
            };
            item.Click += OnLayoutOptionSelected;
            menuItem.Items.Add(item);
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

    private void OnChangePrefix(object sender, RoutedEventArgs e)
    {
        var dlg = new PrefixDialog(_config.ShortcutPrefix) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var newPrefix = dlg.NormalizedPrefix;
            bool prefixChanged = !string.Equals(_config.ShortcutPrefix, newPrefix, StringComparison.Ordinal);

            _shortcutService.UpdatePrefix(newPrefix);
            if (prefixChanged)
            {
                _config.ShortcutPrefix = newPrefix;
                _configService.Save(_config);
            }

            UpdateShortcutRegistrations();
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
        PopulateLayoutMenu(LayoutMenuItem);
    }

    private void OnMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta > 0) SetLayer((_currentLayer + 3) % 4); else SetLayer((_currentLayer + 1) % 4);
    }

    private void OnDragMoveArea(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsWithinInteractiveElement(e.OriginalSource as DependencyObject)) return;
        OnDragMove(sender, e);
    }

    private static bool IsWithinInteractiveElement(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button or MenuItem)
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
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

    private async Task TriggerSlotAsync(int layerIndex, int slotIndex, SlotTriggerSource source)
    {
        if (slotIndex < 0 || slotIndex >= _slotVisuals.Count) return;
        var layer = _config.Layers[layerIndex];
        var slot = layer.Slots[slotIndex];
        if (source == SlotTriggerSource.Mouse && !slot.ClickEnabled) return;

        var script = slot.KeyboardMacroScript ?? string.Empty;
        var hasMacro = !string.IsNullOrWhiteSpace(script);
        var hasCommand = !string.IsNullOrWhiteSpace(slot.Command);

        if (!hasMacro && !hasCommand) return;

        if (_macroService.IsMacroRunning)
        {
            if (_runningSlotLayerIndex.HasValue &&
                _runningSlotLayerIndex.Value == layerIndex &&
                _runningSlotIndex.HasValue &&
                _runningSlotIndex.Value == slotIndex &&
                hasMacro)
            {
                if (_macroService.CancelCurrentMacro())
                {
                    MarkSlotMacroCanceling(layerIndex, slotIndex);
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
            BeginSlotMacro(layerIndex, slotIndex);
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
                ClearSlotMacroState(layerIndex, slotIndex);
            }
        }

        if (hasCommand)
        {
            var result = _launcher.Launch(slot, Array.Empty<string>());
            if (!result.Success)
            {
                MessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnSlotClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        int idx = GetSlotIndex(fe);
        await TriggerSlotAsync(_currentLayer, idx, SlotTriggerSource.Mouse);
    }

    private void OnShortcutTriggered(object? sender, ShortcutTriggeredEventArgs e)
    {
        var normalized = e.RegisteredChord.NormalizedString;
        foreach (var binding in _shortcutBindings)
        {
            if (string.Equals(binding.NormalizedKey, normalized, StringComparison.Ordinal))
            {
                if (binding.LayerIndex != _currentLayer)
                {
                    SetLayer(binding.LayerIndex);
                }
                _ = TriggerSlotAsync(binding.LayerIndex, binding.SlotIndex, SlotTriggerSource.Shortcut);
                break;
            }
        }
    }

    private void OnPrefixPassthroughRequested(object? sender, PrefixPassthroughEventArgs e)
    {
        _ = SendPrefixPassthroughAsync(e.ShortcutText);
    }

    private void OnPrefixStateChanged(object? sender, PrefixStateChangedEventArgs e)
    {
        if (PrefixIndicator == null || PrefixIndicatorText == null)
        {
            return;
        }

        if (e.IsArmed)
        {
            PrefixIndicator.Visibility = Visibility.Visible;
            PrefixIndicator.Background = PrefixArmedBackgroundBrush;
            PrefixIndicator.BorderBrush = PrefixArmedBorderBrush;
            PrefixIndicatorText.Foreground = PrefixArmedForegroundBrush;
            PrefixIndicatorText.Text = "PREFIX";
        }
        else
        {
            PrefixIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private async Task SendPrefixPassthroughAsync(string prefixText)
    {
        if (_macroService.IsMacroRunning)
        {
            return;
        }

        try
        {
            var result = await _macroService.RunMacroAsync("KEY " + prefixText);
            if (!result.Success && !result.IsCanceled)
            {
                _logger.Warn($"Prefix passthrough failed: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Prefix passthrough execution failed: {ex}");
        }
    }

    private void RefreshUi()
    {
        var layer = _config.Layers[_currentLayer];
        int baseNo = _currentLayer * _slotVisuals.Count;
        for (int i = 0; i < _slotVisuals.Count; i++)
        {
            var slot = layer.Slots[i];
            string title = string.IsNullOrWhiteSpace(slot.Title)
                ? $"Slot {baseNo + i + 1}"
                : slot.Title;
            _slotVisuals[i].Title.Text = title;
        }
        // Layer button highlight with stronger contrast
        UpdateLayerButtonVisuals();
        UpdateAllSlotMacroStates();
        UpdateShortcutRegistrations();
    }

    private void UpdateShortcutRegistrations()
    {
        if (_config == null) return;

        _shortcutBindings.Clear();
        var orderedBindings = new List<ShortcutBinding>();

        void AddLayerBindings(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _config.Layers.Count) return;
            var layer = _config.Layers[layerIndex];
            int maxSlots = Math.Min(_slotVisuals.Count, layer.Slots.Count);
            for (int slotIndex = 0; slotIndex < maxSlots; slotIndex++)
            {
                var shortcutText = layer.Slots[slotIndex].ShortcutKey;
                if (string.IsNullOrWhiteSpace(shortcutText)) continue;
                var trimmed = shortcutText.Trim();
                if (!KeyChordParser.TryParse(trimmed, out var chord, out var error))
                {
                    _logger.Warn($"ショートカット設定の解析に失敗しました (Layer={layerIndex + 1}, Slot={slotIndex + 1}): {error}");
                    continue;
                }
                orderedBindings.Add(new ShortcutBinding(chord.NormalizedString, layerIndex, slotIndex));
            }
        }

        AddLayerBindings(_currentLayer);
        for (int layerIndex = 0; layerIndex < _config.Layers.Count; layerIndex++)
        {
            if (layerIndex == _currentLayer) continue;
            AddLayerBindings(layerIndex);
        }

        _shortcutBindings.AddRange(orderedBindings);
        _shortcutService.UpdateAvailableShortcuts(_shortcutBindings.Select(b => b.NormalizedKey));
        if (PrefixIndicatorText != null)
        {
            PrefixIndicatorText.Text = "PREFIX";
        }
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
        _shortcutService.ShortcutTriggered -= OnShortcutTriggered;
        _shortcutService.PrefixPassthroughRequested -= OnPrefixPassthroughRequested;
        _shortcutService.PrefixStateChanged -= OnPrefixStateChanged;
        _shortcutService.Dispose();
        _macroService.Dispose();
    }
}
