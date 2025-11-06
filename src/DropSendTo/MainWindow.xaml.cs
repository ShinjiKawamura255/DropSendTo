using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using DropSendTo.Models;
using DropSendTo.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using DragEventArgs = System.Windows.DragEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

using WpfButton = System.Windows.Controls.Button;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using DrawingIcon = System.Drawing.Icon;

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
    private readonly ConfigTransferService _configTransferService = new();
    private readonly List<ShortcutBinding> _shortcutBindings = new();
    private readonly List<SlotVisual> _slotVisuals = new();
    private Forms.NotifyIcon? _notifyIcon;
    private DrawingIcon? _notifyIconDefault;
    private DrawingIcon? _notifyIconActive;
    private bool _isMinimizedToTray;
    private bool _minimizeOnLoaded;
    private static readonly SolidColorBrush PrefixArmedBackgroundBrush;
    private static readonly SolidColorBrush PrefixArmedBorderBrush;
    private static readonly SolidColorBrush PrefixArmedForegroundBrush;
    private static readonly (int rows, int columns)[] SlotLayoutOptions =
    {
        (2, 2), (2, 3), (2, 4),
        (3, 2), (3, 3), (3, 4),
        (4, 2), (4, 3), (4, 4)
    };

    private static readonly (SlotSize size, string header)[] SlotSizeOptions =
    {
        (SlotSize.Large, "Large"),
        (SlotSize.Small, "Small")
    };

    private readonly record struct SlotSizeMetrics(
        double BaseWidth,
        double BaseHeight,
        double ColumnStep,
        double RowStep,
        double SlotHeight,
        double TitleFontSize,
        double StatusFontSize);

    private static readonly SlotSizeMetrics LargeSlotMetrics = new(
        BaseWidth: 234,
        BaseHeight: 148,
        ColumnStep: 95,
        RowStep: 60,
        SlotHeight: 48,
        TitleFontSize: 12,
        StatusFontSize: 11);

    private static readonly SlotSizeMetrics SmallSlotMetrics = new(
        BaseWidth: 210,
        BaseHeight: 126,
        ColumnStep: 80,
        RowStep: 50,
        SlotHeight: 40,
        TitleFontSize: 11,
        StatusFontSize: 10);
    static MainWindow()
    {
        PrefixArmedBackgroundBrush = CreateFrozenBrush(MediaColor.FromRgb(0x1E, 0x82, 0x4C));
        PrefixArmedBorderBrush = CreateFrozenBrush(MediaColor.FromRgb(0x7C, 0xFF, 0xB0));
        PrefixArmedForegroundBrush = CreateFrozenBrush(System.Windows.Media.Colors.White);
    }

    private static SolidColorBrush CreateFrozenBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private int _hoverTargetLayer = -1;
    private AppConfig _config;
    private int _currentLayer = 0; // 0..3
    private int? _runningSlotIndex;
    private int? _runningSlotLayerIndex;
    private bool _runningSlotCancellationRequested;
    private WindowPlacementMode _windowPlacementMode;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        InitializeNotifyIcon();
        _configService = new ConfigService();
        _launcher = new LauncherService();
        _config = _configService.LoadOrCreate();
        _windowPlacementMode = _config.WindowPlacementMode;
        _minimizeOnLoaded = _config.StartupBehavior == StartupWindowBehavior.RestoreLastState
                            && _config.LastWindowVisibility == WindowVisibilityState.Tray;
        Loaded += OnLoaded;
        Topmost = _config.AlwaysOnTop;
        _currentLayer = Math.Clamp(_config.CurrentLayer, 0, 3);

        ApplySlotLayout();
        RestoreWindowPosition();
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
        RefreshUi();
        UpdateTrayMenuState();
        this.Closing += (_, _) =>
        {
            if (_windowPlacementMode == WindowPlacementMode.Fixed)
            {
                CaptureFixedWindowPosition(clampBeforeStoring: true);
            }
            else
            {
                ClampWindowWithinBounds();
            }
            _config.CurrentLayer = _currentLayer;
            _config.AlwaysOnTop = this.Topmost;
            _config.LastWindowVisibility = _isMinimizedToTray ? WindowVisibilityState.Tray : WindowVisibilityState.Visible;
            _configService.Save(_config);
            ClipboardHistoryService.Instance.Dispose();
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

    private void InitializeNotifyIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "DropSendTo",
            Visible = true
        };

        _notifyIconDefault = LoadTrayIcon("pack://application:,,,/img/icon.ico");
        _notifyIconActive = LoadTrayIcon("pack://application:,,,/img/icon_green.ico");
        UpdateNotifyIconState(false);

        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
    }

    private DrawingIcon? LoadTrayIcon(string resourcePath)
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri(resourcePath));
            if (resource?.Stream == null)
            {
                return null;
            }

            using var stream = resource.Stream;
            using var icon = new DrawingIcon(stream);
            return (DrawingIcon)icon.Clone();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load tray icon \"{resourcePath}\": {ex.Message}");
            return null;
        }
    }

    private void UpdateNotifyIconState(bool macroRunning)
    {
        if (_notifyIcon == null)
        {
            return;
        }

        DrawingIcon icon = macroRunning
            ? (_notifyIconActive ?? _notifyIconDefault ?? System.Drawing.SystemIcons.Application)
            : (_notifyIconDefault ?? System.Drawing.SystemIcons.Application);
        _notifyIcon.Icon = icon;
    }

    private void OnNotifyIconMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                BringWindowToForeground();
            }
            else if (e.Button == Forms.MouseButtons.Right)
            {
                if (ContextMenu != null)
                {
                    ContextMenu.PlacementTarget = this;
                    ContextMenu.Placement = PlacementMode.MousePoint;
                    ContextMenu.IsOpen = true;
                }
            }
        });
    }

    private void MinimizeWindowToTray()
    {
        if (_isMinimizedToTray)
        {
            return;
        }

        CaptureFixedWindowPosition(clampBeforeStoring: true);
        _isMinimizedToTray = true;
        if (_config.LastWindowVisibility != WindowVisibilityState.Tray)
        {
            _config.LastWindowVisibility = WindowVisibilityState.Tray;
            _configService.Save(_config);
        }
        Hide();
        UpdateTrayMenuState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (_minimizeOnLoaded)
        {
            _minimizeOnLoaded = false;
            Dispatcher.BeginInvoke(new Action(MinimizeWindowToTray));
        }
        else if (_config.LastWindowVisibility != WindowVisibilityState.Visible)
        {
            _config.LastWindowVisibility = WindowVisibilityState.Visible;
        }
    }

    private void RestoreWindowFromTray()
    {
        if (_isMinimizedToTray || !IsVisible)
        {
            Show();
            _isMinimizedToTray = false;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (_config.LastWindowVisibility != WindowVisibilityState.Visible)
        {
            _config.LastWindowVisibility = WindowVisibilityState.Visible;
            _configService.Save(_config);
        }

        UpdateTrayMenuState();
    }

    private void UpdateTrayMenuState()
    {
        if (MinimizeToTrayMenuItem != null)
        {
            MinimizeToTrayMenuItem.IsEnabled = !_isMinimizedToTray;
        }
    }

    private void BringWindowToForeground()
    {
        RestoreWindowFromTray();
        Activate();
        Focus();
        bool desiredTopmost = _config?.AlwaysOnTop ?? true;
        Topmost = true;
        Topmost = desiredTopmost;
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
            var rect = GetWindowRect(_config.WindowLeft.Value, _config.WindowTop.Value);
            var bounds = ScreenBoundsResolver.ForRect(this, rect);
            var (left, top) = _placement.Clamp(rect.Left, rect.Top, bounds, rect.Width, rect.Height);
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
        var rect = GetWindowRect(this.Left, this.Top);
        var bounds = ScreenBoundsResolver.ForRect(this, rect);
        var (left, top) = _placement.Clamp(rect.Left, rect.Top, bounds, rect.Width, rect.Height);
        Left = left;
        Top = top;
    }

    private Rect GetWindowRect(double left, double top)
    {
        double width = this.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = this.ActualWidth;
        }
        if (width <= 0)
        {
            width = Math.Max(this.MinWidth, 1);
        }

        double height = this.Height;
        if (double.IsNaN(height) || height <= 0)
        {
            height = this.ActualHeight;
        }
        if (height <= 0)
        {
            height = Math.Max(this.MinHeight, 1);
        }

        return new Rect(left, top, width, height);
    }

    private void UpdateWindowSize(int rows, int columns)
    {
        var metrics = GetSlotSizeMetrics();
        Width = metrics.BaseWidth + (columns - 2) * metrics.ColumnStep;
        Height = metrics.BaseHeight + (rows - 2) * metrics.RowStep;
    }

    private SlotSizeMetrics GetSlotSizeMetrics()
    {
        return _config?.SlotSize == SlotSize.Small ? SmallSlotMetrics : LargeSlotMetrics;
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
        var metrics = GetSlotSizeMetrics();
        var border = new Border
        {
            Margin = new Thickness(2),
            BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x11, 0x11, 0x11)),
            Height = metrics.SlotHeight,
            AllowDrop = true,
            Tag = index
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
            Tag = index
        };

        var title = new TextBlock
        {
            Text = $"Slot {index + 1}",
            FontSize = metrics.TitleFontSize,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Tag = index
        };

        var status = new TextBlock
        {
            Text = "マクロ実行中...",
            FontSize = metrics.StatusFontSize,
            Foreground = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x7C, 0xFF, 0xB0)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
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
            WpfMessageBox.Show("マクロサービスの初期化に失敗しました。ログを確認してください。", "Macro Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        try
        {
            ClipboardHistoryService.Instance.Initialize(this);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to initialize clipboard history listener: {ex.Message}");
        }

        try
        {
            _shortcutService.ShortcutTriggered += OnShortcutTriggered;
            _shortcutService.PrefixPassthroughRequested += OnPrefixPassthroughRequested;
            _shortcutService.PrefixActivationRequested += OnPrefixActivationRequested;
            _shortcutService.PrefixMacroCancelRequested += OnPrefixMacroCancelRequested;
            _shortcutService.PrefixMinimizeRequested += OnPrefixMinimizeRequested;
            _shortcutService.PrefixPositionToggleRequested += OnPrefixPositionToggleRequested;
            _shortcutService.PrefixStateChanged += OnPrefixStateChanged;
            _shortcutService.Initialize(_config.ShortcutPrefix, _config.ShortcutPrefixDisabled);
            _macroService.SetPrefixStateAccessors(
                () => _shortcutService.CurrentPrefixChord,
                () => _shortcutService.IsPrefixArmed,
                () => _shortcutService.ResetPrefixState(clearModifiers: true));
            if (_shortcutService.IsPrefixDisabled)
            {
                // 無効化時は設定値の正規化は行わない。
            }
            else if (!_shortcutService.IsUsingFallbackPrefix)
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
                WpfMessageBox.Show("設定ファイルの Prefix を解釈できなかったため、Ctrl+Q に戻しました。設定値を確認してください。", "Shortcut Prefix", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateShortcutRegistrations();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to initialize shortcut service: {ex}");
            WpfMessageBox.Show("ショートカットサービスの初期化に失敗しました。ログを確認してください。", "Shortcut Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        _config.CurrentLayer = _currentLayer;
        _config.AlwaysOnTop = this.Topmost;
        _config.LastWindowVisibility = _isMinimizedToTray ? WindowVisibilityState.Tray : WindowVisibilityState.Visible;
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
        var idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        var emptyTargets = GetEmptySlotOptions();
        bool sourceHasContent = !IsSlotEmpty(slot);
        bool hasMoveTargets = emptyTargets.Any(opt => opt.LayerIndex != _currentLayer || opt.SlotIndex != idx);
        bool hasCopyTargets = emptyTargets.Count > 0;

        var cm = new ContextMenu();
        var miEdit = new MenuItem { Header = "Edit..." };
        miEdit.Click += (_, _) => EditSlot(fe);
        var miMove = new MenuItem { Header = "Move to..." , IsEnabled = sourceHasContent && hasMoveTargets };
        miMove.Click += (_, _) => MoveSlot(_currentLayer, idx);
        var miCopy = new MenuItem { Header = "Copy to..." , IsEnabled = sourceHasContent && hasCopyTargets };
        miCopy.Click += (_, _) => CopySlot(_currentLayer, idx);
        var miClear = new MenuItem { Header = "Clear..." };
        miClear.Click += (_, _) => ClearSlot(fe);
        cm.Items.Add(miEdit);
        cm.Items.Add(miMove);
        cm.Items.Add(miCopy);
        cm.Items.Add(new Separator());
        cm.Items.Add(miClear);
        cm.Items.Add(new Separator());
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
                border.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x1A, 0x2E, 0x1A));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x5A, 0xD6, 0x6B));
                status.Text = "マクロ実行中...";
                status.Foreground = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x7C, 0xFF, 0xB0));
                status.Visibility = Visibility.Visible;
                break;
            case SlotMacroState.Cancelling:
                border.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x2E, 0x28, 0x1A));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0xFF, 0xC6, 0x4D));
                status.Text = "キャンセル中...";
                status.Foreground = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0xFF, 0xD7, 0x66));
                status.Visibility = Visibility.Visible;
                break;
            default:
                border.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x11, 0x11, 0x11));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x33, 0x33, 0x33));
                status.Text = "マクロ実行中...";
                status.Foreground = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x7C, 0xFF, 0xB0));
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
        UpdateNotifyIconState(true);
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
        if (!_macroService.IsMacroRunning)
        {
            UpdateNotifyIconState(false);
        }
    }

    private void EditSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        var dlg = new RegisterDialog(slot)
        {
            Owner = this
        };
        WindowCascadeService.Arrange(dlg, this);

        dlg.SlotSaved += (_, args) =>
        {
            slot.Title = args.AppTitle;
            slot.Command = args.CommandPath;
            slot.ArgumentsTemplate = args.ArgumentsTemplate;
            slot.KeyboardMacroScript = args.MacroScript;
            slot.ShortcutKey = args.ShortcutChord;
            slot.ExecutionMode = args.ExecutionMode;
            _configService.Save(_config);
            RefreshUi();
        };

        dlg.Show();
    }

    private void ClearSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        if (!IsSlotEmpty(slot))
        {
            var result = WpfMessageBox.Show(
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

    private static bool IsSlotEmpty(SlotModel slot)
    {
        if (slot == null) return true;
        bool baseTemplate = string.Equals(slot.ArgumentsTemplate ?? string.Empty, "{args}", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(slot.Title) &&
               string.IsNullOrWhiteSpace(slot.Command) &&
               string.IsNullOrWhiteSpace(slot.KeyboardMacroScript) &&
               string.IsNullOrWhiteSpace(slot.ShortcutKey) &&
               baseTemplate &&
               slot.ClickEnabled &&
               string.IsNullOrWhiteSpace(slot.IconPath);
    }

    private static SlotModel CloneSlot(SlotModel source)
    {
        return new SlotModel
        {
            Title = source.Title,
            Command = source.Command,
            ArgumentsTemplate = source.ArgumentsTemplate,
            IconPath = source.IconPath,
            ClickEnabled = source.ClickEnabled,
            ShortcutKey = source.ShortcutKey,
            KeyboardMacroScript = source.KeyboardMacroScript,
            ExecutionMode = source.ExecutionMode
        };
    }

    private List<SlotSelectionOption> GetEmptySlotOptions()
    {
        var options = new List<SlotSelectionOption>();
        for (int layerIndex = 0; layerIndex < _config.Layers.Count; layerIndex++)
        {
            var layer = _config.Layers[layerIndex];
            for (int slotIndex = 0; slotIndex < layer.Slots.Count; slotIndex++)
            {
                if (IsSlotEmpty(layer.Slots[slotIndex]))
                {
                    options.Add(new SlotSelectionOption(layerIndex, slotIndex, FormatSlotDisplayName(layerIndex, slotIndex)));
                }
            }
        }
        return options;
    }

    private static string FormatSlotDisplayName(int layerIndex, int slotIndex)
    {
        return string.Format(CultureInfo.InvariantCulture, "Layer {0} - Slot {1}", layerIndex + 1, slotIndex + 1);
    }

    private void MoveSlot(int sourceLayerIndex, int sourceSlotIndex)
    {
        var sourceSlot = _config.Layers[sourceLayerIndex].Slots[sourceSlotIndex];
        if (IsSlotEmpty(sourceSlot))
        {
            WpfMessageBox.Show("空のスロットは移動できません。", "Move Slot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var candidates = GetEmptySlotOptions()
            .Where(opt => opt.LayerIndex != sourceLayerIndex || opt.SlotIndex != sourceSlotIndex)
            .ToList();
        if (candidates.Count == 0)
        {
            WpfMessageBox.Show("移動先の空きスロットがありません。", "Move Slot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SlotSelectionDialog(candidates, "スロットの移動先を選択") { Owner = this };
        WindowCascadeService.Arrange(dialog, this);
        if (dialog.ShowDialog() != true || dialog.SelectedOption == null)
        {
            return;
        }

        var selection = dialog.SelectedOption;
        var targetSlot = _config.Layers[selection.LayerIndex].Slots[selection.SlotIndex];
        if (!IsSlotEmpty(targetSlot))
        {
            WpfMessageBox.Show("選択されたスロットは既に使用されています。", "Move Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.Layers[selection.LayerIndex].Slots[selection.SlotIndex] = CloneSlot(sourceSlot);
        _config.Layers[sourceLayerIndex].Slots[sourceSlotIndex] = new SlotModel();
        _configService.Save(_config);
        RefreshUi();
    }

    private void CopySlot(int sourceLayerIndex, int sourceSlotIndex)
    {
        var sourceSlot = _config.Layers[sourceLayerIndex].Slots[sourceSlotIndex];
        if (IsSlotEmpty(sourceSlot))
        {
            WpfMessageBox.Show("空のスロットはコピーできません。", "Copy Slot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var candidates = GetEmptySlotOptions();
        if (candidates.Count == 0)
        {
            WpfMessageBox.Show("コピー先の空きスロットがありません。", "Copy Slot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SlotSelectionDialog(candidates, "スロットのコピー先を選択") { Owner = this };
        WindowCascadeService.Arrange(dialog, this);
        if (dialog.ShowDialog() != true || dialog.SelectedOption == null)
        {
            return;
        }

        var selection = dialog.SelectedOption;
        var targetSlot = _config.Layers[selection.LayerIndex].Slots[selection.SlotIndex];
        if (!IsSlotEmpty(targetSlot))
        {
            WpfMessageBox.Show("選択されたスロットは既に使用されています。", "Copy Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.Layers[selection.LayerIndex].Slots[selection.SlotIndex] = CloneSlot(sourceSlot);
        _configService.Save(_config);
        RefreshUi();
    }

    private void OnSlotDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
            if (sender is not FrameworkElement fe) return;
            int idx = GetSlotIndex(fe);
            var slot = _config.Layers[_currentLayer].Slots[idx];
            var mode = slot.ExecutionMode;
            if (mode == SlotExecutionMode.MacroScript)
            {
                WpfMessageBox.Show("Macro Script モードのスロットにはファイルをドロップできません。", "DropSendTo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(slot.Command))
            {
                WpfMessageBox.Show("No app registered for this slot.", "DropSendTo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var result = _launcher.Launch(slot, paths);
            if (!result.Success)
            {
                WpfMessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(ex.Message, "Drop Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        if (this.ContextMenu != null)
        {
            AlwaysOnTopMenuItem.IsChecked = this.Topmost;
            PopulateLayoutMenu(LayoutMenuItem);
            PopulateSlotSizeMenu(SlotSizeMenuItem);
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

    private void OnSlotSizeMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        PopulateSlotSizeMenu(menuItem);
    }

    private void OnSlotSizeOptionSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not SlotSize size) return;
        if (_config.SlotSize == size) return;

        _config.SlotSize = size;
        ApplySlotLayout();
        RefreshUi();
        ClampWindowWithinBounds();
        _configService.Save(_config);
    }

    private void PopulateSlotSizeMenu(MenuItem menuItem)
    {
        if (menuItem == null) return;
        menuItem.Items.Clear();
        foreach (var (size, header) in SlotSizeOptions)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = _config.SlotSize == size,
                Tag = size
            };
            item.Click += OnSlotSizeOptionSelected;
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
            WpfMessageBox.Show(ex.Message, "Open Config", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportConfig(object sender, RoutedEventArgs e)
    {
        try
        {
            var passwordDialog = new PasswordPromptDialog(
                "コンフィグのエクスポート",
                "エクスポートファイルを保護するパスワードを入力してください。",
                requireConfirmation: true)
            {
                Owner = this
            };
            WindowCascadeService.Arrange(passwordDialog, this);
            if (passwordDialog.ShowDialog() != true)
            {
                return;
            }

            var password = passwordDialog.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                WpfMessageBox.Show("パスワードを入力してください。", "Export Config", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "エクスポート先を選択",
                Filter = "DropSendTo Export (*.dstcfg)|*.dstcfg|All files (*.*)|*.*",
                DefaultExt = ".dstcfg",
                FileName = $"DropSendTo_{DateTime.Now:yyyyMMdd_HHmmss}.dstcfg"
            };

            if (saveDialog.ShowDialog(this) != true)
            {
                return;
            }

            var payload = _configTransferService.CreateExportPayload(_config, password);
            File.WriteAllText(saveDialog.FileName, payload);
            WpfMessageBox.Show("コンフィグのエクスポートが完了しました。", "Export Config", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"Config export failed: {ex}");
            WpfMessageBox.Show("コンフィグのエクスポートに失敗しました。ログをご確認ください。", "Export Config", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnImportConfig(object sender, RoutedEventArgs e)
    {
        try
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "インポートするファイルを選択",
                Filter = "DropSendTo Export (*.dstcfg)|*.dstcfg|All files (*.*)|*.*",
                DefaultExt = ".dstcfg"
            };
            if (openDialog.ShowDialog(this) != true)
            {
                return;
            }

            var payload = File.ReadAllText(openDialog.FileName);
            var passwordDialog = new PasswordPromptDialog(
                "コンフィグのインポート",
                "エクスポート時に設定したパスワードを入力してください。",
                requireConfirmation: false)
            {
                Owner = this
            };
            WindowCascadeService.Arrange(passwordDialog, this);
            if (passwordDialog.ShowDialog() != true)
            {
                return;
            }

            var password = passwordDialog.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                WpfMessageBox.Show("パスワードを入力してください。", "Import Config", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var imported = _configTransferService.ImportConfig(payload, password);
            _config = imported;
            _currentLayer = Math.Clamp(_config.CurrentLayer, 0, 3);
            Topmost = _config.AlwaysOnTop;
            ApplySlotLayout();
            RestoreWindowPosition();
            Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
            RefreshUi();
            UpdateTrayMenuState();
            _configService.Save(_config);
            WpfMessageBox.Show("コンフィグのインポートが完了しました。", "Import Config", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (InvalidOperationException ex)
        {
            _logger.Warn($"Config import failed: {ex}");
            WpfMessageBox.Show(ex.Message, "Import Config", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger.Error($"Config import error: {ex}");
            WpfMessageBox.Show("コンフィグのインポートに失敗しました。ログをご確認ください。", "Import Config", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show(ex.Message, "Open Logs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnChangePrefix(object sender, RoutedEventArgs e)
    {
        var dlg = new PrefixDialog(_config.ShortcutPrefix, _config.ShortcutPrefixDisabled) { Owner = this };
        WindowCascadeService.Arrange(dlg, this);
        if (dlg.ShowDialog() == true)
        {
            var newPrefix = dlg.NormalizedPrefix;
            bool prefixChanged = !string.Equals(_config.ShortcutPrefix, newPrefix, StringComparison.Ordinal);
            bool disableChanged = _config.ShortcutPrefixDisabled != dlg.IsPrefixDisabled;

            _shortcutService.UpdatePrefix(newPrefix, dlg.IsPrefixDisabled);
            if (prefixChanged || disableChanged)
            {
                _config.ShortcutPrefix = newPrefix;
                _config.ShortcutPrefixDisabled = dlg.IsPrefixDisabled;
                _configService.Save(_config);
            }

            UpdateShortcutRegistrations();
        }
    }

    private void OnMinimizeToTray(object sender, RoutedEventArgs e)
    {
        MinimizeWindowToTray();
    }

    private void OnToggleAlwaysOnTop(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        Topmost = item.IsChecked;
        _config.AlwaysOnTop = Topmost;
        _configService.Save(_config);
    }

    private void OnStartupAlwaysShow(object sender, RoutedEventArgs e)
    {
        if (StartupAlwaysShowMenuItem != null)
        {
            StartupAlwaysShowMenuItem.IsChecked = true;
        }
        if (StartupRestoreMenuItem != null)
        {
            StartupRestoreMenuItem.IsChecked = false;
        }
        if (_config.StartupBehavior != StartupWindowBehavior.AlwaysShow)
        {
            _config.StartupBehavior = StartupWindowBehavior.AlwaysShow;
            _configService.Save(_config);
        }
    }

    private void OnStartupRestoreState(object sender, RoutedEventArgs e)
    {
        if (StartupRestoreMenuItem != null)
        {
            StartupRestoreMenuItem.IsChecked = true;
        }
        if (StartupAlwaysShowMenuItem != null)
        {
            StartupAlwaysShowMenuItem.IsChecked = false;
        }
        if (_config.StartupBehavior != StartupWindowBehavior.RestoreLastState)
        {
            _config.StartupBehavior = StartupWindowBehavior.RestoreLastState;
            _configService.Save(_config);
        }
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        AlwaysOnTopMenuItem.IsChecked = this.Topmost;
        if (StartupAlwaysShowMenuItem != null && StartupRestoreMenuItem != null)
        {
            StartupAlwaysShowMenuItem.IsChecked = _config.StartupBehavior == StartupWindowBehavior.AlwaysShow;
            StartupRestoreMenuItem.IsChecked = _config.StartupBehavior == StartupWindowBehavior.RestoreLastState;
        }
        PopulateLayoutMenu(LayoutMenuItem);
        PopulateSlotSizeMenu(SlotSizeMenuItem);
        UpdateTrayMenuState();
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
            if (source is WpfButton or MenuItem)
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
        if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
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
        if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            if (sender == LayerBtn1) _hoverTargetLayer = 0;
            else if (sender == LayerBtn2) _hoverTargetLayer = 1;
            else if (sender == LayerBtn3) _hoverTargetLayer = 2;
            else if (sender == LayerBtn4) _hoverTargetLayer = 3;
            if (!_layerHoverTimer.IsEnabled) _layerHoverTimer.Start();
            e.Effects = WpfDragDropEffects.Link;
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
        b.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(32, 32, 32));
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
        b.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(48, 48, 48));
        b.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(200, 200, 200));
    }

    private void OnSlotDragEnter(object sender, DragEventArgs e)
    {
        if (sender is not Border b) return;
        int idx = GetSlotIndex(b);
        if (GetSlotMacroState(idx) != SlotMacroState.Idle) return;
        b.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(48, 48, 48));
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

        var slotTitle = slot.Title?.ReplaceLineEndings(" ").Trim() ?? string.Empty;
        if (slotTitle.Length == 0)
        {
            slotTitle = "(untitled)";
        }
        var mode = slot.ExecutionMode;
        var script = slot.KeyboardMacroScript ?? string.Empty;
        var macroConfigured = !string.IsNullOrWhiteSpace(script);
        var commandConfigured = !string.IsNullOrWhiteSpace(slot.Command);
        _logger.Info($"Trigger requested (layer={layerIndex + 1}, slot={slotIndex + 1}, title=\"{slotTitle}\", source={source}, mode={mode}, macroConfigured={macroConfigured}, commandConfigured={commandConfigured})");

        if (!macroConfigured && !commandConfigured) return;

        bool isCommandOnly = mode == SlotExecutionMode.Command;
        bool shouldRunMacro = mode != SlotExecutionMode.Command && macroConfigured;

        if (_macroService.IsMacroRunning && !isCommandOnly)
        {
            if (_runningSlotLayerIndex.HasValue &&
                _runningSlotLayerIndex.Value == layerIndex &&
                _runningSlotIndex.HasValue &&
                _runningSlotIndex.Value == slotIndex &&
                shouldRunMacro)
            {
                if (_macroService.CancelCurrentMacro())
                {
                    _logger.Info($"Requested cancel for running macro (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}).");
                    MarkSlotMacroCanceling(layerIndex, slotIndex);
                }
            }
            else
            {
                _logger.Warn($"Rejected trigger while another macro is running (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}).");
                WpfMessageBox.Show("別のスロットのマクロが実行中です。完了または停止してから再度実行してください。", "Macro Running", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }
        if (_macroService.IsMacroRunning && isCommandOnly)
        {
            _logger.Info($"Command-only slot triggered while macro is active (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}).");
        }

        MacroExecutionContext? macroContext = null;
        if (mode == SlotExecutionMode.MacroScriptExtended && commandConfigured)
        {
            var contextTitle = slotTitle;
            macroContext = new MacroExecutionContext(
                SlotExecutionMode.MacroScriptExtended,
                overrideArgs =>
                {
                    var launchResult = _launcher.Launch(slot, Array.Empty<string>(), overrideArgs);
                    if (!launchResult.Success)
                    {
                        _logger.Warn($"Command launch failed via macro (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}): {launchResult.Message}");
                    }
                    return launchResult;
                },
                contextTitle,
                slot.Command ?? string.Empty);
        }

        if (shouldRunMacro)
        {
            BeginSlotMacro(layerIndex, slotIndex);
            try
            {
                var macroResult = await _macroService.RunMacroAsync(script, macroContext);
                if (!macroResult.Success)
                {
                    if (macroResult.IsCanceled)
                    {
                        _logger.Info($"Macro canceled (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}).");
                    }
                    else
                    {
                        _logger.Warn($"Macro failed (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}): {macroResult.Message}");
                    }
                    if (!macroResult.IsCanceled)
                    {
                        WpfMessageBox.Show(macroResult.Message, "Macro Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return;
                }
                _logger.Info($"Macro completed (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}).");
            }
            catch (Exception ex)
            {
                _logger.Error($"Macro execution failed (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}): {ex}");
                WpfMessageBox.Show("マクロの実行に失敗しました。ログを確認してください。", "Macro Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                ClearSlotMacroState(layerIndex, slotIndex);
            }
        }

        if (mode == SlotExecutionMode.Command && commandConfigured)
        {
            _logger.Info($"Launching command for layer={layerIndex + 1}, slot={slotIndex + 1}, title=\"{slotTitle}\": {slot.Command}");
            var result = _launcher.Launch(slot, Array.Empty<string>());
            if (!result.Success)
            {
                _logger.Warn($"Command launch failed (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}): {result.Message}");
                WpfMessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                _logger.Info($"Command launch succeeded (layer={layerIndex + 1}, slot={slotIndex + 1}, source={source}).");
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

    private void OnPrefixActivationRequested(object? sender, EventArgs e)
    {
        if (_windowPlacementMode == WindowPlacementMode.MouseFollow)
        {
            PositionWindowAtMouse();
        }
        BringWindowToForeground();
    }

    private void OnPrefixMinimizeRequested(object? sender, EventArgs e)
    {
        MinimizeWindowToTray();
    }

    private void OnPrefixMacroCancelRequested(object? sender, EventArgs e)
    {
        if (!_macroService.IsMacroRunning)
        {
            return;
        }

        if (_macroService.CancelCurrentMacro())
        {
            _logger.Info("Requested cancel for running macro via prefix shortcut.");
            if (_runningSlotLayerIndex.HasValue && _runningSlotIndex.HasValue)
            {
                MarkSlotMacroCanceling(_runningSlotLayerIndex.Value, _runningSlotIndex.Value);
            }
        }
        else
        {
            _logger.Warn("Prefix cancel macro shortcut could not cancel the current macro.");
        }
    }

    private void OnPrefixPositionToggleRequested(object? sender, EventArgs e)
    {
        ToggleWindowPlacementMode();
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
        bool shouldToggleNotify = !_macroService.IsMacroRunning;

        try
        {
            if (shouldToggleNotify)
            {
                UpdateNotifyIconState(true);
            }
            var result = await _macroService.RunMacroAsync("PREFIX PASSTHROUGH");
            if (!result.Success && !result.IsCanceled)
            {
                _logger.Warn($"Prefix passthrough failed: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Prefix passthrough execution failed: {ex}");
        }
        finally
        {
            if (shouldToggleNotify && !_macroService.IsMacroRunning)
            {
                UpdateNotifyIconState(false);
            }
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

        if (_shortcutService.IsPrefixDisabled)
        {
            _shortcutBindings.Clear();
            _shortcutService.UpdateAvailableShortcuts(Array.Empty<string>());
            return;
        }

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
        void SetState(WpfButton b, bool active)
        {
            if (active)
            {
                // Black-based emphasis: brighter gray background and strong border
                b.Opacity = 1.0;
                b.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x22, 0x22, 0x22));
                b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                b.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                b.FontWeight = System.Windows.FontWeights.SemiBold;
            }
            else
            {
                b.Opacity = 0.9;
                b.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x11, 0x11, 0x11));
                b.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x44, 0x44, 0x44));
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
        if (_notifyIcon != null)
        {
            _notifyIcon.MouseClick -= OnNotifyIconMouseClick;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _notifyIconDefault?.Dispose();
        _notifyIconDefault = null;
        _notifyIconActive?.Dispose();
        _notifyIconActive = null;
        _shortcutService.ShortcutTriggered -= OnShortcutTriggered;
        _shortcutService.PrefixPassthroughRequested -= OnPrefixPassthroughRequested;
        _shortcutService.PrefixActivationRequested -= OnPrefixActivationRequested;
        _shortcutService.PrefixMacroCancelRequested -= OnPrefixMacroCancelRequested;
        _shortcutService.PrefixMinimizeRequested -= OnPrefixMinimizeRequested;
        _shortcutService.PrefixPositionToggleRequested -= OnPrefixPositionToggleRequested;
        _shortcutService.PrefixStateChanged -= OnPrefixStateChanged;
        _shortcutService.Dispose();
        _macroService.Dispose();
    }

    private void ToggleWindowPlacementMode()
    {
        var next = _windowPlacementMode == WindowPlacementMode.Fixed
            ? WindowPlacementMode.MouseFollow
            : WindowPlacementMode.Fixed;
        SetWindowPlacementMode(next, initiatedByToggle: true);
    }

    private void SetWindowPlacementMode(WindowPlacementMode mode, bool initiatedByToggle)
    {
        if (_windowPlacementMode == mode)
        {
            if (mode == WindowPlacementMode.MouseFollow && initiatedByToggle)
            {
                PositionWindowAtMouse();
            }
            return;
        }

        if (mode == WindowPlacementMode.MouseFollow)
        {
            CaptureFixedWindowPosition(clampBeforeStoring: true);
            _windowPlacementMode = mode;
            _config.WindowPlacementMode = mode;
            if (initiatedByToggle)
            {
                PositionWindowAtMouse();
            }
        }
        else
        {
            _windowPlacementMode = mode;
            _config.WindowPlacementMode = mode;
            RestoreWindowPosition();
        }

        _configService.Save(_config);
    }

    private void CaptureFixedWindowPosition(bool clampBeforeStoring)
    {
        if (_windowPlacementMode != WindowPlacementMode.Fixed)
        {
            return;
        }

        if (clampBeforeStoring)
        {
            ClampWindowWithinBounds();
        }
        _config.WindowLeft = this.Left;
        _config.WindowTop = this.Top;
    }

    private void PositionWindowAtMouse()
    {
        try
        {
            var cursorPosition = Forms.Control.MousePosition;
            var rect = GetWindowRect(this.Left, this.Top);
            double newLeft = cursorPosition.X - (rect.Width / 2.0);
            double newTop = cursorPosition.Y - (rect.Height / 2.0);
            var bounds = ScreenBoundsResolver.ForRect(this, new Rect(newLeft, newTop, rect.Width, rect.Height));
            var (left, top) = _placement.Clamp(newLeft, newTop, bounds, rect.Width, rect.Height);
            Left = left;
            Top = top;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to position window at mouse: {ex.Message}");
        }
    }
}
