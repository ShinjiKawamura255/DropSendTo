using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DropSendTo.Models;
using DropSendTo.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using DragEventArgs = System.Windows.DragEventArgs;
using System.Windows.Media.Effects;
using WpfMessageBox = System.Windows.MessageBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

using WpfButton = System.Windows.Controls.Button;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using DrawingIcon = System.Drawing.Icon;
using DrawingPoint = System.Drawing.Point;

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
    private SlotShortcutListWindow? _shortcutListWindow;
    private bool _keyboardNavigationActive;
    private int _keyboardSelectedSlotIndex = -1;
    private int _lastLayerNavigationDirection;
    private bool _suppressLayerSelectionForPrefix;
    private bool _metaPrefixPending;
    private DateTime _metaPrefixExpiryUtc;
    private int _prefixGuardToken;
    private Forms.NotifyIcon? _notifyIcon;
    private DrawingIcon? _notifyIconDefault;
    private DrawingIcon? _notifyIconActive;
    private bool _isMinimizedToTray;
    private bool _suppressFixedCapture;
    private bool _suppressFixedCaptureDuringSearch;
    private bool _applySearchPlacementForCurrentSearch;
    private IReadOnlyList<WpfButton>? _layerButtons;
    private readonly DispatcherTimer _positionSaveTimer;
    private bool _pendingPositionSave;
    private bool _blockLocationSave;
    private bool _minimizeOnLoaded;
    private LayerNameOverlayWindow? _layerNameOverlayWindow;
    private CancellationTokenSource? _layerNameOverlayCts;
    private SearchOverlayWindow? _searchOverlayWindow;
    private bool _searchLayerActive;
    private bool _isSearchOverlayBelowMain;
    private SearchOverlayPlacementMode _searchPlacementMode;
    private AppLanguage _currentLanguage;
    private string _searchQuery = string.Empty;
    private PrefixSearchRestoreContext? _prefixSearchRestoreContext;
    private readonly List<SearchResult> _searchResults = new();
    private readonly List<VisibleSlotMapping> _visibleSlotMappings = new();
    private static readonly SolidColorBrush PrefixArmedBackgroundBrush;
    private static readonly SolidColorBrush PrefixArmedBorderBrush;
    private static readonly SolidColorBrush PrefixArmedForegroundBrush;
    private static readonly IReadOnlyDictionary<SlotAccentColor, SlotColorScheme> SlotColorSchemes;
    private const int MinSlotRows = 2;
    private const int MaxSlotRows = 8;
    private const int MinSlotColumns = 2;
    private const int MaxSlotColumns = 8;
    private const int MinLayers = 4;
    private const int MaxLayers = 8;
    private enum ShowLayerTrigger
    {
        MouseGesture,
        Prefix
    }
    private static readonly (SlotSize size, string header)[] SlotSizeOptions =
    {
        (SlotSize.Large, "Large"),
        (SlotSize.Medium, "Medium"),
        (SlotSize.Small, "Small"),
        (SlotSize.Custom, "Custom...")
    };
    private const double LayoutEditIndicatorTopSpacing = 4;
    private const double LayoutEditIndicatorBottomSpacing = 6;
    private const double LayoutEditIndicatorFallbackHeight = 22;
    private readonly record struct UiText(
        string ConfigMenu,
        string OpenConfig,
        string OpenLogs,
        string ExportConfig,
        string ImportConfig,
        string ChangePrefix,
        string SearchHotkey,
        string LayerSettings,
        string LayerCount,
        string EditLayerNames,
        string LayoutDisplay,
        string SlotLayout,
        string SlotSize,
        string HideEmptySlotNames,
        string StartupWindow,
        string StartupAlwaysShow,
        string StartupRestore,
        string MacroMode,
        string MacroExclusive,
        string MacroInterrupt,
        string MacroSuspend,
        string KeyboardNavigation,
        string EmacsNavigation,
        string ViNavigation,
        string MouseGestures,
        string DisplayBehavior,
        string ShowLayerPreference,
        string KeyboardPlacement,
        string MousePlacement,
        string SearchPlacement,
        string FollowKeyboardPlacement,
        string PlacementFixed,
        string PlacementMouseFollow,
        string PlacementScreenCenter,
        string PrefixLayerShortcut,
        string RemoteSessionPriority,
        string MinimizeTriggers,
        string ShortcutList,
        string LanguageMenu,
        string LanguageJapanese,
        string LanguageEnglish,
        string AlwaysOnTop,
        string MinimizeToTray,
        string Exit,
        string SearchLabel);

    private static readonly UiText JapaneseText = new(
        ConfigMenu: "設定ファイル / ログ",
        OpenConfig: "設定ファイルを開く",
        OpenLogs: "ログを開く",
        ExportConfig: "設定をエクスポート...",
        ImportConfig: "設定をインポート...",
        ChangePrefix: "Prefix を変更...",
        SearchHotkey: "検索ホットキー...",
        LayerSettings: "レイヤー設定",
        LayerCount: "レイヤー数...",
        EditLayerNames: "レイヤー名を編集...",
        LayoutDisplay: "レイアウト / 表示",
        SlotLayout: "Slot Layout...",
        SlotSize: "Slot Size",
        HideEmptySlotNames: "未登録スロット名を非表示",
        StartupWindow: "起動時のウィンドウ",
        StartupAlwaysShow: "常にウィンドウを表示",
        StartupRestore: "前回の状態を復元",
        MacroMode: "Macro 実行モード",
        MacroExclusive: "排他（単一実行のみ）",
        MacroInterrupt: "割り込み実行（実行中マクロを停止）",
        MacroSuspend: "一時停止して割り込み（完了後に元マクロ再開）",
        KeyboardNavigation: "キーボード操作",
        EmacsNavigation: "Emacs ライク操作",
        ViNavigation: "vi ライク操作",
        MouseGestures: "マウスジェスチャ (表示/最小化)...",
        DisplayBehavior: "表示挙動",
        ShowLayerPreference: "表示時のレイヤー切替...",
        KeyboardPlacement: "表示位置 (キーボード)",
        MousePlacement: "表示位置 (マウスジェスチャ)",
        SearchPlacement: "表示位置 (検索)",
        FollowKeyboardPlacement: "キーボード設定に追従",
        PlacementFixed: "固定位置",
        PlacementMouseFollow: "マウスフォロー",
        PlacementScreenCenter: "画面中央 (マウスがある画面)",
        PrefixLayerShortcut: "Prefix: Ctrl+N/P でレイヤー切替",
        RemoteSessionPriority: "Prefix: リモートセッション優先 (RDP/Citrix)",
        MinimizeTriggers: "最小化トリガー...",
        ShortcutList: "ショートカット一覧...",
        LanguageMenu: "Language",
        LanguageJapanese: "日本語",
        LanguageEnglish: "English",
        AlwaysOnTop: "常に最前面に表示",
        MinimizeToTray: "Minimize to Tray",
        Exit: "Exit",
        SearchLabel: "検索");

    private static readonly UiText EnglishText = new(
        ConfigMenu: "Config / Logs",
        OpenConfig: "Open Config",
        OpenLogs: "Open Logs",
        ExportConfig: "Export Config...",
        ImportConfig: "Import Config...",
        ChangePrefix: "Change Prefix...",
        SearchHotkey: "Search Hotkey...",
        LayerSettings: "Layer Settings",
        LayerCount: "Layer Count...",
        EditLayerNames: "Edit Layer Names...",
        LayoutDisplay: "Layout / Display",
        SlotLayout: "Slot Layout...",
        SlotSize: "Slot Size",
        HideEmptySlotNames: "Hide empty slot names",
        StartupWindow: "Startup Window",
        StartupAlwaysShow: "Always show window",
        StartupRestore: "Restore last state",
        MacroMode: "Macro Mode",
        MacroExclusive: "Exclusive (single run only)",
        MacroInterrupt: "Interrupt running macro",
        MacroSuspend: "Pause and resume",
        KeyboardNavigation: "Keyboard Navigation",
        EmacsNavigation: "Emacs-like navigation",
        ViNavigation: "vi-like navigation",
        MouseGestures: "Mouse gestures (show/minimize)...",
        DisplayBehavior: "Display Behavior",
        ShowLayerPreference: "Show-layer preference...",
        KeyboardPlacement: "Placement (keyboard)",
        MousePlacement: "Placement (mouse gesture)",
        SearchPlacement: "Placement (search)",
        FollowKeyboardPlacement: "Follow keyboard setting",
        PlacementFixed: "Fixed position",
        PlacementMouseFollow: "Follow mouse",
        PlacementScreenCenter: "Screen center (cursor screen)",
        PrefixLayerShortcut: "Prefix: Switch layers with Ctrl+N/P",
        RemoteSessionPriority: "Prefix: Prefer remote session (RDP/Citrix)",
        MinimizeTriggers: "Minimize triggers...",
        ShortcutList: "Shortcut list...",
        LanguageMenu: "Language",
        LanguageJapanese: "Japanese",
        LanguageEnglish: "English",
        AlwaysOnTop: "Always on top",
        MinimizeToTray: "Minimize to Tray",
        Exit: "Exit",
        SearchLabel: "Search");

    private readonly record struct SlotSizeMetrics(
        double BaseWidth,
        double BaseHeight,
        double ColumnStep,
        double RowStep,
        double SlotHeight,
        double TitleFontSize,
        double StatusFontSize,
        TextWrapping TitleWrapping,
        TextTrimming TitleTrimming,
        int TitleVisibleLines,
        bool OverlayStatus,
        double SlotMargin);

    private static readonly SlotSizeMetrics LargeSlotMetrics = new(
        BaseWidth: 240,
        BaseHeight: 180,
        ColumnStep: 95,
        RowStep: 70,
        SlotHeight: 64,
        TitleFontSize: 12,
        StatusFontSize: 11,
        TitleWrapping: TextWrapping.Wrap,
        TitleTrimming: TextTrimming.CharacterEllipsis,
        TitleVisibleLines: 3,
        OverlayStatus: false,
        SlotMargin: 2);

    private static readonly SlotSizeMetrics MediumSlotMetrics = new(
        BaseWidth: 218,
        BaseHeight: 148,
        ColumnStep: 82,
        RowStep: 55,
        SlotHeight: 46,
        TitleFontSize: 11,
        StatusFontSize: 10,
        TitleWrapping: TextWrapping.Wrap,
        TitleTrimming: TextTrimming.CharacterEllipsis,
        TitleVisibleLines: 2,
        OverlayStatus: false,
        SlotMargin: 2);

    private static readonly SlotSizeMetrics SmallSlotMetrics = new(
        BaseWidth: 234,
        BaseHeight: 120,
        ColumnStep: 70,
        RowStep: 36,
        SlotHeight: 32,
        TitleFontSize: 10,
        StatusFontSize: 9,
        TitleWrapping: TextWrapping.NoWrap,
        TitleTrimming: TextTrimming.CharacterEllipsis,
        TitleVisibleLines: 1,
        OverlayStatus: true,
        SlotMargin: 2);

    static MainWindow()
    {
        PrefixArmedBackgroundBrush = CreateFrozenBrush(MediaColor.FromRgb(0x1E, 0x82, 0x4C));
        PrefixArmedBorderBrush = CreateFrozenBrush(MediaColor.FromRgb(0x7C, 0xFF, 0xB0));
        PrefixArmedForegroundBrush = CreateFrozenBrush(System.Windows.Media.Colors.White);
        SlotColorSchemes = new Dictionary<SlotAccentColor, SlotColorScheme>
        {
            [SlotAccentColor.Default] = new(
                CreateFrozenBrush(MediaColor.FromRgb(0x11, 0x11, 0x11)),
                CreateFrozenBrush(MediaColor.FromRgb(0x33, 0x33, 0x33)),
                CreateFrozenBrush(System.Windows.Media.Colors.White)),
            [SlotAccentColor.Teal] = new(
                CreateFrozenBrush(MediaColor.FromRgb(0x10, 0x2A, 0x30)),
                CreateFrozenBrush(MediaColor.FromRgb(0x1F, 0x76, 0x7D)),
                CreateFrozenBrush(MediaColor.FromRgb(0xE4, 0xFD, 0xFF))),
            [SlotAccentColor.Indigo] = new(
                CreateFrozenBrush(MediaColor.FromRgb(0x16, 0x15, 0x2E)),
                CreateFrozenBrush(MediaColor.FromRgb(0x4E, 0x52, 0xA6)),
                CreateFrozenBrush(MediaColor.FromRgb(0xF4, 0xF2, 0xFF))),
            [SlotAccentColor.Amber] = new(
                CreateFrozenBrush(MediaColor.FromRgb(0x2D, 0x1F, 0x0F)),
                CreateFrozenBrush(MediaColor.FromRgb(0xB5, 0x6B, 0x17)),
                CreateFrozenBrush(MediaColor.FromRgb(0xFF, 0xE8, 0xC2))),
            [SlotAccentColor.Olive] = new(
                CreateFrozenBrush(MediaColor.FromRgb(0x20, 0x27, 0x12)),
                CreateFrozenBrush(MediaColor.FromRgb(0x6E, 0x8C, 0x23)),
                CreateFrozenBrush(MediaColor.FromRgb(0xF0, 0xFF, 0xD8))),
            [SlotAccentColor.Crimson] = new(
                CreateFrozenBrush(MediaColor.FromRgb(0x2B, 0x11, 0x16)),
                CreateFrozenBrush(MediaColor.FromRgb(0xB5, 0x45, 0x4F)),
                CreateFrozenBrush(MediaColor.FromRgb(0xFF, 0xE3, 0xE7)))
        };
    }

    private static SolidColorBrush CreateFrozenBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private int _hoverTargetLayer = -1;
    private int _hoverNavigationDirection;
    private sealed record LayerButtonModel(string Content, object Tag, bool IsLayer, int LayerIndex, string? ToolTip, bool Visible)
    {
        public static LayerButtonModel Layer(int layerIndex) =>
            new((layerIndex + 1).ToString(CultureInfo.InvariantCulture), layerIndex, true, layerIndex, $"Layer {layerIndex + 1}", true);

        public static LayerButtonModel Arrow(string content, string tag, string toolTip) =>
            new(content, tag, false, -1, toolTip, true);

        public static LayerButtonModel Hidden { get; } = new(string.Empty, string.Empty, false, -1, null, false);
    }
    private AppConfig _config;
    private int _currentLayer = 0; // 0-based
    private readonly Stack<SlotRunContext> _slotRunStack = new();
    private SlotRunContext? _currentSlotRun;
    private WindowPlacementMode _keyboardPlacementMode;
    private WindowPlacementMode _mousePlacementMode;
    private bool _mousePlacementFollowsKeyboard;
    private bool _searchPlacementFollowsKeyboard;
    private bool _isSlotLayoutEditMode;
    private System.Windows.Point? _slotDragStartPoint;
    private bool _isSlotLayoutDragInProgress;
    private int _slotLayoutDragSourceLayer = -1;
    private int _slotLayoutDragSourceIndex = -1;
    private int _slotLayoutPreviewTargetLayer = -1;
    private int _slotLayoutPreviewTargetIndex = -1;
    private const string SlotLayoutDragFormat = "DropSendTo/SlotLayoutEdit";

    public MainWindow()
    {
        InitializeComponent();
        AttachSubmenuPlacementHandler(this.ContextMenu);
        SourceInitialized += OnSourceInitialized;
        InitializeNotifyIcon();
        _configService = new ConfigService();
        _launcher = new LauncherService();
        _config = _configService.LoadOrCreate();
        _keyboardPlacementMode = _config.KeyboardPlacementMode;
        _mousePlacementMode = _config.MousePlacementMode;
        _mousePlacementFollowsKeyboard = _config.MousePlacementFollowsKeyboard;
        _searchPlacementFollowsKeyboard = _config.SearchPlacementFollowsKeyboard;
        _searchPlacementMode = Enum.IsDefined(typeof(SearchOverlayPlacementMode), (int)_config.SearchPlacementMode)
            ? _config.SearchPlacementMode
            : SearchOverlayPlacementMode.Fixed;
        _applySearchPlacementForCurrentSearch = false;
        _layerButtons = new[] { LayerBtn1, LayerBtn2, LayerBtn3, LayerBtn4 };
        _positionSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _positionSaveTimer.Tick += OnPositionSaveTick;
        _minimizeOnLoaded = _config.StartupBehavior == StartupWindowBehavior.RestoreLastState
                            && _config.LastWindowVisibility == WindowVisibilityState.Tray;
        Loaded += OnLoaded;
        ApplyTopmostState();
        _currentLanguage = _config.Language;
        int totalLayers = Math.Max(_config.Layers?.Count ?? MinLayers, MinLayers);
        _currentLayer = Math.Clamp(_config.CurrentLayer, 0, totalLayers - 1);
        if (EditModeIndicator is { } indicator)
        {
            indicator.SizeChanged += (_, _) =>
            {
                if (_isSlotLayoutEditMode)
                {
                    UpdateSlotPanelEditModePadding();
                    UpdateWindowSize(_config.SlotRows, _config.SlotColumns);
                }
            };
        }

        ApplySlotLayout();
        RestoreWindowPosition();
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
        RefreshUi();
        UpdateTrayMenuState();
        ApplyLanguageToUi();
        LocationChanged += OnWindowLocationChanged;
        this.Closing += (_, _) =>
        {
            if (_keyboardPlacementMode == WindowPlacementMode.Fixed)
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
            _layerNameOverlayCts?.Cancel();
            _layerNameOverlayWindow?.Close();
        };

        _layerHoverTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = System.TimeSpan.FromMilliseconds(480)
        };
        _layerHoverTimer.Tick += (_, _) =>
        {
            if (_hoverNavigationDirection != 0)
            {
                var total = _config?.Layers?.Count ?? 0;
                if (total <= 0)
                {
                    _layerHoverTimer.Stop();
                    return;
                }
                int next = Math.Clamp(_currentLayer + _hoverNavigationDirection, 0, total - 1);
                if (next == _currentLayer)
                {
                    _layerHoverTimer.Stop();
                    return;
                }
                SetLayer(next);
                _layerHoverTimer.Start();
            }
            else if (_hoverTargetLayer >= 0)
            {
                SetLayer(_hoverTargetLayer);
                _layerHoverTimer.Stop();
            }
        };

        SystemEvents.SessionSwitch += OnSystemSessionSwitch;
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

    private static void UpdateSlotStatusVisual(SlotVisual visual, string text, MediaColor color, bool isVisible)
    {
        visual.Status.Text = text;
        visual.Status.Foreground = new System.Windows.Media.SolidColorBrush(color);
        visual.Status.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        if (visual.OverlayStatus)
        {
            visual.Title.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static SlotColorScheme GetSlotColorScheme(SlotAccentColor accent)
    {
        if (SlotColorSchemes.TryGetValue(accent, out var scheme))
        {
            return scheme;
        }

        return SlotColorSchemes[SlotAccentColor.Default];
    }

    private void RenderEmptySlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotVisuals.Count)
        {
            return;
        }

        var visual = _slotVisuals[slotIndex];
        visual.Title.Text = string.Empty;
        visual.DragPreviewHost.Visibility = Visibility.Collapsed;
        UpdateSlotStatusVisual(visual, string.Empty, MediaColor.FromRgb(0x7C, 0xFF, 0xB0), false);
        ApplySlotColor(slotIndex);
    }

    private void ApplySlotColor(int slotIndex)
    {
        if (_config == null || slotIndex < 0 || slotIndex >= _slotVisuals.Count)
        {
            return;
        }

        var visual = _slotVisuals[slotIndex];

        if (!TryGetVisibleSlotModel(slotIndex, out _, out _, out var slot))
        {
            var defaultScheme = GetSlotColorScheme(SlotAccentColor.Default);
            visual.Border.Background = defaultScheme.Background;
            visual.Border.BorderBrush = defaultScheme.Border;
            visual.Title.Foreground = defaultScheme.Title;
            visual.Border.IsEnabled = false;
            visual.Border.Opacity = 0.7;
            return;
        }

        var scheme = GetSlotColorScheme(slot.AccentColor);
        visual.Border.Background = scheme.Background;
        visual.Border.BorderBrush = scheme.Border;
        visual.Title.Foreground = scheme.Title;
        visual.Border.IsEnabled = true;
        visual.Border.Opacity = 1;
    }

    private void UpdateKeyboardSelectionVisual()
    {
        if (_slotVisuals.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _slotVisuals.Count; i++)
        {
            bool isSelected = _keyboardNavigationActive && i == _keyboardSelectedSlotIndex;
            bool hasSlot = TryGetVisibleSlot(i, out _, out _);
            ApplyKeyboardSelectionVisual(_slotVisuals[i], isSelected && hasSlot);
        }
    }

    private static void ApplyKeyboardSelectionVisual(SlotVisual visual, bool isSelected)
    {
        if (isSelected)
        {
            visual.Border.Effect = new DropShadowEffect
            {
                Color = System.Windows.Media.Colors.DeepSkyBlue,
                ShadowDepth = 0,
                BlurRadius = 12,
                Opacity = 0.9
            };
        }
        else
        {
            visual.Border.Effect = null;
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
                _suppressFixedCapture = false;
                PositionWindowAtFixedLocation();
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
        CloseSearchLayer();
        HideLayerNameOverlayImmediate();
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

    private static UiText GetUiText(AppLanguage language) =>
        language == AppLanguage.English ? EnglishText : JapaneseText;

    private void ApplyLanguageToUi()
    {
        _currentLanguage = _config?.Language ?? AppLanguage.Japanese;
        var text = GetUiText(_currentLanguage);

        if (ConfigMenuItem != null) ConfigMenuItem.Header = text.ConfigMenu;
        if (OpenConfigMenuItem != null) OpenConfigMenuItem.Header = text.OpenConfig;
        if (OpenLogsMenuItem != null) OpenLogsMenuItem.Header = text.OpenLogs;
        if (ExportConfigMenuItem != null) ExportConfigMenuItem.Header = text.ExportConfig;
        if (ImportConfigMenuItem != null) ImportConfigMenuItem.Header = text.ImportConfig;
        if (ChangePrefixMenuItem != null) ChangePrefixMenuItem.Header = text.ChangePrefix;
        if (SearchHotkeyMenuItem != null) SearchHotkeyMenuItem.Header = text.SearchHotkey;
        if (LayerSettingsMenuItem != null) LayerSettingsMenuItem.Header = text.LayerSettings;
        if (LayerCountMenuItem != null) LayerCountMenuItem.Header = text.LayerCount;
        if (EditLayerNamesMenuItem != null) EditLayerNamesMenuItem.Header = text.EditLayerNames;
        if (LayoutDisplayMenuItem != null) LayoutDisplayMenuItem.Header = text.LayoutDisplay;
        if (LayoutMenuItem != null) LayoutMenuItem.Header = text.SlotLayout;
        if (SlotSizeMenuItem != null) SlotSizeMenuItem.Header = text.SlotSize;
        if (HideEmptySlotNamesMenuItem != null) HideEmptySlotNamesMenuItem.Header = text.HideEmptySlotNames;
        if (StartupWindowMenuItem != null) StartupWindowMenuItem.Header = text.StartupWindow;
        if (StartupAlwaysShowMenuItem != null) StartupAlwaysShowMenuItem.Header = text.StartupAlwaysShow;
        if (StartupRestoreMenuItem != null) StartupRestoreMenuItem.Header = text.StartupRestore;
        if (MacroModeMenuItem != null) MacroModeMenuItem.Header = text.MacroMode;
        if (MacroModeExclusiveMenuItem != null) MacroModeExclusiveMenuItem.Header = text.MacroExclusive;
        if (MacroModeInterruptMenuItem != null) MacroModeInterruptMenuItem.Header = text.MacroInterrupt;
        if (MacroModeSuspendMenuItem != null) MacroModeSuspendMenuItem.Header = text.MacroSuspend;
        if (KeyboardNavigationMenuItem != null) KeyboardNavigationMenuItem.Header = text.KeyboardNavigation;
        if (EmacsNavigationMenuItem != null) EmacsNavigationMenuItem.Header = text.EmacsNavigation;
        if (ViNavigationMenuItem != null) ViNavigationMenuItem.Header = text.ViNavigation;
        if (MouseGesturesMenuItem != null) MouseGesturesMenuItem.Header = text.MouseGestures;
        if (DisplayBehaviorMenuItem != null) DisplayBehaviorMenuItem.Header = text.DisplayBehavior;
        if (ShowLayerPreferenceMenuItem != null) ShowLayerPreferenceMenuItem.Header = text.ShowLayerPreference;
        if (KeyboardPlacementMenuItem != null) KeyboardPlacementMenuItem.Header = text.KeyboardPlacement;
        if (MousePlacementMenuItem != null) MousePlacementMenuItem.Header = text.MousePlacement;
        if (SearchPlacementMenuItem != null) SearchPlacementMenuItem.Header = text.SearchPlacement;
        if (MousePlacementFollowKeyboardMenuItem != null) MousePlacementFollowKeyboardMenuItem.Header = text.FollowKeyboardPlacement;
        if (SearchPlacementFollowKeyboardMenuItem != null) SearchPlacementFollowKeyboardMenuItem.Header = text.FollowKeyboardPlacement;
        if (MousePlacementFixedMenuItem != null) MousePlacementFixedMenuItem.Header = text.PlacementFixed;
        if (MousePlacementFollowMouseMenuItem != null) MousePlacementFollowMouseMenuItem.Header = text.PlacementMouseFollow;
        if (MousePlacementScreenCenterMenuItem != null) MousePlacementScreenCenterMenuItem.Header = text.PlacementScreenCenter;
        if (KeyboardPlacementFixedMenuItem != null) KeyboardPlacementFixedMenuItem.Header = text.PlacementFixed;
        if (KeyboardPlacementFollowMouseMenuItem != null) KeyboardPlacementFollowMouseMenuItem.Header = text.PlacementMouseFollow;
        if (KeyboardPlacementScreenCenterMenuItem != null) KeyboardPlacementScreenCenterMenuItem.Header = text.PlacementScreenCenter;
        if (SearchPlacementFixedMenuItem != null) SearchPlacementFixedMenuItem.Header = text.PlacementFixed;
        if (SearchPlacementFollowMouseMenuItem != null) SearchPlacementFollowMouseMenuItem.Header = text.PlacementMouseFollow;
        if (SearchPlacementScreenCenterMenuItem != null) SearchPlacementScreenCenterMenuItem.Header = text.PlacementScreenCenter;
        if (PrefixLayerShortcutMenuItem != null) PrefixLayerShortcutMenuItem.Header = text.PrefixLayerShortcut;
        if (RemoteSessionPriorityMenuItem != null) RemoteSessionPriorityMenuItem.Header = text.RemoteSessionPriority;
        if (MinimizeTriggersMenuItem != null) MinimizeTriggersMenuItem.Header = text.MinimizeTriggers;
        if (ShortcutListMenuItem != null) ShortcutListMenuItem.Header = text.ShortcutList;
        if (LanguageMenuItem != null) LanguageMenuItem.Header = text.LanguageMenu;
        if (LanguageJapaneseMenuItem != null) LanguageJapaneseMenuItem.Header = text.LanguageJapanese;
        if (LanguageEnglishMenuItem != null) LanguageEnglishMenuItem.Header = text.LanguageEnglish;
        if (AlwaysOnTopMenuItem != null) AlwaysOnTopMenuItem.Header = text.AlwaysOnTop;
        if (MinimizeToTrayMenuItem != null) MinimizeToTrayMenuItem.Header = text.MinimizeToTray;
        if (ExitMenuItem != null) ExitMenuItem.Header = text.Exit;

        if (_searchOverlayWindow != null)
        {
            _searchOverlayWindow.SetSearchLabel(text.SearchLabel);
        }

        UpdateLanguageMenuState();
    }

    private void UpdateLanguageMenuState()
    {
        if (LanguageJapaneseMenuItem != null)
        {
            LanguageJapaneseMenuItem.IsChecked = _currentLanguage == AppLanguage.Japanese;
        }
        if (LanguageEnglishMenuItem != null)
        {
            LanguageEnglishMenuItem.IsChecked = _currentLanguage == AppLanguage.English;
        }
    }

    private bool GetDesiredTopmost()
    {
        return _isSlotLayoutEditMode || (_config?.AlwaysOnTop ?? true);
    }

    private void ApplyTopmostState()
    {
        bool desiredTopmost = GetDesiredTopmost();
        Topmost = true;
        Topmost = desiredTopmost;
        UpdateOverlayTopmost();
    }

    private void BringWindowToForeground()
    {
        RestoreWindowFromTray();

        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle != IntPtr.Zero)
        {
            ForceForegroundWindow(handle);
        }

        Activate();
        Focus();

        ApplyTopmostState();
    }

    private static void ForceForegroundWindow(IntPtr handle)
    {
        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }

        var foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = foreground != IntPtr.Zero
            ? NativeMethods.GetWindowThreadProcessId(foreground, out _)
            : 0;
        uint currentThread = NativeMethods.GetCurrentThreadId();
        bool attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            }

            NativeMethods.BringWindowToTop(handle);
            NativeMethods.SetForegroundWindow(handle);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private void ApplySlotLayout()
    {
        int rows = Math.Clamp(_config.SlotRows, MinSlotRows, MaxSlotRows);
        int columns = Math.Clamp(_config.SlotColumns, MinSlotColumns, MaxSlotColumns);
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

        UpdateSlotPanelEditModePadding();
        UpdateWindowSize(rows, columns);
        ClampWindowWithinBounds();
        RefreshVisibleSlotMappings();
        UpdateKeyboardSelectionVisual();
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
        Height = metrics.BaseHeight + (rows - 2) * metrics.RowStep + GetEditModeReservedHeight();
    }

    private SlotSizeMetrics GetSlotSizeMetrics()
    {
        return _config?.SlotSize switch
        {
            SlotSize.Small => SmallSlotMetrics,
            SlotSize.Medium => MediumSlotMetrics,
            SlotSize.Custom when _config?.CustomSlotSize != null => BuildCustomSlotSizeMetrics(_config.CustomSlotSize),
            _ => LargeSlotMetrics
        };
    }

    private SlotSizeMetrics BuildCustomSlotSizeMetrics(CustomSlotSizeOptions options)
    {
        var slotHeight = options.SlotHeight;
        var titleFont = options.TitleFontSize;
        var statusFont = options.StatusFontSize;
        var overlayStatus = slotHeight < 40;
        var titleLines = slotHeight >= 60 ? 2 : 1;
        var wrapping = overlayStatus ? TextWrapping.NoWrap : TextWrapping.Wrap;
        var baseHeight = SmallSlotMetrics.BaseHeight - SmallSlotMetrics.SlotHeight + slotHeight;
        return new SlotSizeMetrics(
            BaseWidth: SmallSlotMetrics.BaseWidth,
            BaseHeight: baseHeight,
            ColumnStep: options.ColumnStep,
            RowStep: options.RowStep,
            SlotHeight: slotHeight,
            TitleFontSize: titleFont,
            StatusFontSize: statusFont,
            TitleWrapping: wrapping,
            TitleTrimming: TextTrimming.CharacterEllipsis,
            TitleVisibleLines: titleLines,
            OverlayStatus: overlayStatus,
            SlotMargin: options.SlotMargin);
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
            Margin = new Thickness(metrics.SlotMargin),
            BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x11, 0x11, 0x11)),
            Height = metrics.SlotHeight,
            AllowDrop = true,
            Tag = index
        };

        UIElement content;
        StackPanel? stackingPanel = null;
        Grid? overlayGrid = null;
        if (metrics.OverlayStatus)
        {
            overlayGrid = new Grid
            {
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center,
                Tag = index
            };
            content = overlayGrid;
        }
        else
        {
            stackingPanel = new StackPanel
            {
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center,
                Tag = index
            };
            content = stackingPanel;
        }

        var title = new TextBlock
        {
            Text = $"Slot {index + 1}",
            FontSize = metrics.TitleFontSize,
            TextWrapping = metrics.TitleWrapping,
            TextTrimming = metrics.TitleTrimming,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Tag = index
        };
        if (metrics.TitleVisibleLines > 0)
        {
            title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            title.LineHeight = metrics.TitleFontSize * 1.2;
            title.MaxHeight = title.LineHeight * metrics.TitleVisibleLines;
        }

        var status = new TextBlock
        {
            Text = "マクロ実行中...",
            FontSize = metrics.StatusFontSize,
            Foreground = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x7C, 0xFF, 0xB0)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Tag = index
        };

        if (metrics.OverlayStatus && overlayGrid != null)
        {
            overlayGrid.Children.Add(title);
            overlayGrid.Children.Add(status);
        }
        else if (stackingPanel != null)
        {
            stackingPanel.Children.Add(title);
            stackingPanel.Children.Add(status);
        }

        var container = new Grid
        {
            Tag = index
        };
        container.Children.Add(content);

        var previewBorder = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromArgb(0xBB, 0x10, 0x24, 0x10)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x66, 0xFF, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Visibility = Visibility.Collapsed,
            Tag = index
        };
        var previewText = new TextBlock
        {
            FontSize = Math.Max(metrics.StatusFontSize - 1, 9),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
            Tag = index
        };
        previewBorder.Child = previewText;
        container.Children.Add(previewBorder);

        border.Child = container;

        border.Drop += OnSlotDrop;
        border.MouseRightButtonUp += OnSlotContextMenu;
        border.MouseEnter += OnSlotMouseEnter;
        border.MouseLeave += OnSlotMouseLeave;
        border.DragEnter += OnSlotDragEnter;
        border.DragLeave += OnSlotDragLeave;
        border.DragOver += OnSlotDragOver;
        border.MouseLeftButtonDown += OnSlotMouseDown;
        border.MouseLeftButtonUp += OnSlotClick;
        border.MouseMove += OnSlotMouseMove;

        return new SlotVisual(border, title, status, metrics.OverlayStatus, previewBorder, previewText);
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
            _shortcutService.PrefixSearchRequested += OnPrefixSearchRequested;
            _shortcutService.PrefixStateChanged += OnPrefixStateChanged;
            _shortcutService.PrefixNextLayerRequested += OnPrefixNextLayerRequested;
            _shortcutService.PrefixPreviousLayerRequested += OnPrefixPreviousLayerRequested;
            _shortcutService.SearchHotkeyRequested += OnSearchHotkeyRequested;
            _shortcutService.MouseGestureShowRequested += OnMouseGestureShowRequested;
            _shortcutService.MouseGestureHideRequested += OnMouseGestureHideRequested;
            _shortcutService.Initialize(_config.ShortcutPrefix, _config.ShortcutPrefixDisabled);
            _shortcutService.SetPrefixLayerShortcutsEnabled(_config.EnablePrefixLayerShortcuts);
            _shortcutService.SetRemoteSessionPreference(_config.PreferRemoteSessions);
            _shortcutService.UpdateSearchHotkey(_config.SearchHotkey, _config.SearchHotkeyEnabled);
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
                _config.ShortcutPrefixDisabled = false;
                _config.ShortcutPrefix = _shortcutService.CurrentPrefixText;
                WpfMessageBox.Show("設定ファイルの Prefix が無効または設定不可のため、Ctrl+Q に戻しました。設定値を確認してください。", "Shortcut Prefix", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateShortcutRegistrations();
            ApplyMouseGestureOptions();
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
        _config.LastWindowVisibility = _isMinimizedToTray ? WindowVisibilityState.Tray : WindowVisibilityState.Visible;
        _configService.Save(_config);
        Close();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var modifiers = System.Windows.Input.Keyboard.Modifiers;

        if (TryMinimizeOnEscape(key, modifiers))
        {
            e.Handled = true;
            return;
        }

        if (HandleSearchKey(e) || HandleKeyboardNavigationKey(e) || HandleLayerSelectionKey(e))
        {
            e.Handled = true;
            return;
        }
        _metaPrefixPending = false;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_keyboardNavigationActive)
        {
            DeactivateKeyboardNavigation();
        }
        base.OnPreviewMouseDown(e);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_layerNameOverlayWindow?.IsVisible == true)
        {
            PositionLayerNameOverlay(_layerNameOverlayWindow);
        }
        if (_searchOverlayWindow?.IsVisible == true)
        {
            PositionSearchOverlay();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_searchOverlayWindow?.IsVisible == true)
        {
            PositionSearchOverlay();
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_keyboardNavigationActive)
        {
            DeactivateKeyboardNavigation();
        }
    }

    private void OnLayerButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button) return;
        if (button.Tag is string s)
        {
            if (s == "prev")
            {
                ChangeLayer(-1);
            }
            else if (s == "next")
            {
                ChangeLayer(1);
            }
            return;
        }

        if (button.Tag is int target)
        {
            SetLayer(target);
        }
    }

    private void SetLayer(int index)
    {
        var totalLayers = _config?.Layers?.Count ?? MinLayers;
        if (totalLayers <= 0)
        {
            return;
        }

        if (_searchLayerActive)
        {
            CloseSearchLayer();
        }

        var target = Math.Clamp(index, 0, totalLayers - 1);
        var delta = target - _currentLayer;
        if (delta == 0)
        {
            ShowLayerNameOverlay();
            return;
        }

        _lastLayerNavigationDirection = Math.Sign(delta);
        _currentLayer = target;
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
        RefreshUi();

        ShowLayerNameOverlay();
    }

    private void ChangeLayer(int delta)
    {
        var totalLayers = _config?.Layers?.Count ?? MinLayers;
        if (totalLayers <= 0)
        {
            return;
        }

        var next = (_currentLayer + delta) % totalLayers;
        if (next < 0)
        {
            next += totalLayers;
        }

        SetLayer(next);
    }

    private string? GetLayerDisplayName(int layerIndex)
    {
        var fallback = $"Layer {layerIndex + 1}";
        if (_config?.Layers == null || layerIndex < 0 || layerIndex >= _config.Layers.Count)
        {
            return fallback;
        }

        var name = _config.Layers[layerIndex].Name;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private Rect GetWindowRect()
    {
        double width = Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = ActualWidth;
        }
        if (width <= 0)
        {
            width = Math.Max(MinWidth, 1);
        }

        double height = Height;
        if (double.IsNaN(height) || height <= 0)
        {
            height = ActualHeight;
        }
        if (height <= 0)
        {
            height = Math.Max(MinHeight, 1);
        }

        return new Rect(Left, Top, width, height);
    }

    private (double left, double top, bool placedBelow) CalculateOverlayPosition(Window overlay)
    {
        overlay.InvalidateMeasure();
        overlay.Measure(new System.Windows.Size(overlay.MaxWidth, double.PositiveInfinity));
        overlay.Width = overlay.DesiredSize.Width;
        overlay.Height = overlay.DesiredSize.Height;
        overlay.UpdateLayout();

        var anchorRect = GetWindowRect();
        var bounds = ScreenBoundsResolver.ForRect(this, anchorRect);
        const double offset = 10;

        double targetLeft = anchorRect.Left + (anchorRect.Width - overlay.Width) / 2;
        double targetTop = anchorRect.Top - overlay.Height - offset;
        bool placedBelow = false;
        if (targetTop < bounds.Top)
        {
            targetTop = anchorRect.Bottom + offset;
            placedBelow = true;
        }

        var (clampedLeft, clampedTop) = _placement.Clamp(targetLeft, targetTop, bounds, overlay.Width, overlay.Height);
        return (clampedLeft, clampedTop, placedBelow);
    }

    private void PositionOverlayWindow(Window overlay)
    {
        var (left, top, _) = CalculateOverlayPosition(overlay);
        overlay.Left = left;
        overlay.Top = top;
    }

    private LayerNameOverlayWindow EnsureLayerNameOverlay()
    {
        if (_layerNameOverlayWindow == null)
        {
            _layerNameOverlayWindow = new LayerNameOverlayWindow
            {
                Owner = this,
                Topmost = this.Topmost
            };
        }
        return _layerNameOverlayWindow;
    }

    private void HideLayerNameOverlayImmediate()
    {
        _layerNameOverlayCts?.Cancel();
        _layerNameOverlayWindow?.Hide();
        _layerNameOverlayWindow?.BeginAnimation(UIElement.OpacityProperty, null);
        if (_layerNameOverlayWindow != null)
        {
            _layerNameOverlayWindow.Opacity = 1;
        }
    }

    private void PositionLayerNameOverlay(LayerNameOverlayWindow overlay)
    {
        PositionOverlayWindow(overlay);
    }

    private void UpdateOverlayTopmost()
    {
        if (_layerNameOverlayWindow != null)
        {
            _layerNameOverlayWindow.Topmost = this.Topmost;
        }
        if (_searchOverlayWindow != null)
        {
            _searchOverlayWindow.Topmost = this.Topmost;
        }
    }

    private async Task FadeLayerNameOverlayAsync(LayerNameOverlayWindow overlay, double targetOpacity, TimeSpan duration, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<bool>();
        var animation = new DoubleAnimation(targetOpacity, duration) { FillBehavior = FillBehavior.Stop };
        void OnCompleted(object? _, EventArgs __)
        {
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            overlay.Opacity = targetOpacity;
            tcs.TrySetResult(true);
        }

        animation.Completed += OnCompleted;
        overlay.BeginAnimation(UIElement.OpacityProperty, animation);

        using (token.Register(() =>
               {
                   overlay.Dispatcher.Invoke(() =>
                   {
                       overlay.BeginAnimation(UIElement.OpacityProperty, null);
                       tcs.TrySetCanceled(token);
                   });
               }))
        {
            await tcs.Task.ConfigureAwait(true);
        }
    }

    private async void ShowLayerNameOverlay()
    {
        if (!IsLoaded || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        var layerName = GetLayerDisplayName(_currentLayer);
        if (string.IsNullOrWhiteSpace(layerName))
        {
            HideLayerNameOverlayImmediate();
            return;
        }

        var previousCts = _layerNameOverlayCts;
        var cts = new CancellationTokenSource();
        _layerNameOverlayCts = cts;
        previousCts?.Cancel();

        var overlay = EnsureLayerNameOverlay();
        overlay.Topmost = this.Topmost;
        overlay.SetLayerName(layerName);
        PositionLayerNameOverlay(overlay);

        overlay.Opacity = 1;
        overlay.Show();
        overlay.UpdateLayout();
        _ = Dispatcher.BeginInvoke(new Action(() => PositionLayerNameOverlay(overlay)), DispatcherPriority.Background);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5), cts.Token).ConfigureAwait(true);
            await FadeLayerNameOverlayAsync(overlay, 0, TimeSpan.FromSeconds(0.5), cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
        }
        finally
        {
            if (ReferenceEquals(_layerNameOverlayCts, cts))
            {
                overlay.Hide();
                overlay.Opacity = 1;
            }
        }
    }

    private SearchOverlayWindow EnsureSearchOverlay()
    {
        if (_searchOverlayWindow == null)
        {
            _searchOverlayWindow = new SearchOverlayWindow
            {
                Owner = this,
                Topmost = this.Topmost
            };
            _searchOverlayWindow.SearchTextChanged += OnSearchOverlayTextChanged;
            _searchOverlayWindow.CancelRequested += OnSearchOverlayCancelRequested;
            _searchOverlayWindow.SlotNavigationRequested += OnSearchOverlaySlotNavigationRequested;
        }
        UpdateSearchOverlayNavigationModes();
        return _searchOverlayWindow;
    }

    private void UpdateSearchOverlayNavigationModes()
    {
        if (_searchOverlayWindow == null)
        {
            return;
        }

        _searchOverlayWindow.EnableEmacsNavigation = _config?.EnableEmacsNavigation ?? false;
    }

    private void PositionSearchOverlay()
    {
        if (_searchOverlayWindow == null)
        {
            return;
        }
        var (left, top, placedBelow) = CalculateOverlayPosition(_searchOverlayWindow);
        _searchOverlayWindow.Left = left;
        _searchOverlayWindow.Top = top;
        _isSearchOverlayBelowMain = placedBelow;
        _searchOverlayWindow.NavigationDirectionToSlots = placedBelow ? NavigationDirection.Up : NavigationDirection.Down;
    }

    private void HideSearchOverlay()
    {
        _searchOverlayWindow?.Hide();
    }

    private void OpenSearchLayer()
    {
        if (_config?.Layers == null || _config.Layers.Count == 0)
        {
            _suppressFixedCaptureDuringSearch = false;
            return;
        }

        _searchLayerActive = true;
        SetSlotLayoutEditMode(false);
        if (_applySearchPlacementForCurrentSearch)
        {
            PositionWindowForSearchOverlayAnchor();
        }
        var overlay = EnsureSearchOverlay();
        overlay.Topmost = this.Topmost;
        overlay.Query = _searchQuery;
        overlay.Show();
        overlay.UpdateLayout();
        PositionSearchOverlay();
        _ = Dispatcher.BeginInvoke(new Action(PositionSearchOverlay), DispatcherPriority.Background);
        overlay.FocusInput(selectAll: string.IsNullOrEmpty(_searchQuery));
        RebuildSearchResults();
        RefreshUi();
    }

    private void CloseSearchLayer()
    {
        if (!_searchLayerActive && _searchOverlayWindow?.IsVisible != true)
        {
            return;
        }

        _searchLayerActive = false;
        _suppressFixedCaptureDuringSearch = false;
        _applySearchPlacementForCurrentSearch = false;
        _searchQuery = string.Empty;
        _searchResults.Clear();
        HideSearchOverlay();
        _prefixSearchRestoreContext = null;
        RefreshUi();
    }

    private void CloseSearchLayerAndMaybeRestore()
    {
        var restoreContext = _prefixSearchRestoreContext;
        CloseSearchLayer();
        if (restoreContext != null)
        {
            RestoreWindowAfterPrefixSearch(restoreContext);
        }
    }

    private void CapturePrefixSearchRestoreContext()
    {
        _prefixSearchRestoreContext = new PrefixSearchRestoreContext(
            IsWindowHiddenForShow(),
            Left,
            Top,
            _suppressFixedCapture);
    }

    private void RestoreWindowAfterPrefixSearch(PrefixSearchRestoreContext restoreContext)
    {
        if (restoreContext.WasMinimized)
        {
            MinimizeWindowToTray();
            return;
        }

        if (!double.IsNaN(restoreContext.Left) && !double.IsNaN(restoreContext.Top))
        {
            Left = restoreContext.Left;
            Top = restoreContext.Top;
        }

        _suppressFixedCapture = restoreContext.PreviousSuppressFixedCapture;
    }

    private void OnSearchOverlayTextChanged(object? sender, string text)
    {
        _searchQuery = text ?? string.Empty;
        RebuildSearchResults();
        RefreshUi();
    }

    private void OnSearchOverlayCancelRequested(object? sender, EventArgs e)
    {
        CloseSearchLayerAndMaybeRestore();
    }

    private void OnSearchOverlaySlotNavigationRequested(object? sender, SlotNavigationRequestedEventArgs e)
    {
        if (!_searchLayerActive || !CanNavigateFromSearchToSlots())
        {
            return;
        }

        BringWindowToForeground();
        ActivateKeyboardNavigation();
        int totalSlots = GetNavigableSlotCount();
        if (totalSlots <= 0)
        {
            _keyboardSelectedSlotIndex = -1;
        }
        else if (e?.PreferLastSlot == true)
        {
            _keyboardSelectedSlotIndex = totalSlots - 1;
        }
        else
        {
            _keyboardSelectedSlotIndex = 0;
        }
        NormalizeKeyboardSelectionIndex();
        UpdateKeyboardSelectionVisual();
        Focus();
        if (SlotsPanel != null)
        {
            Keyboard.Focus(SlotsPanel);
        }
        else
        {
            Keyboard.Focus(this);
        }
        Focus();
        Keyboard.Focus(this);
    }

    private void RebuildSearchResults()
    {
        _searchResults.Clear();
        if (!_searchLayerActive)
        {
            return;
        }

        var query = (_searchQuery ?? string.Empty).Trim();
        if (_config?.Layers == null)
        {
            return;
        }

        var tokens = string.IsNullOrEmpty(query)
            ? Array.Empty<string>()
            : query
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeTokenForSearch)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();
        bool matchAll = tokens.Length == 0;

        for (int layerIndex = 0; layerIndex < _config.Layers.Count; layerIndex++)
        {
            var layer = _config.Layers[layerIndex];
            if (layer?.Slots == null) continue;
            for (int slotIndex = 0; slotIndex < layer.Slots.Count; slotIndex++)
            {
                var slot = layer.Slots[slotIndex];
                if (slot == null || IsSlotEmpty(slot))
                {
                    continue;
                }

                if (matchAll)
                {
                    _searchResults.Add(new SearchResult(layerIndex, slotIndex));
                    continue;
                }

                var haystack = BuildSlotSearchText(slot);
                if (MatchesAllTokens(haystack, tokens))
                {
                    _searchResults.Add(new SearchResult(layerIndex, slotIndex));
                }
            }
        }
    }

    private static string BuildSlotSearchText(SlotModel slot)
    {
        var title = slot.Title ?? string.Empty;
        var keywords = slot.SearchKeywords ?? string.Empty;
        var baseText = (title + " " + keywords).ReplaceLineEndings(" ");
        var normalized = NormalizeForSearch(baseText);
        var romaji = ConvertKanaToRomaji(baseText);
        return string.Join(" ", baseText, normalized, romaji);
    }

    private static string NormalizeTokenForSearch(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var cleaned = token
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("ー", string.Empty, StringComparison.Ordinal);
        return NormalizeForSearch(cleaned);
    }

    private static bool MatchesAllTokens(string haystack, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (!IsFuzzyMatch(haystack, token))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsFuzzyMatch(string haystack, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        if (haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return IsSubsequence(haystack, token);
    }

    private static bool IsSubsequence(string haystack, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        int hIndex = 0;
        var source = haystack.ToLowerInvariant();
        var needle = token.ToLowerInvariant();
        foreach (char ch in needle)
        {
            hIndex = source.IndexOf(ch, hIndex);
            if (hIndex < 0)
            {
                return false;
            }
            hIndex++;
        }
        return true;
    }

    private static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string ConvertKanaToRomaji(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var hira = ToHiragana(text.Normalize(NormalizationForm.FormKC));
        var sb = new StringBuilder(hira.Length * 3);
        bool sokuonPending = false;

        for (int i = 0; i < hira.Length; i++)
        {
            char ch = hira[i];
            if (ch == 'っ')
            {
                sokuonPending = true;
                continue;
            }

            if (ch == 'ー')
            {
                if (sb.Length > 0)
                {
                    char last = sb[sb.Length - 1];
                    if ("aeiou".Contains(last))
                    {
                        sb.Append(last);
                    }
                }
                continue;
            }

            string? roma = TryGetDigraph(hira, i, out int consumed)
                ?? TryGetSingleKanaRomaji(ch);

            if (consumed > 0)
            {
                i += consumed;
            }

            if (string.IsNullOrEmpty(roma))
            {
                sokuonPending = false;
                continue;
            }

            if (sokuonPending)
            {
                var first = roma[0];
                if (char.IsLetter(first) && !"aeiou".Contains(char.ToLowerInvariant(first)))
                {
                    sb.Append(first);
                }
                sokuonPending = false;
            }

            sb.Append(roma);
        }

        return sb.ToString();
    }

    private static string ToHiragana(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (ch >= '\u30A1' && ch <= '\u30F4')
            {
                sb.Append((char)(ch - 0x60));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static string? TryGetDigraph(string hira, int index, out int consumed)
    {
        consumed = 0;
        if (index + 1 >= hira.Length)
        {
            return null;
        }

        char first = hira[index];
        char second = hira[index + 1];
        string key = new string(new[] { first, second });
        if (DigraphRomaji.TryGetValue(key, out var roma))
        {
            consumed = 1;
            return roma;
        }
        return null;
    }

    private static string? TryGetSingleKanaRomaji(char ch)
    {
        if (SingleKanaRomaji.TryGetValue(ch, out var roma))
        {
            return roma;
        }
        return null;
    }

    private void RefreshVisibleSlotMappings()
    {
        _visibleSlotMappings.Clear();
        if (_slotVisuals.Count == 0)
        {
            return;
        }

        if (_searchLayerActive)
        {
            int max = Math.Min(_slotVisuals.Count, _searchResults.Count);
            for (int i = 0; i < max; i++)
            {
                var result = _searchResults[i];
                _visibleSlotMappings.Add(new VisibleSlotMapping(result.LayerIndex, result.SlotIndex));
            }
            while (_visibleSlotMappings.Count < _slotVisuals.Count)
            {
                _visibleSlotMappings.Add(VisibleSlotMapping.Empty);
            }
        }
        else
        {
            for (int i = 0; i < _slotVisuals.Count; i++)
            {
                _visibleSlotMappings.Add(new VisibleSlotMapping(_currentLayer, i));
            }
        }
    }

    private bool TryGetVisibleSlot(int displayIndex, out int layerIndex, out int slotIndex)
    {
        layerIndex = -1;
        slotIndex = -1;
        if (displayIndex < 0 || displayIndex >= _visibleSlotMappings.Count)
        {
            return false;
        }

        var map = _visibleSlotMappings[displayIndex];
        if (map.IsEmpty)
        {
            return false;
        }

        layerIndex = map.LayerIndex;
        slotIndex = map.SlotIndex;
        return true;
    }

    private bool TryGetVisibleSlotModel(int displayIndex, out int layerIndex, out int slotIndex, out SlotModel slot)
    {
        slot = null!;
        if (!TryGetVisibleSlot(displayIndex, out layerIndex, out slotIndex))
        {
            return false;
        }

        if (_config?.Layers == null || layerIndex < 0 || layerIndex >= _config.Layers.Count)
        {
            return false;
        }

        var layer = _config.Layers[layerIndex];
        if (layer.Slots == null || slotIndex < 0 || slotIndex >= layer.Slots.Count)
        {
            return false;
        }

        slot = layer.Slots[slotIndex];
        return slot != null;
    }

    private bool TryFindDisplayIndex(int layerIndex, int slotIndex, out int displayIndex)
    {
        for (int i = 0; i < _visibleSlotMappings.Count; i++)
        {
            var map = _visibleSlotMappings[i];
            if (!map.IsEmpty && map.LayerIndex == layerIndex && map.SlotIndex == slotIndex)
            {
                displayIndex = i;
                return true;
            }
        }

        displayIndex = -1;
        return false;
    }

    private void FocusSearchOverlayInput(bool selectAll)
    {
        var overlay = EnsureSearchOverlay();
        overlay.FocusInput(selectAll);
    }

    private static readonly Dictionary<string, string> DigraphRomaji = new(StringComparer.Ordinal)
    {
        ["きゃ"] = "kya",
        ["きゅ"] = "kyu",
        ["きょ"] = "kyo",
        ["ぎゃ"] = "gya",
        ["ぎゅ"] = "gyu",
        ["ぎょ"] = "gyo",
        ["しゃ"] = "sha",
        ["しゅ"] = "shu",
        ["しょ"] = "sho",
        ["じゃ"] = "ja",
        ["じゅ"] = "ju",
        ["じょ"] = "jo",
        ["ちゃ"] = "cha",
        ["ちゅ"] = "chu",
        ["ちょ"] = "cho",
        ["にゃ"] = "nya",
        ["にゅ"] = "nyu",
        ["にょ"] = "nyo",
        ["ひゃ"] = "hya",
        ["ひゅ"] = "hyu",
        ["ひょ"] = "hyo",
        ["びゃ"] = "bya",
        ["びゅ"] = "byu",
        ["びょ"] = "byo",
        ["ぴゃ"] = "pya",
        ["ぴゅ"] = "pyu",
        ["ぴょ"] = "pyo",
        ["みゃ"] = "mya",
        ["みゅ"] = "myu",
        ["みょ"] = "myo",
        ["りゃ"] = "rya",
        ["りゅ"] = "ryu",
        ["りょ"] = "ryo"
    };

    private static readonly Dictionary<char, string> SingleKanaRomaji = new()
    {
        ['あ'] = "a", ['い'] = "i", ['う'] = "u", ['え'] = "e", ['お'] = "o",
        ['ぁ'] = "a", ['ぃ'] = "i", ['ぅ'] = "u", ['ぇ'] = "e", ['ぉ'] = "o",
        ['か'] = "ka", ['き'] = "ki", ['く'] = "ku", ['け'] = "ke", ['こ'] = "ko",
        ['さ'] = "sa", ['し'] = "shi", ['す'] = "su", ['せ'] = "se", ['そ'] = "so",
        ['た'] = "ta", ['ち'] = "chi", ['つ'] = "tsu", ['て'] = "te", ['と'] = "to",
        ['な'] = "na", ['に'] = "ni", ['ぬ'] = "nu", ['ね'] = "ne", ['の'] = "no",
        ['は'] = "ha", ['ひ'] = "hi", ['ふ'] = "fu", ['へ'] = "he", ['ほ'] = "ho",
        ['ま'] = "ma", ['み'] = "mi", ['む'] = "mu", ['め'] = "me", ['も'] = "mo",
        ['や'] = "ya", ['ゆ'] = "yu", ['よ'] = "yo",
        ['ら'] = "ra", ['り'] = "ri", ['る'] = "ru", ['れ'] = "re", ['ろ'] = "ro",
        ['わ'] = "wa", ['を'] = "o", ['ん'] = "n",
        ['が'] = "ga", ['ぎ'] = "gi", ['ぐ'] = "gu", ['げ'] = "ge", ['ご'] = "go",
        ['ざ'] = "za", ['じ'] = "ji", ['ず'] = "zu", ['ぜ'] = "ze", ['ぞ'] = "zo",
        ['だ'] = "da", ['ぢ'] = "ji", ['づ'] = "zu", ['で'] = "de", ['ど'] = "do",
        ['ば'] = "ba", ['び'] = "bi", ['ぶ'] = "bu", ['べ'] = "be", ['ぼ'] = "bo",
        ['ぱ'] = "pa", ['ぴ'] = "pi", ['ぷ'] = "pu", ['ぺ'] = "pe", ['ぽ'] = "po",
        ['ゔ'] = "vu",
        ['ー'] = string.Empty
    };


    private void ActivateKeyboardNavigation()
    {
        _keyboardNavigationActive = true;
        _keyboardSelectedSlotIndex = -1;
        UpdateKeyboardSelectionVisual();
    }

    private void DeactivateKeyboardNavigation()
    {
        _keyboardNavigationActive = false;
        _keyboardSelectedSlotIndex = -1;
        UpdateKeyboardSelectionVisual();
    }

    // Avoid letting held navigation modifiers (e.g., Ctrl+M) leak into macro execution.
    private void ReleaseKeyboardNavigationModifiers()
    {
        Span<ushort> modifierKeys = stackalloc ushort[]
        {
            NativeMethods.VK_LCONTROL,
            NativeMethods.VK_RCONTROL,
            NativeMethods.VK_LMENU,
            NativeMethods.VK_RMENU,
            NativeMethods.VK_LSHIFT,
            NativeMethods.VK_RSHIFT,
            NativeMethods.VK_LWIN,
            NativeMethods.VK_RWIN
        };

        foreach (var vk in modifierKeys)
        {
            if (NativeMethods.IsKeyPressed(vk))
            {
                NativeMethods.SendKeyUp(vk);
            }
        }
    }

    private int GetNavigableSlotCount()
    {
        if (_searchLayerActive)
        {
            return Math.Min(_slotVisuals.Count, _searchResults.Count);
        }
        return _slotVisuals.Count;
    }

    private void NormalizeKeyboardSelectionIndex()
    {
        int totalSlots = GetNavigableSlotCount();
        if (totalSlots <= 0)
        {
            _keyboardSelectedSlotIndex = -1;
            return;
        }

        if (_keyboardSelectedSlotIndex >= totalSlots)
        {
            _keyboardSelectedSlotIndex = totalSlots - 1;
        }
        if (_keyboardSelectedSlotIndex < 0)
        {
            _keyboardSelectedSlotIndex = 0;
        }
    }

    private void MoveKeyboardSelection(int deltaRow, int deltaColumn)
    {
        if (!_keyboardNavigationActive)
        {
            return;
        }

        int totalSlots = GetNavigableSlotCount();
        if (totalSlots <= 0)
        {
            _keyboardSelectedSlotIndex = -1;
            UpdateKeyboardSelectionVisual();
            return;
        }

        int rows = _config.SlotRows;
        int columns = _config.SlotColumns;
        if (rows <= 0 || columns <= 0)
        {
            return;
        }

        int row;
        int column;
        if (_keyboardSelectedSlotIndex < 0)
        {
            row = 0;
            column = 0;
        }
        else
        {
            row = _keyboardSelectedSlotIndex / columns;
            column = _keyboardSelectedSlotIndex % columns;
        }

        row = Math.Clamp(row + deltaRow, 0, rows - 1);
        column = Math.Clamp(column + deltaColumn, 0, columns - 1);
        _keyboardSelectedSlotIndex = Math.Min(row * columns + column, totalSlots - 1);
        UpdateKeyboardSelectionVisual();
    }

    private bool HandleSearchKey(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        bool emacsEnabled = _config?.EnableEmacsNavigation ?? false;
        bool viEnabled = _config?.EnableViNavigation ?? false;

        if (_config?.Layers == null || _config.Layers.Count == 0)
        {
            return false;
        }

        if (_searchLayerActive && modifiers == System.Windows.Input.ModifierKeys.None && key == System.Windows.Input.Key.Escape)
        {
            CloseSearchLayerAndMaybeRestore();
            return true;
        }

        if (_searchLayerActive && emacsEnabled && modifiers == System.Windows.Input.ModifierKeys.Control && key == System.Windows.Input.Key.G)
        {
            CloseSearchLayerAndMaybeRestore();
            return true;
        }

        if (viEnabled &&
            modifiers == System.Windows.Input.ModifierKeys.None &&
            (key == System.Windows.Input.Key.Oem2 || key == System.Windows.Input.Key.OemQuestion || key == System.Windows.Input.Key.Divide))
        {
            _applySearchPlacementForCurrentSearch = false;
            OpenSearchLayer();
            return true;
        }

        if (emacsEnabled && modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            if (key == System.Windows.Input.Key.S)
            {
                _applySearchPlacementForCurrentSearch = false;
                OpenSearchLayer();
                return true;
            }

            if (key == System.Windows.Input.Key.F)
            {
                return false;
            }
        }

        if (modifiers == System.Windows.Input.ModifierKeys.Control && key == System.Windows.Input.Key.F)
        {
            _applySearchPlacementForCurrentSearch = false;
            OpenSearchLayer();
            return true;
        }

        return false;
    }

    private bool HandleKeyboardNavigationKey(System.Windows.Input.KeyEventArgs e)
    {
        if (!IsActive)
        {
            return false;
        }

        if (TryHandleMenuNavigationKey(e))
        {
            return true;
        }

        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        NavigationCommand command = NavigationCommand.MoveUp;
        if (_searchLayerActive && key == System.Windows.Input.Key.Tab &&
            (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None ||
             System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift))
        {
            if (_keyboardNavigationActive)
            {
                FocusSearchOverlayInput(selectAll: false);
                DeactivateKeyboardNavigation();
            }
            else
            {
                ActivateKeyboardNavigation();
                _keyboardSelectedSlotIndex = 0;
                NormalizeKeyboardSelectionIndex();
                UpdateKeyboardSelectionVisual();
            }
            return true;
        }
        if (_metaPrefixPending && DateTime.UtcNow <= _metaPrefixExpiryUtc &&
            System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            _metaPrefixPending = false;
            if (key == System.Windows.Input.Key.X || key == System.Windows.Input.Key.Back)
            {
                command = NavigationCommand.OpenMenu;
                OpenContextMenuFromKeyboard();
                return true;
            }
            return true;
        }
        else
        {
            _metaPrefixPending = false;
        }

        if (!TryMapNavigationCommand(e, key, out command))
        {
            return false;
        }

        if (command == NavigationCommand.MetaPrefix)
        {
            return true;
        }

        if (command == NavigationCommand.OpenMenu)
        {
            OpenContextMenuFromKeyboard();
            return true;
        }

        if (_searchLayerActive && _keyboardNavigationActive &&
            (command == NavigationCommand.MoveUp || command == NavigationCommand.MoveDown))
        {
            int columns = _config?.SlotColumns ?? 0;
            int totalSlots = GetNavigableSlotCount();
            if (columns > 0 && totalSlots > 0 && _keyboardSelectedSlotIndex >= 0)
            {
                bool atOverlayEdge = !_isSearchOverlayBelowMain
                    ? command == NavigationCommand.MoveUp && _keyboardSelectedSlotIndex < columns
                    : command == NavigationCommand.MoveDown && _keyboardSelectedSlotIndex >= Math.Max(totalSlots - columns, 0);
                if (atOverlayEdge && ShouldReturnToSearch(key, modifiers))
                {
                    FocusSearchOverlayInput(selectAll: false);
                    DeactivateKeyboardNavigation();
                    _searchOverlayWindow?.SuppressNavigationUntilKeyUp(key, modifiers);
                    return true;
                }
            }
        }

        if (!_keyboardNavigationActive)
        {
            ActivateKeyboardNavigation();
        }

        switch (command)
        {
            case NavigationCommand.MoveUp:
                MoveKeyboardSelection(-1, 0);
                return true;
            case NavigationCommand.MoveDown:
                MoveKeyboardSelection(1, 0);
                return true;
            case NavigationCommand.MoveLeft:
                MoveKeyboardSelection(0, -1);
                return true;
            case NavigationCommand.MoveRight:
                MoveKeyboardSelection(0, 1);
                return true;
            case NavigationCommand.RowStart:
                MoveKeyboardSelectionToBoundary(start: true);
                return true;
            case NavigationCommand.RowEnd:
                MoveKeyboardSelectionToBoundary(start: false);
                return true;
            case NavigationCommand.Confirm:
                if (_keyboardSelectedSlotIndex >= 0)
                {
                    ReleaseKeyboardNavigationModifiers();
                    int index = _keyboardSelectedSlotIndex;
                    DeactivateKeyboardNavigation();
                    _ = TriggerVisibleSlotAsync(index, SlotTriggerKind.Keyboard);
                }
                return true;
            case NavigationCommand.Cancel:
                DeactivateKeyboardNavigation();
                return true;
            default:
                return false;
        }
    }

    private enum NavigationCommand
    {
        MoveUp,
        MoveDown,
        MoveLeft,
        MoveRight,
        RowStart,
        RowEnd,
        Confirm,
        Cancel,
        OpenMenu,
        MetaPrefix
    }

    private bool TryMapNavigationCommand(System.Windows.Input.KeyEventArgs e, System.Windows.Input.Key key, out NavigationCommand command)
    {
        bool emacsEnabled = _config?.EnableEmacsNavigation ?? false;
        bool viEnabled = _config?.EnableViNavigation ?? false;
        command = NavigationCommand.MoveUp;
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        bool ctrl = (modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        bool alt = (modifiers & System.Windows.Input.ModifierKeys.Alt) != 0;
        bool shift = (modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
        bool hasOther = (modifiers & ~(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift | System.Windows.Input.ModifierKeys.Alt)) != 0;
        if (hasOther)
        {
            return false;
        }

        if (emacsEnabled && alt && !ctrl && key == System.Windows.Input.Key.X)
        {
            command = NavigationCommand.OpenMenu;
            return true;
        }

        if (viEnabled && !ctrl && !alt && (key == System.Windows.Input.Key.OemSemicolon || key == System.Windows.Input.Key.Oem1) && shift)
        {
            command = NavigationCommand.OpenMenu;
            return true;
        }

        if (emacsEnabled && ctrl && !alt)
        {
            switch (key)
            {
                case System.Windows.Input.Key.OemOpenBrackets:
                case System.Windows.Input.Key.Escape:
                    command = NavigationCommand.MetaPrefix;
                    ArmMetaPrefix();
                    return true;
                case System.Windows.Input.Key.F:
                    command = NavigationCommand.MoveRight;
                    return true;
                case System.Windows.Input.Key.B:
                    command = NavigationCommand.MoveLeft;
                    return true;
                case System.Windows.Input.Key.N:
                    command = NavigationCommand.MoveDown;
                    return true;
                case System.Windows.Input.Key.P:
                    command = NavigationCommand.MoveUp;
                    return true;
                case System.Windows.Input.Key.A:
                    command = NavigationCommand.RowStart;
                    return true;
                case System.Windows.Input.Key.E:
                    command = NavigationCommand.RowEnd;
                    return true;
                case System.Windows.Input.Key.M:
                case System.Windows.Input.Key.J:
                    command = NavigationCommand.Confirm;
                    return true;
            }
        }

        if (!ctrl && !alt)
        {
            switch (key)
            {
                case System.Windows.Input.Key.Up:
                    command = NavigationCommand.MoveUp;
                    return true;
                case System.Windows.Input.Key.Down:
                    command = NavigationCommand.MoveDown;
                    return true;
                case System.Windows.Input.Key.Left:
                    command = NavigationCommand.MoveLeft;
                    return true;
                case System.Windows.Input.Key.Right:
                    command = NavigationCommand.MoveRight;
                    return true;
            }
        }

        if (!ctrl && !alt && viEnabled)
        {
            switch (key)
            {
                case System.Windows.Input.Key.H:
                    command = NavigationCommand.MoveLeft;
                    return true;
                case System.Windows.Input.Key.J:
                    command = NavigationCommand.MoveDown;
                    return true;
                case System.Windows.Input.Key.K:
                    command = NavigationCommand.MoveUp;
                    return true;
                case System.Windows.Input.Key.L:
                    command = NavigationCommand.MoveRight;
                    return true;
            }
        }

        if (!ctrl && !alt && (viEnabled || emacsEnabled))
        {
            switch (key)
            {
                case System.Windows.Input.Key.Enter:
                    command = NavigationCommand.Confirm;
                    return true;
                case System.Windows.Input.Key.Escape:
                    command = NavigationCommand.Cancel;
                    return true;
            }
        }

        return false;
    }

    private void MoveKeyboardSelectionToBoundary(bool start)
    {
        if (!_keyboardNavigationActive)
        {
            return;
        }

        int totalSlots = GetNavigableSlotCount();
        if (totalSlots <= 0)
        {
            return;
        }

        int rows = _config.SlotRows;
        int columns = _config.SlotColumns;
        if (rows <= 0 || columns <= 0)
        {
            return;
        }

        if (_keyboardSelectedSlotIndex < 0)
        {
            _keyboardSelectedSlotIndex = Math.Min(0, totalSlots - 1);
            UpdateKeyboardSelectionVisual();
            return;
        }

        int row = _keyboardSelectedSlotIndex / columns;
        int column = start ? 0 : columns - 1;
        _keyboardSelectedSlotIndex = Math.Min(row * columns + column, totalSlots - 1);
        UpdateKeyboardSelectionVisual();
    }

    private bool HandleLayerSelectionKey(System.Windows.Input.KeyEventArgs e)
    {
        if (!IsActive || _suppressLayerSelectionForPrefix)
        {
            return false;
        }

        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        if (e.Key == System.Windows.Input.Key.Tab &&
            (modifiers & System.Windows.Input.ModifierKeys.Control) != 0 &&
            (modifiers & ~(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)) == 0)
        {
            int delta = (modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 ? -1 : 1;
            ChangeLayer(delta);
            return true;
        }

        if (modifiers != System.Windows.Input.ModifierKeys.None)
        {
            return false;
        }

        if (!TryGetLayerIndexFromKey(e.Key, out var targetLayer))
        {
            return false;
        }

        var totalLayers = _config?.Layers?.Count ?? 0;
        if (totalLayers == 0)
        {
            return false;
        }

        if (targetLayer < 0 || targetLayer >= totalLayers)
        {
            return false;
        }
        if (targetLayer == _currentLayer)
        {
            return true;
        }

        SetLayer(targetLayer);
        return true;
    }

    private static bool TryGetLayerIndexFromKey(System.Windows.Input.Key key, out int layerIndex)
    {
        switch (key)
        {
            case System.Windows.Input.Key.D1:
            case System.Windows.Input.Key.NumPad1:
                layerIndex = 0;
                return true;
            case System.Windows.Input.Key.D2:
            case System.Windows.Input.Key.NumPad2:
                layerIndex = 1;
                return true;
            case System.Windows.Input.Key.D3:
            case System.Windows.Input.Key.NumPad3:
                layerIndex = 2;
                return true;
            case System.Windows.Input.Key.D4:
            case System.Windows.Input.Key.NumPad4:
                layerIndex = 3;
                return true;
            case System.Windows.Input.Key.D5:
            case System.Windows.Input.Key.NumPad5:
                layerIndex = 4;
                return true;
            case System.Windows.Input.Key.D6:
            case System.Windows.Input.Key.NumPad6:
                layerIndex = 5;
                return true;
            case System.Windows.Input.Key.D7:
            case System.Windows.Input.Key.NumPad7:
                layerIndex = 6;
                return true;
            case System.Windows.Input.Key.D8:
            case System.Windows.Input.Key.NumPad8:
                layerIndex = 7;
                return true;
            default:
                layerIndex = -1;
                return false;
        }
    }

    private bool TryHandleMenuNavigationKey(System.Windows.Input.KeyEventArgs e)
    {
        var menu = this.ContextMenu;
        if (menu?.IsOpen != true)
        {
            return false;
        }

        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (!TryMapMenuCommand(key, System.Windows.Input.Keyboard.Modifiers, out var command))
        {
            return false;
        }

        switch (command)
        {
            case NavigationCommand.MoveDown:
                return MoveMenuFocus(menu, FocusNavigationDirection.Next);
            case NavigationCommand.MoveUp:
                return MoveMenuFocus(menu, FocusNavigationDirection.Previous);
            case NavigationCommand.MoveRight:
                return TryEnterSubmenu(menu);
            case NavigationCommand.MoveLeft:
                return TryLeaveSubmenu(menu);
            case NavigationCommand.Confirm:
                return TryActivateMenuSelection(menu);
            case NavigationCommand.Cancel:
                menu.IsOpen = false;
                return true;
            default:
                return false;
        }
    }

    private void OnContextMenuPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (TryHandleMenuNavigationKey(e))
        {
            e.Handled = true;
        }
        else
        {
            _metaPrefixPending = false;
        }
    }

    private void OnContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        _metaPrefixPending = false;
        if (sender is ContextMenu menu)
        {
            menu.PreviewKeyDown -= OnContextMenuPreviewKeyDown;
            menu.Closed -= OnContextMenuClosed;
        }
    }

    private static bool IsElementWithinContextMenu(DependencyObject element, ContextMenu menu)
    {
        while (element != null)
        {
            if (ReferenceEquals(element, menu))
            {
                return true;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static MenuItem? FindFirstMenuItem(ContextMenu? menu)
    {
        return menu?.Items.OfType<MenuItem>().FirstOrDefault(mi => mi.IsEnabled);
    }

    private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl root)
    {
        foreach (var item in root.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in EnumerateMenuItems(item))
            {
                yield return child;
            }
        }
    }

    private static MenuItem? FindAncestorMenuItem(DependencyObject? element, MenuItem excludeSelf)
    {
        while (element != null)
        {
            if (element is MenuItem mi && !ReferenceEquals(mi, excludeSelf))
            {
                return mi;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static MenuItem? GetFocusedMenuItem(ContextMenu menu)
    {
        var element = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
        while (element != null && !ReferenceEquals(element, menu))
        {
            if (element is MenuItem mi)
            {
                return mi;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static MenuItem? GetCurrentMenuItem(ContextMenu menu)
    {
        var focused = GetFocusedMenuItem(menu);
        if (focused != null) return focused;

        var highlighted = EnumerateMenuItems(menu).FirstOrDefault(mi => mi.IsHighlighted && mi.IsEnabled);
        if (highlighted != null) return highlighted;

        return FindFirstMenuItem(menu);
    }

    private static bool MoveMenuFocus(ContextMenu menu, FocusNavigationDirection direction)
    {
        var focused = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
        if (focused == null)
        {
            focused = FindFirstMenuItem(menu);
            if (focused == null)
            {
                return false;
            }
            focused.Focus();
        }
        return focused.MoveFocus(new TraversalRequest(direction));
    }

    private static bool TryEnterSubmenu(ContextMenu menu)
    {
        var focused = GetCurrentMenuItem(menu);
        if (focused == null)
        {
            return false;
        }

        if (focused.HasItems)
        {
            focused.IsSubmenuOpen = true;
            var firstChild = focused.Items.OfType<MenuItem>().FirstOrDefault(mi => mi.IsEnabled);
            if (firstChild != null)
            {
                firstChild.Focus();
                return true;
            }
        }

        return MoveMenuFocus(menu, FocusNavigationDirection.Right);
    }

    private static bool TryLeaveSubmenu(ContextMenu menu)
    {
        var focused = GetCurrentMenuItem(menu);
        if (focused != null)
        {
            if (focused.HasItems && focused.IsSubmenuOpen)
            {
                focused.IsSubmenuOpen = false;
                focused.Focus();
                return true;
            }

            var parent = ItemsControl.ItemsControlFromItemContainer(focused) as MenuItem
                         ?? FindAncestorMenuItem(VisualTreeHelper.GetParent(focused), focused);
            if (parent != null)
            {
                parent.IsSubmenuOpen = false;
                parent.Focus();
                return true;
            }
        }
        return MoveMenuFocus(menu, FocusNavigationDirection.Left);
    }

    private static bool TryActivateMenuSelection(ContextMenu menu)
    {
        var focused = GetCurrentMenuItem(menu);
        if (focused == null)
        {
            return false;
        }

        if (focused.HasItems)
        {
            focused.IsSubmenuOpen = true;
            var firstChild = focused.Items.OfType<MenuItem>().FirstOrDefault(mi => mi.IsEnabled);
            firstChild?.Focus();
            return true;
        }

        if (focused.IsCheckable && focused.IsEnabled)
        {
            focused.IsChecked = !focused.IsChecked;
        }
        focused.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.IsOpen = false;
        return true;
    }

    private bool TryMapMenuCommand(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers, out NavigationCommand command)
    {
        bool emacsEnabled = _config?.EnableEmacsNavigation ?? false;
        bool viEnabled = _config?.EnableViNavigation ?? false;
        command = NavigationCommand.MoveUp;
        bool ctrl = (modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        bool alt = (modifiers & System.Windows.Input.ModifierKeys.Alt) != 0;
        bool shift = (modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
        bool hasOther = (modifiers & ~(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift | System.Windows.Input.ModifierKeys.Alt)) != 0;
        if (hasOther)
        {
            return false;
        }

        if (!ctrl && !alt)
        {
            switch (key)
            {
                case System.Windows.Input.Key.Enter:
                    command = NavigationCommand.Confirm;
                    return true;
                case System.Windows.Input.Key.Escape:
                    command = NavigationCommand.Cancel;
                    return true;
            }
        }

        if (ctrl && !alt && !shift)
        {
            switch (key)
            {
                case System.Windows.Input.Key.M:
                case System.Windows.Input.Key.J:
                    command = NavigationCommand.Confirm;
                    return true;
            }
        }

        if (emacsEnabled && ctrl && !alt)
        {
            switch (key)
            {
                case System.Windows.Input.Key.N:
                    command = NavigationCommand.MoveDown;
                    return true;
                case System.Windows.Input.Key.P:
                    command = NavigationCommand.MoveUp;
                    return true;
                case System.Windows.Input.Key.F:
                    command = NavigationCommand.MoveRight;
                    return true;
                case System.Windows.Input.Key.B:
                    command = NavigationCommand.MoveLeft;
                    return true;
                case System.Windows.Input.Key.G:
                    command = NavigationCommand.Cancel;
                    return true;
                case System.Windows.Input.Key.M:
                case System.Windows.Input.Key.J:
                    command = NavigationCommand.Confirm;
                    return true;
            }
        }

        if (viEnabled && !ctrl && !alt)
        {
            switch (key)
            {
                case System.Windows.Input.Key.J:
                    command = NavigationCommand.MoveDown;
                    return true;
                case System.Windows.Input.Key.K:
                    command = NavigationCommand.MoveUp;
                    return true;
                case System.Windows.Input.Key.L:
                    command = NavigationCommand.MoveRight;
                    return true;
                case System.Windows.Input.Key.H:
                    command = NavigationCommand.MoveLeft;
                    return true;
            }
        }

        if (emacsEnabled && alt && !ctrl && key == System.Windows.Input.Key.X)
        {
            command = NavigationCommand.Cancel;
            return true;
        }

        if (viEnabled && (key == System.Windows.Input.Key.OemSemicolon || key == System.Windows.Input.Key.Oem1) && shift && !ctrl && !alt)
        {
            command = NavigationCommand.Cancel;
            return true;
        }

        return false;
    }

    private void ArmMetaPrefix()
    {
        _metaPrefixPending = true;
        _metaPrefixExpiryUtc = DateTime.UtcNow.AddSeconds(1.5);
    }

    private void OpenContextMenuFromKeyboard()
    {
        if (this.ContextMenu == null)
        {
            return;
        }

        var target = MenuBtn as UIElement ?? (UIElement)this;
        OnOpenMenu(target, new RoutedEventArgs());
        var first = FindFirstMenuItem(this.ContextMenu);
        first?.Focus();
    }

    private static bool IsArrowKey(System.Windows.Input.Key key)
    {
        return key is System.Windows.Input.Key.Up
            or System.Windows.Input.Key.Down
            or System.Windows.Input.Key.Left
            or System.Windows.Input.Key.Right;
    }

    private bool ShouldReturnToSearch(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
    {
        bool emacsEnabled = _config?.EnableEmacsNavigation ?? false;
        if (_isSearchOverlayBelowMain)
        {
            if (modifiers == System.Windows.Input.ModifierKeys.None && key == System.Windows.Input.Key.Down)
            {
                return true;
            }

            if (emacsEnabled && modifiers == System.Windows.Input.ModifierKeys.Control && key == System.Windows.Input.Key.N)
            {
                return true;
            }
        }
        else
        {
            if (modifiers == System.Windows.Input.ModifierKeys.None && key == System.Windows.Input.Key.Up)
            {
                return true;
            }

            if (emacsEnabled && modifiers == System.Windows.Input.ModifierKeys.Control && key == System.Windows.Input.Key.P)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanNavigateFromSearchToSlots()
    {
        if (!_searchLayerActive)
        {
            return false;
        }

        return _searchResults.Count > 0;
    }

    private void OnSlotContextMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var idx = GetSlotIndex(fe);
        if (!TryGetVisibleSlotModel(idx, out var layerIndex, out var slotIndex, out var slot))
        {
            return;
        }
        var emptyTargets = GetEmptySlotOptions();
        bool sourceHasContent = !IsSlotEmpty(slot);
        bool hasMoveTargets = emptyTargets.Any(opt => opt.LayerIndex != layerIndex || opt.SlotIndex != slotIndex);
        bool hasCopyTargets = emptyTargets.Count > 0;

        var cm = new ContextMenu();
        var miEdit = new MenuItem { Header = "Edit..." };
        miEdit.Click += (_, _) => EditSlot(fe);
        var miMove = new MenuItem { Header = "Move to..." , IsEnabled = sourceHasContent && hasMoveTargets };
        miMove.Click += async (_, _) => await MoveSlotAsync(layerIndex, slotIndex);
        var miCopy = new MenuItem { Header = "Copy to..." , IsEnabled = sourceHasContent && hasCopyTargets };
        miCopy.Click += async (_, _) => await CopySlotAsync(layerIndex, slotIndex);
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
        AttachSubmenuPlacementHandler(cm);
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
        Cancelling,
        Paused
    }

    private sealed record SearchResult(int LayerIndex, int SlotIndex);
    private sealed record PrefixSearchRestoreContext(bool WasMinimized, double Left, double Top, bool PreviousSuppressFixedCapture);
    private readonly record struct VisibleSlotMapping(int LayerIndex, int SlotIndex)
    {
        public bool IsEmpty => LayerIndex < 0 || SlotIndex < 0;
        public static VisibleSlotMapping Empty => new(-1, -1);
    }

    private sealed record ShortcutBinding(string NormalizedKey, int LayerIndex, int SlotIndex);
    private sealed record SlotLayoutDragData(int SourceLayerIndex, int SourceSlotIndex);
    private sealed record SlotVisual(
        Border Border,
        TextBlock Title,
        TextBlock Status,
        bool OverlayStatus,
        Border DragPreviewHost,
        TextBlock DragPreviewText);
    private sealed record SlotColorScheme(SolidColorBrush Background, SolidColorBrush Border, SolidColorBrush Title);
    private sealed class SlotRunContext
    {
        public SlotRunContext(int layerIndex, int slotIndex)
        {
            LayerIndex = layerIndex;
            SlotIndex = slotIndex;
        }

        public int LayerIndex { get; }
        public int SlotIndex { get; }
        public bool CancellationRequested { get; set; }
        public bool IsPaused { get; set; }
    }

    private SlotMacroState GetSlotMacroState(int index)
    {
        if (!TryGetVisibleSlot(index, out var layerIndex, out var slotIndex))
        {
            return SlotMacroState.Idle;
        }

        var context = GetSlotContext(layerIndex, slotIndex);
        if (context == null) return SlotMacroState.Idle;
        if (context.CancellationRequested) return SlotMacroState.Cancelling;
        if (context.IsPaused) return SlotMacroState.Paused;
        return ReferenceEquals(context, _currentSlotRun) ? SlotMacroState.Running : SlotMacroState.Idle;
    }

    private void RenderSlotMacroState(int index, SlotMacroState state)
    {
        if (index < 0 || index >= _slotVisuals.Count) return;

        var visual = _slotVisuals[index];
        var border = visual.Border;

        switch (state)
        {
            case SlotMacroState.Running:
                border.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x1A, 0x2E, 0x1A));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x5A, 0xD6, 0x6B));
                UpdateSlotStatusVisual(visual, "マクロ実行中...", MediaColor.FromRgb(0x7C, 0xFF, 0xB0), true);
                break;
            case SlotMacroState.Cancelling:
                border.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x2E, 0x28, 0x1A));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0xFF, 0xC6, 0x4D));
                UpdateSlotStatusVisual(visual, "キャンセル中...", MediaColor.FromRgb(0xFF, 0xD7, 0x66), true);
                break;
            case SlotMacroState.Paused:
                border.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x1E, 0x1A, 0x2E));
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x98, 0x7C, 0xFF));
                UpdateSlotStatusVisual(visual, "一時停止中...", MediaColor.FromRgb(0xC9, 0xB6, 0xFF), true);
                break;
            default:
                ApplySlotColor(index);
                var statusText = TryGetVisibleSlot(index, out _, out _) ? "マクロ実行中..." : string.Empty;
                UpdateSlotStatusVisual(visual, statusText, MediaColor.FromRgb(0x7C, 0xFF, 0xB0), false);
                break;
        }
    }

    private bool HasAnyRunningSlot() => _currentSlotRun != null || _slotRunStack.Count > 0;

    private bool IsSlotCurrentlyRunning(int layerIndex, int slotIndex) =>
        _currentSlotRun != null &&
        _currentSlotRun.LayerIndex == layerIndex &&
        _currentSlotRun.SlotIndex == slotIndex;

    private SlotRunContext? GetSlotContext(int layerIndex, int slotIndex)
    {
        if (_currentSlotRun != null &&
            _currentSlotRun.LayerIndex == layerIndex &&
            _currentSlotRun.SlotIndex == slotIndex)
        {
            return _currentSlotRun;
        }

        foreach (var context in _slotRunStack)
        {
            if (context.LayerIndex == layerIndex && context.SlotIndex == slotIndex)
            {
                return context;
            }
        }

        return null;
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
        if (_currentSlotRun != null)
        {
            _slotRunStack.Push(_currentSlotRun);
        }
        _currentSlotRun = new SlotRunContext(layerIndex, index);
        UpdateNotifyIconState(true);
        if (TryFindDisplayIndex(layerIndex, index, out var displayIndex))
        {
            RenderSlotMacroState(displayIndex, SlotMacroState.Running);
        }
    }

    private void MarkSlotMacroCanceling(int layerIndex, int index)
    {
        var context = GetSlotContext(layerIndex, index);
        if (context == null) return;
        context.CancellationRequested = true;
        UpdateSlotVisual(context);
    }

    private void SetSlotPaused(SlotRunContext? context, bool isPaused)
    {
        if (context == null || context.IsPaused == isPaused)
        {
            return;
        }

        context.IsPaused = isPaused;
        UpdateSlotVisual(context);
    }

    private async Task ResumeSuspendedMacroScopeAsync(IAsyncDisposable suspension, SlotRunContext? pausedContext)
    {
        try
        {
            if (pausedContext != null)
            {
                SetSlotPaused(pausedContext, false);
            }
            await suspension.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to resume suspended macro: {ex}");
            if (pausedContext != null)
            {
                ClearSlotMacroState(pausedContext.LayerIndex, pausedContext.SlotIndex);
            }
            else
            {
                await _macroService.CancelAllRunningMacrosAsync(CancellationToken.None);
            }
        }
    }

    private void ForceClearAllSlotStates()
    {
        _slotRunStack.Clear();
        _currentSlotRun = null;
        UpdateAllSlotMacroStates();
        UpdateNotifyIconState(false);
    }

    private void UpdateSlotVisual(SlotRunContext context)
    {
        if (!TryFindDisplayIndex(context.LayerIndex, context.SlotIndex, out var displayIndex))
        {
            return;
        }

        SlotMacroState state;
        if (context.CancellationRequested)
        {
            state = SlotMacroState.Cancelling;
        }
        else if (context.IsPaused)
        {
            state = SlotMacroState.Paused;
        }
        else if (ReferenceEquals(context, _currentSlotRun))
        {
            state = SlotMacroState.Running;
        }
        else
        {
            state = SlotMacroState.Idle;
        }

        RenderSlotMacroState(displayIndex, state);
    }

    private void ClearSlotMacroState(int layerIndex, int index)
    {
        if (_currentSlotRun != null &&
            _currentSlotRun.LayerIndex == layerIndex &&
            _currentSlotRun.SlotIndex == index)
        {
            _currentSlotRun = _slotRunStack.Count > 0 ? _slotRunStack.Pop() : null;
        }

        if (TryFindDisplayIndex(layerIndex, index, out var displayIndex))
        {
            RenderSlotMacroState(displayIndex, SlotMacroState.Idle);
        }

        if (_currentSlotRun != null &&
            TryFindDisplayIndex(_currentSlotRun.LayerIndex, _currentSlotRun.SlotIndex, out var currentDisplayIndex))
        {
            var state = _currentSlotRun.CancellationRequested
                ? SlotMacroState.Cancelling
                : (_currentSlotRun.IsPaused ? SlotMacroState.Paused : SlotMacroState.Running);
            RenderSlotMacroState(currentDisplayIndex, state);
        }

        if (!HasAnyRunningSlot() && !_macroService.IsMacroRunning)
        {
            UpdateNotifyIconState(false);
        }
    }

    private void EditSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        if (!TryGetVisibleSlotModel(idx, out _, out _, out var slot))
        {
            return;
        }
        if (_config == null)
        {
            _logger.Error("Config is not loaded; cannot edit slot.");
            return;
        }
        var config = _config;
        if ((config.DefaultMinimizeOptions != null && IsSlotEmpty(slot)) || slot.MinimizeOptions == null)
        {
            slot.MinimizeOptions = (config.DefaultMinimizeOptions ?? SlotMinimizeOptions.CreateDefault()).Clone();
        }
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
            slot.AccentColor = args.AccentColor;
            slot.MinimizeOptions = args.MinimizeOptions ?? SlotMinimizeOptions.CreateDefault();
            slot.SearchKeywords = args.SearchKeywords;
            _configService.Save(config);
            RefreshUi();
        };

        dlg.Show();
    }

    private void ClearSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        if (!TryGetVisibleSlotModel(idx, out var layerIndex, out var slotIndex, out var slot))
        {
            return;
        }
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
        _config.Layers[layerIndex].Slots[slotIndex] = new SlotModel();
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
            ExecutionMode = source.ExecutionMode,
            AccentColor = source.AccentColor,
            MinimizeOptions = source.MinimizeOptions == null
                ? SlotMinimizeOptions.CreateDefault()
                : new SlotMinimizeOptions
                {
                    EnableOnClick = source.MinimizeOptions.EnableOnClick,
                    EnableOnShortcut = source.MinimizeOptions.EnableOnShortcut,
                    EnableOnDrop = source.MinimizeOptions.EnableOnDrop,
                    EnableOnKeyboard = source.MinimizeOptions.EnableOnKeyboard
                },
            SearchKeywords = source.SearchKeywords
        };
    }

    private string DescribeSlotForEditMode(int layerIndex, int slotIndex)
    {
        if (layerIndex < 0 || layerIndex >= _config.Layers.Count)
        {
            return $"Layer {layerIndex + 1} / Slot {slotIndex + 1}";
        }

        var layer = _config.Layers[layerIndex];
        if (slotIndex < 0 || slotIndex >= layer.Slots.Count)
        {
            return $"Layer {layerIndex + 1} / Slot {slotIndex + 1}";
        }

        var slot = layer.Slots[slotIndex];
        string title = string.IsNullOrWhiteSpace(slot.Title) ? $"Slot {slotIndex + 1}" : slot.Title.Trim();
        if (IsSlotEmpty(slot))
        {
            title += " (Empty)";
        }
        return $"L{layerIndex + 1}-S{slotIndex + 1}: {title}";
    }

    private void TryBeginSlotLayoutDrag(FrameworkElement? fe)
    {
        if (!_isSlotLayoutEditMode || fe == null || _isSlotLayoutDragInProgress)
        {
            return;
        }

        if (_searchLayerActive)
        {
            return;
        }

        int index = GetSlotIndex(fe);
        var layer = _config.Layers[_currentLayer];
        if (index < 0 || index >= layer.Slots.Count)
        {
            return;
        }

        var slot = layer.Slots[index];
        if (IsSlotEmpty(slot))
        {
            return;
        }

        if (HasAnyRunningSlot())
        {
            WpfMessageBox.Show("マクロ実行中はスロット編集モードに切り替えられません。", "Slot Setup Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isSlotLayoutDragInProgress = true;
        _slotLayoutDragSourceLayer = _currentLayer;
        _slotLayoutDragSourceIndex = index;
        HighlightSlotDragSource(index, true);
        UpdateEditModeIndicatorText();
        HideSlotDragPreview();

        var payload = new SlotLayoutDragData(_slotLayoutDragSourceLayer, _slotLayoutDragSourceIndex);
        var data = new System.Windows.DataObject(SlotLayoutDragFormat, payload);
        try
        {
            DragDrop.DoDragDrop(fe, data, WpfDragDropEffects.Move);
        }
        finally
        {
            HighlightSlotDragSource(index, false);
            HideSlotDragPreview();
            _slotLayoutDragSourceLayer = -1;
            _slotLayoutDragSourceIndex = -1;
            _isSlotLayoutDragInProgress = false;
            UpdateEditModeIndicatorText();
        }
    }

    private void HighlightSlotDragSource(int index, bool isActive)
    {
        if (index < 0 || index >= _slotVisuals.Count)
        {
            return;
        }

        var border = _slotVisuals[index].Border;
        if (isActive)
        {
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x7C, 0xFF, 0xB0));
            border.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x1A, 0x35, 0x1A));
        }
        else
        {
            RenderSlotMacroState(index, GetSlotMacroState(index));
        }
    }

    private void UpdateSlotLayoutPreview(int targetLayer, int targetSlot)
    {
        if (!_isSlotLayoutDragInProgress || _slotLayoutDragSourceIndex < 0 || _slotLayoutDragSourceIndex >= _slotVisuals.Count)
        {
            return;
        }

        _slotLayoutPreviewTargetLayer = targetLayer;
        _slotLayoutPreviewTargetIndex = targetSlot;

        var visual = _slotVisuals[_slotLayoutDragSourceIndex];
        if (targetLayer == _slotLayoutDragSourceLayer && targetSlot == _slotLayoutDragSourceIndex)
        {
            visual.DragPreviewHost.Visibility = Visibility.Collapsed;
            return;
        }

        var description = DescribeSlotForEditMode(targetLayer, targetSlot);
        visual.DragPreviewText.Text = $"Swap with\n{description}";
        visual.DragPreviewHost.Visibility = Visibility.Visible;
    }

    private void HideSlotDragPreview()
    {
        if (_slotLayoutDragSourceIndex >= 0 && _slotLayoutDragSourceIndex < _slotVisuals.Count)
        {
            _slotVisuals[_slotLayoutDragSourceIndex].DragPreviewHost.Visibility = Visibility.Collapsed;
        }
        _slotLayoutPreviewTargetLayer = -1;
        _slotLayoutPreviewTargetIndex = -1;
    }

    private bool TryHandleSlotLayoutDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(SlotLayoutDragFormat))
        {
            return false;
        }

        e.Handled = true;
        HideSlotDragPreview();
        if (sender is not FrameworkElement fe)
        {
            return true;
        }

        if (e.Data.GetData(SlotLayoutDragFormat) is not SlotLayoutDragData payload)
        {
            return true;
        }

        int targetSlot = GetSlotIndex(fe);
        int targetLayer = _currentLayer;
        CompleteSlotSwap(payload.SourceLayerIndex, payload.SourceSlotIndex, targetLayer, targetSlot);
        return true;
    }

    private void CompleteSlotSwap(int sourceLayer, int sourceSlot, int targetLayer, int targetSlot)
    {
        if (sourceLayer < 0 || sourceLayer >= _config.Layers.Count ||
            targetLayer < 0 || targetLayer >= _config.Layers.Count)
        {
            return;
        }

        var sourceSlots = _config.Layers[sourceLayer].Slots;
        var targetSlots = _config.Layers[targetLayer].Slots;
        if (sourceSlot < 0 || sourceSlot >= sourceSlots.Count ||
            targetSlot < 0 || targetSlot >= targetSlots.Count)
        {
            return;
        }

        if (sourceLayer == targetLayer && sourceSlot == targetSlot)
        {
            return;
        }

        (sourceSlots[sourceSlot], targetSlots[targetSlot]) = (targetSlots[targetSlot], sourceSlots[sourceSlot]);
        _configService.Save(_config);
        RefreshUi();
    }

    private static bool IsSlotLayoutDrag(DragEventArgs e)
    {
        return e.Data.GetDataPresent(SlotLayoutDragFormat);
    }

    private List<SlotSelectionOption> GetEmptySlotOptions()
    {
        var options = new List<SlotSelectionOption>();
        if (_config?.Layers == null || _config.Layers.Count == 0)
        {
            return options;
        }

        int visibleSlots = Math.Max(0, _config.SlotRows * _config.SlotColumns);
        if (visibleSlots == 0)
        {
            return options;
        }

        for (int layerIndex = 0; layerIndex < _config.Layers.Count; layerIndex++)
        {
            var layer = _config.Layers[layerIndex];
            var slots = layer.Slots ??= new List<SlotModel>();
            int maxIndex = Math.Min(visibleSlots, slots.Count);
            for (int slotIndex = 0; slotIndex < maxIndex; slotIndex++)
            {
                if (IsSlotEmpty(slots[slotIndex]))
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

    private async Task MoveSlotAsync(int sourceLayerIndex, int sourceSlotIndex)
    {
        var sourceSlot = _config.Layers[sourceLayerIndex].Slots[sourceSlotIndex];
        sourceSlot.MinimizeOptions ??= SlotMinimizeOptions.CreateDefault();
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
        if (!await dialog.ShowForResultAsync() || dialog.SelectedOption == null)
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

    private async Task CopySlotAsync(int sourceLayerIndex, int sourceSlotIndex)
    {
        var sourceSlot = _config.Layers[sourceLayerIndex].Slots[sourceSlotIndex];
        sourceSlot.MinimizeOptions ??= SlotMinimizeOptions.CreateDefault();
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
        if (!await dialog.ShowForResultAsync() || dialog.SelectedOption == null)
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
            if (TryHandleSlotLayoutDrop(sender, e))
            {
                return;
            }
            if (_isSlotLayoutEditMode && TryRegisterSlotFromEditModeDrop(sender, e))
            {
                return;
            }
            if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
            if (sender is not FrameworkElement fe) return;
            int idx = GetSlotIndex(fe);
            if (!TryGetVisibleSlotModel(idx, out var layerIndex, out var slotIndex, out var slot))
            {
                return;
            }
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
            _ = TriggerSlotAsync(layerIndex, slotIndex, SlotTriggerKind.Drop, paths);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(ex.Message, "Drop Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool TryHandleEditModeDropPreview(FrameworkElement target, DragEventArgs e)
    {
        if (!_isSlotLayoutEditMode || _config == null)
        {
            return false;
        }

        if (!TryGetVisibleSlotModel(GetSlotIndex(target), out _, out _, out var slot))
        {
            return false;
        }

        var paths = e.Data.GetData(WpfDataFormats.FileDrop) as string[];
        var text = TryGetDropText(e.Data);
        bool canRegister = IsSlotEmpty(slot) && SlotDropRegistrationHelper.TryCreate(paths, text, out _);
        e.Handled = true;
        e.Effects = canRegister ? WpfDragDropEffects.Copy : WpfDragDropEffects.None;
        return true;
    }

    private bool TryRegisterSlotFromEditModeDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_config == null)
        {
            return true;
        }

        if (sender is not FrameworkElement fe)
        {
            return true;
        }

        int idx = GetSlotIndex(fe);
        if (!TryGetVisibleSlotModel(idx, out var layerIndex, out var slotIndex, out var slot))
        {
            return true;
        }

        if (!IsSlotEmpty(slot))
        {
            WpfMessageBox.Show("空きスロットにドロップしてください。", "Slot Setup Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        var paths = e.Data.GetData(WpfDataFormats.FileDrop) as string[];
        var text = TryGetDropText(e.Data);
        if (!SlotDropRegistrationHelper.TryCreate(paths, text, out var registration))
        {
            return true;
        }

        slot.Title = registration.Title;
        slot.Command = registration.Command;
        slot.ArgumentsTemplate = registration.ArgumentsTemplate;
        slot.ExecutionMode = registration.ExecutionMode;
        slot.KeyboardMacroScript = string.Empty;
        slot.ShortcutKey = string.Empty;
        slot.ClickEnabled = true;
        slot.MinimizeOptions ??= (_config.DefaultMinimizeOptions ?? SlotMinimizeOptions.CreateDefault()).Clone();
        slot.SearchKeywords = slot.SearchKeywords ?? string.Empty;
        slot.IconPath = slot.IconPath ?? string.Empty;

        _configService.Save(_config);
        _logger.Info($"Registered slot via edit-mode drop (layer={layerIndex + 1}, slot={slotIndex + 1}, title=\"{slot.Title}\", command=\"{slot.Command}\").");
        RefreshUi();
        return true;
    }

    private static string? TryGetDropText(System.Windows.IDataObject data)
    {
        if (data.GetDataPresent(WpfDataFormats.UnicodeText))
        {
            return data.GetData(WpfDataFormats.UnicodeText) as string;
        }

        if (data.GetDataPresent(WpfDataFormats.Text))
        {
            return data.GetData(WpfDataFormats.Text) as string;
        }

        return null;
    }

    private void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        if (this.ContextMenu != null)
        {
            UpdateContextMenuState();
            this.ContextMenu.PlacementTarget = (UIElement)sender;
            this.ContextMenu.PreviewKeyDown -= OnContextMenuPreviewKeyDown;
            this.ContextMenu.PreviewKeyDown += OnContextMenuPreviewKeyDown;
            this.ContextMenu.Closed -= OnContextMenuClosed;
            this.ContextMenu.Closed += OnContextMenuClosed;
            this.ContextMenu.IsOpen = true;
        }
    }

    private void AttachSubmenuPlacementHandler(ContextMenu? menu)
    {
        if (menu == null)
        {
            return;
        }

        menu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnAnySubmenuOpened), true);
    }

    private void OnAnySubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem menuItem)
        {
            return;
        }

        menuItem.ApplyTemplate();
        if (menuItem.Template?.FindName("PART_Popup", menuItem) is Popup popup)
        {
            popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback = RightFirstSubmenuPlacement;
        }
    }

    private static CustomPopupPlacement[] RightFirstSubmenuPlacement(System.Windows.Size popupSize, System.Windows.Size targetSize, System.Windows.Point offset)
    {
        return new[]
        {
            new CustomPopupPlacement(new System.Windows.Point(targetSize.Width, 0), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new System.Windows.Point(-popupSize.Width, 0), PopupPrimaryAxis.Horizontal)
        };
    }

    private void OnConfigureSlotLayout(object sender, RoutedEventArgs e)
    {
        var dialog = new SlotLayoutDialog(_config.SlotRows, _config.SlotColumns)
        {
            Owner = this
        };
        WindowCascadeService.Arrange(dialog, this);
        var result = dialog.ShowDialog();
        if (result != true)
        {
            return;
        }

        int rows = Math.Clamp(dialog.Rows, MinSlotRows, MaxSlotRows);
        int columns = Math.Clamp(dialog.Columns, MinSlotColumns, MaxSlotColumns);
        if (rows == _config.SlotRows && columns == _config.SlotColumns)
        {
            return;
        }

        _config.SlotRows = rows;
        _config.SlotColumns = columns;
        ApplySlotLayout();
        RefreshUi();
        ClampWindowWithinBounds();
        _configService.Save(_config);
    }

    private void OnConfigureLayerCount(object sender, RoutedEventArgs e)
    {
        if (_config?.Layers == null)
        {
            return;
        }

        var dialog = new LayerCountDialog(_config.Layers.Count, MinLayers, MaxLayers)
        { Owner = this };
        WindowCascadeService.Arrange(dialog, this);
        var result = dialog.ShowDialog();
        if (result != true || !dialog.IsConfirmed)
        {
            return;
        }

        int desired = dialog.SelectedCount;

        if (desired == _config.Layers.Count)
        {
            return;
        }

        ApplyLayerCount(desired);
    }

    private void ApplyLayerCount(int newCount)
    {
        if (_config?.Layers == null)
        {
            return;
        }

        int requiredSlots = _config.SlotRows * _config.SlotColumns;
        int current = _config.Layers.Count;
        if (newCount < current)
        {
            _config.Layers.RemoveRange(newCount, current - newCount);
        }
        else if (newCount > current)
        {
            for (int i = current; i < newCount; i++)
            {
                var layer = new Layer();
                EnsureLayerSlotCapacity(layer, requiredSlots);
                _config.Layers.Add(layer);
            }
        }

        _currentLayer = Math.Clamp(_currentLayer, 0, _config.Layers.Count - 1);
        _config.CurrentLayer = _currentLayer;
        ClampShowLayerPreferencesToRange();
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
        RefreshVisibleSlotMappings();
        RefreshUi();
        _configService.Save(_config);
    }

    private void ClampShowLayerPreferencesToRange()
    {
        if (_config?.Layers == null || _config.Layers.Count == 0)
        {
            return;
        }

        int maxIndex = _config.Layers.Count - 1;
        int Normalize(int value) => value < 0 ? -1 : Math.Clamp(value, 0, maxIndex);

        _config.MouseGestureShowLayerWhenVisible = Normalize(_config.MouseGestureShowLayerWhenVisible);
        _config.MouseGestureShowLayerWhenHidden = Normalize(_config.MouseGestureShowLayerWhenHidden);
        _config.PrefixShowLayerWhenVisible = Normalize(_config.PrefixShowLayerWhenVisible);
        _config.PrefixShowLayerWhenHidden = Normalize(_config.PrefixShowLayerWhenHidden);
    }

    private void OnSlotSizeMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        PopulateSlotSizeMenu(menuItem);
    }

    private void OnSlotSizeOptionSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not SlotSize size) return;
        if (_config.SlotSize == size && size != SlotSize.Custom) return;

        if (size == SlotSize.Custom)
        {
            var dialog = new CustomSlotSizeDialog(_config.CustomSlotSize ?? CustomSlotSizeOptions.CreateDefault())
            {
                Owner = this
            };
            bool appliedDuringDialog = false;
            dialog.SlotSizeApplied += (_, options) =>
            {
                appliedDuringDialog = true;
                ApplyCustomSlotSize(options);
            };
            WindowCascadeService.Arrange(dialog, this);
            var result = dialog.ShowDialog();
            if (result == true && !appliedDuringDialog)
            {
                ApplyCustomSlotSize(dialog.ResultOptions?.Clone() ?? CustomSlotSizeOptions.CreateDefault());
            }
            else if (result != true && !appliedDuringDialog)
            {
                return;
            }

            return;
        }

        _config.SlotSize = size;
        ApplySlotLayout();
        RefreshUi();
        ClampWindowWithinBounds();
        _configService.Save(_config);
    }

    private void ApplyCustomSlotSize(CustomSlotSizeOptions options)
    {
        if (_config == null)
        {
            return;
        }

        _config.CustomSlotSize = CustomSlotSizeNormalizer.Normalize(options.Clone());
        _config.SlotSize = SlotSize.Custom;
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

    private void OnEditModeToggleButtonClick(object sender, RoutedEventArgs e)
    {
        SetSlotLayoutEditMode(!_isSlotLayoutEditMode);
    }

    private void SetSlotLayoutEditMode(bool isEnabled)
    {
        if (isEnabled == _isSlotLayoutEditMode)
        {
            UpdateEditModeIndicatorText();
            return;
        }

        if (isEnabled && _searchLayerActive)
        {
            WpfMessageBox.Show("検索レイヤー表示中はスロット編集モードに切り替えられません。検索を閉じてから再度お試しください。", "Slot Setup Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (isEnabled && HasAnyRunningSlot())
        {
            WpfMessageBox.Show("マクロ実行中はスロット編集モードに切り替えられません。", "Slot Setup Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isSlotLayoutEditMode = isEnabled;
        if (!isEnabled)
        {
            HideSlotDragPreview();
        }
        _slotDragStartPoint = null;

        if (EditModeIndicator != null)
        {
            EditModeIndicator.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateEditModeIndicatorText();
        UpdateSlotPanelEditModePadding();
        UpdateWindowSize(_config.SlotRows, _config.SlotColumns);
        ClampWindowWithinBounds();
        ApplyTopmostState();
        if (EditModeToggleButton != null)
        {
            EditModeToggleButton.Content = isEnabled ? "✕" : "✎";
            EditModeToggleButton.ToolTip = isEnabled ? "Exit Slot Setup Mode" : "Slot Setup Mode";
        }
    }

    private void UpdateEditModeIndicatorText()
    {
        if (EditModeIndicatorText == null)
        {
            return;
        }

        if (!_isSlotLayoutEditMode)
        {
            EditModeIndicatorText.Text = string.Empty;
            return;
        }

        EditModeIndicatorText.Text = "SLOT SETUP MODE";
    }

    private void UpdateSlotPanelEditModePadding()
    {
        if (SlotsPanel == null)
        {
            return;
        }

        if (_isSlotLayoutEditMode)
        {
            SlotsPanel.Margin = new Thickness(0, GetEditModeReservedHeight(), 0, 0);
        }
        else
        {
            SlotsPanel.Margin = new Thickness(0);
        }
    }

    private double GetEditModeReservedHeight()
    {
        if (!_isSlotLayoutEditMode)
        {
            return 0;
        }

        double indicatorHeight = EditModeIndicator?.ActualHeight ?? LayoutEditIndicatorFallbackHeight;
        if (double.IsNaN(indicatorHeight) || indicatorHeight <= 0)
        {
            indicatorHeight = LayoutEditIndicatorFallbackHeight;
        }

        return LayoutEditIndicatorTopSpacing + indicatorHeight + LayoutEditIndicatorBottomSpacing;
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

    private async void OnExportConfig(object sender, RoutedEventArgs e)
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
            if (!await passwordDialog.ShowForResultAsync())
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

    private async void OnImportConfig(object sender, RoutedEventArgs e)
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
            if (!await passwordDialog.ShowForResultAsync())
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
            int totalLayers = Math.Max(_config.Layers?.Count ?? MinLayers, 1);
            _currentLayer = Math.Clamp(_config.CurrentLayer, 0, totalLayers - 1);
            _searchPlacementFollowsKeyboard = _config.SearchPlacementFollowsKeyboard;
            ApplyTopmostState();
            _currentLanguage = _config.Language;
            _shortcutService.UpdatePrefix(_config.ShortcutPrefix, _config.ShortcutPrefixDisabled);
            _shortcutService.UpdateSearchHotkey(_config.SearchHotkey, _config.SearchHotkeyEnabled);
            ApplySlotLayout();
            RestoreWindowPosition();
            Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
            RefreshUi();
            UpdateTrayMenuState();
            ApplyLanguageToUi();
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

    private async void OnChangePrefix(object sender, RoutedEventArgs e)
    {
        var dlg = new PrefixDialog(_config.ShortcutPrefix, _config.ShortcutPrefixDisabled) { Owner = this };
        WindowCascadeService.Arrange(dlg, this);
        if (await dlg.ShowForResultAsync())
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

    private void OnConfigureSearchHotkey(object sender, RoutedEventArgs e)
    {
        var dlg = new SearchHotkeyDialog(_config.SearchHotkey, _config.SearchHotkeyEnabled) { Owner = this };
        WindowCascadeService.Arrange(dlg, this);
        dlg.ShowDialog();
        if (!dlg.IsConfirmed)
        {
            return;
        }

        bool enabled = dlg.IsHotkeyEnabled;
        var hotkey = dlg.NormalizedHotkey;
        if (_config.SearchHotkeyEnabled == enabled && string.Equals(_config.SearchHotkey, hotkey, StringComparison.Ordinal))
        {
            return;
        }

        _config.SearchHotkeyEnabled = enabled;
        _config.SearchHotkey = hotkey;
        _shortcutService.UpdateSearchHotkey(hotkey, enabled);
        _configService.Save(_config);
    }

    private async void OnConfigureMouseGestures(object sender, RoutedEventArgs e)
    {
        if (_config == null)
        {
            WpfMessageBox.Show("設定がまだ読み込まれていません。少し待ってから再度お試しください。", "Mouse Gesture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var options = BuildMouseGestureOptions();
            var dlg = new MouseGestureDialog(options) { Owner = this };
            WindowCascadeService.Arrange(dlg, this);
            if (!await dlg.ShowForResultAsync())
            {
                return;
            }

            var result = dlg.ResultOptions;
        bool changed =
            _config.EnableMouseGestures != result.Enabled
            || _config.MouseGestureClockwiseTurnsToShow != result.ClockwiseTurnsToShow
            || _config.MouseGestureCounterClockwiseTurnsToHide != result.CounterClockwiseTurnsToHide
            || _config.MouseGestureInvertDirections != result.InvertDirections
            || _config.MouseGestureRequireCtrl != result.RequireCtrl
            || _config.MouseGestureSuppressDuringPresentation != result.SuppressDuringPresentation
            || _config.MouseGestureEnforceRadiusLimit != result.EnforceRadiusLimit
            || _config.MouseGestureMaxRadiusPixels != result.MaxRadiusPixels;

            if (!changed)
            {
                return;
            }

            _config.EnableMouseGestures = result.Enabled;
            _config.MouseGestureClockwiseTurnsToShow = result.ClockwiseTurnsToShow;
            _config.MouseGestureCounterClockwiseTurnsToHide = result.CounterClockwiseTurnsToHide;
            _config.MouseGestureInvertDirections = result.InvertDirections;
        _config.MouseGestureRequireCtrl = result.RequireCtrl;
        _config.MouseGestureSuppressDuringPresentation = result.SuppressDuringPresentation;
        _config.MouseGestureEnforceRadiusLimit = result.EnforceRadiusLimit;
        _config.MouseGestureMaxRadiusPixels = result.MaxRadiusPixels;

            ApplyMouseGestureOptions();
            _configService.Save(_config);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open mouse gesture dialog: {ex}");
            WpfMessageBox.Show("マウスジェスチャ設定の表示に失敗しました。ログをご確認ください。", "Mouse Gesture", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnConfigureShowLayerPreferences(object sender, RoutedEventArgs e)
    {
        if (_config == null)
        {
            WpfMessageBox.Show("設定がまだ読み込まれていません。少し待ってから再度お試しください。", "Show Layer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new ShowLayerPreferenceDialog(new ShowLayerPreferenceOptions(
            _config.MouseGestureShowLayerWhenVisible,
            _config.MouseGestureShowLayerWhenHidden,
            _config.PrefixShowLayerWhenVisible,
            _config.PrefixShowLayerWhenHidden),
            _config.Layers.Count)
        { Owner = this };
        WindowCascadeService.Arrange(dialog, this);
        if (!await dialog.ShowForResultAsync())
        {
            return;
        }

        var result = dialog.ResultOptions;
        bool changed = false;

        if (_config.MouseGestureShowLayerWhenVisible != result.MouseGestureVisibleLayer)
        {
            _config.MouseGestureShowLayerWhenVisible = result.MouseGestureVisibleLayer;
            changed = true;
        }
        if (_config.MouseGestureShowLayerWhenHidden != result.MouseGestureHiddenLayer)
        {
            _config.MouseGestureShowLayerWhenHidden = result.MouseGestureHiddenLayer;
            changed = true;
        }
        if (_config.PrefixShowLayerWhenVisible != result.PrefixVisibleLayer)
        {
            _config.PrefixShowLayerWhenVisible = result.PrefixVisibleLayer;
            changed = true;
        }
        if (_config.PrefixShowLayerWhenHidden != result.PrefixHiddenLayer)
        {
            _config.PrefixShowLayerWhenHidden = result.PrefixHiddenLayer;
            changed = true;
        }

        if (changed)
        {
            _configService.Save(_config);
        }
    }

    private async void OnEditLayerNames(object sender, RoutedEventArgs e)
    {
        if (_config?.Layers == null || _config.Layers.Count == 0)
        {
            return;
        }

        var names = _config.Layers.Select(l => l.Name ?? string.Empty).ToList();
        var dialog = new LayerNamesDialog(names) { Owner = this };
        WindowCascadeService.Arrange(dialog, this);
        if (!await dialog.ShowForResultAsync())
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < _config.Layers.Count && i < dialog.LayerNames.Count; i++)
        {
            var newName = dialog.LayerNames[i] ?? string.Empty;
            if (!string.Equals(_config.Layers[i].Name, newName, StringComparison.Ordinal))
            {
                _config.Layers[i].Name = newName;
                changed = true;
            }
        }

        if (changed)
        {
            _configService.Save(_config);
            ShowLayerNameOverlay();
        }
    }

    private void OnMinimizeToTray(object sender, RoutedEventArgs e)
    {
        MinimizeWindowToTray();
    }

    private void OnTogglePrefixLayerShortcuts(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        bool enabled = item.IsChecked;
        if (_config.EnablePrefixLayerShortcuts == enabled) return;
        _config.EnablePrefixLayerShortcuts = enabled;
        _shortcutService.SetPrefixLayerShortcutsEnabled(enabled);
        _configService.Save(_config);
    }

    private void OnKeyboardPlacementFixed(object sender, RoutedEventArgs e)
    {
        SetPlacementMode(WindowPlacementMode.Fixed, ShowLayerTrigger.Prefix, initiatedByToggle: false);
        UpdatePlacementMenuState();
    }

    private void OnKeyboardPlacementMouseFollow(object sender, RoutedEventArgs e)
    {
        SetPlacementMode(WindowPlacementMode.MouseFollow, ShowLayerTrigger.Prefix, initiatedByToggle: false);
        UpdatePlacementMenuState();
    }

    private void OnKeyboardPlacementScreenCenter(object sender, RoutedEventArgs e)
    {
        SetPlacementMode(WindowPlacementMode.CursorScreenCenter, ShowLayerTrigger.Prefix, initiatedByToggle: false);
        UpdatePlacementMenuState();
    }

    private void OnMousePlacementFollowKeyboard(object sender, RoutedEventArgs e)
    {
        _mousePlacementFollowsKeyboard = true;
        _config.MousePlacementFollowsKeyboard = true;
        _mousePlacementMode = _keyboardPlacementMode;
        _config.MousePlacementMode = _mousePlacementMode;
        _configService.Save(_config);
        UpdatePlacementMenuState();
    }

    private void OnMousePlacementFixed(object sender, RoutedEventArgs e)
    {
        _mousePlacementFollowsKeyboard = false;
        _config.MousePlacementFollowsKeyboard = false;
        SetPlacementMode(WindowPlacementMode.Fixed, ShowLayerTrigger.MouseGesture, initiatedByToggle: false);
        UpdatePlacementMenuState();
    }

    private void OnMousePlacementMouseFollow(object sender, RoutedEventArgs e)
    {
        _mousePlacementFollowsKeyboard = false;
        _config.MousePlacementFollowsKeyboard = false;
        SetPlacementMode(WindowPlacementMode.MouseFollow, ShowLayerTrigger.MouseGesture, initiatedByToggle: false);
        UpdatePlacementMenuState();
    }

    private void OnMousePlacementScreenCenter(object sender, RoutedEventArgs e)
    {
        _mousePlacementFollowsKeyboard = false;
        _config.MousePlacementFollowsKeyboard = false;
        SetPlacementMode(WindowPlacementMode.CursorScreenCenter, ShowLayerTrigger.MouseGesture, initiatedByToggle: false);
        UpdatePlacementMenuState();
    }

    private void OnSearchPlacementFixed(object sender, RoutedEventArgs e)
    {
        _searchPlacementFollowsKeyboard = false;
        _config.SearchPlacementFollowsKeyboard = false;
        _searchPlacementMode = SearchOverlayPlacementMode.Fixed;
        _config.SearchPlacementMode = _searchPlacementMode;
        _configService.Save(_config);
        UpdatePlacementMenuState();
        if (_searchLayerActive && _searchOverlayWindow?.IsVisible == true)
        {
            PositionSearchOverlay();
        }
    }

    private void OnSearchPlacementFollowMouse(object sender, RoutedEventArgs e)
    {
        _searchPlacementFollowsKeyboard = false;
        _config.SearchPlacementFollowsKeyboard = false;
        _searchPlacementMode = SearchOverlayPlacementMode.MouseFollow;
        _config.SearchPlacementMode = _searchPlacementMode;
        _configService.Save(_config);
        UpdatePlacementMenuState();
        if (_searchLayerActive && _searchOverlayWindow?.IsVisible == true)
        {
            PositionSearchOverlay();
        }
    }

    private void OnSearchPlacementScreenCenter(object sender, RoutedEventArgs e)
    {
        _searchPlacementFollowsKeyboard = false;
        _config.SearchPlacementFollowsKeyboard = false;
        _searchPlacementMode = SearchOverlayPlacementMode.CursorScreenCenter;
        _config.SearchPlacementMode = _searchPlacementMode;
        _configService.Save(_config);
        UpdatePlacementMenuState();
        if (_searchLayerActive && _searchOverlayWindow?.IsVisible == true)
        {
            PositionSearchOverlay();
        }
    }

    private void OnSearchPlacementFollowKeyboard(object sender, RoutedEventArgs e)
    {
        _searchPlacementFollowsKeyboard = true;
        _config.SearchPlacementFollowsKeyboard = true;
        _configService.Save(_config);
        UpdatePlacementMenuState();
        if (_searchLayerActive && _searchOverlayWindow?.IsVisible == true)
        {
            PositionSearchOverlay();
        }
    }

    private void OnToggleEmacsNavigation(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        bool enabled = item.IsChecked;
        if (_config.EnableEmacsNavigation == enabled) return;
        _config.EnableEmacsNavigation = enabled;
        UpdateSearchOverlayNavigationModes();
        _configService.Save(_config);
    }

    private void OnToggleViNavigation(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        bool enabled = item.IsChecked;
        if (_config.EnableViNavigation == enabled) return;
        _config.EnableViNavigation = enabled;
        UpdateSearchOverlayNavigationModes();
        _configService.Save(_config);
    }

    private void OnToggleAlwaysOnTop(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        _config.AlwaysOnTop = item.IsChecked;
        ApplyTopmostState();
        _configService.Save(_config);
    }

    private void OnLanguageJapanese(object sender, RoutedEventArgs e) => SetLanguage(AppLanguage.Japanese);

    private void OnLanguageEnglish(object sender, RoutedEventArgs e) => SetLanguage(AppLanguage.English);

    private void SetLanguage(AppLanguage language)
    {
        if (_config == null)
        {
            return;
        }

        if (_config.Language == language)
        {
            UpdateLanguageMenuState();
            return;
        }

        _config.Language = language;
        _currentLanguage = language;
        ApplyLanguageToUi();
        _configService.Save(_config);
    }

    private void OnToggleHideEmptySlotNames(object sender, RoutedEventArgs e)
    {
        if (_config == null || HideEmptySlotNamesMenuItem == null) return;
        bool hide = HideEmptySlotNamesMenuItem.IsChecked;
        if (_config.HideEmptySlotNames == hide) return;
        _config.HideEmptySlotNames = hide;
        _configService.Save(_config);
        RefreshUi();
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

    private void OnToggleRemoteSessionPriority(object sender, RoutedEventArgs e)
    {
        if (_config == null || _shortcutService == null || RemoteSessionPriorityMenuItem == null) return;
        bool enabled = RemoteSessionPriorityMenuItem.IsChecked;
        if (_config.PreferRemoteSessions == enabled) return;
        _config.PreferRemoteSessions = enabled;
        _shortcutService.SetRemoteSessionPreference(enabled);
        _configService.Save(_config);
    }

    private MouseGestureOptions BuildMouseGestureOptions() =>
        new(
            _config?.EnableMouseGestures ?? MouseGestureOptions.Default.Enabled,
            _config?.MouseGestureClockwiseTurnsToShow ?? MouseGestureOptions.Default.ClockwiseTurnsToShow,
            _config?.MouseGestureCounterClockwiseTurnsToHide ?? MouseGestureOptions.Default.CounterClockwiseTurnsToHide,
            _config?.MouseGestureInvertDirections ?? MouseGestureOptions.Default.InvertDirections,
            _config?.MouseGestureRequireCtrl ?? MouseGestureOptions.Default.RequireCtrl,
            _config?.MouseGestureSuppressDuringPresentation ?? MouseGestureOptions.Default.SuppressDuringPresentation,
            _config?.MouseGestureEnforceRadiusLimit ?? MouseGestureOptions.Default.EnforceRadiusLimit,
            _config?.MouseGestureMaxRadiusPixels ?? MouseGestureOptions.Default.MaxRadiusPixels,
            _config?.MouseGestureMaxRadiusPixels ?? MouseGestureOptions.Default.MaxRadiusPixels);

    private void ApplyMouseGestureOptions()
    {
        _shortcutService?.UpdateMouseGestureOptions(BuildMouseGestureOptions());
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

    private void OnMacroModeExclusive(object sender, RoutedEventArgs e) =>
        SetMacroConcurrencyMode(MacroConcurrencyMode.Exclusive);

    private void OnMacroModeInterrupt(object sender, RoutedEventArgs e) =>
        SetMacroConcurrencyMode(MacroConcurrencyMode.Interrupt);

    private void OnMacroModeSuspend(object sender, RoutedEventArgs e) =>
        SetMacroConcurrencyMode(MacroConcurrencyMode.SuspendAndResume);

    private void SetMacroConcurrencyMode(MacroConcurrencyMode mode)
    {
        if (_config.MacroConcurrencyMode == mode) return;
        _config.MacroConcurrencyMode = mode;
        _configService.Save(_config);
        UpdateMacroModeMenu(mode);
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        UpdateContextMenuState();
    }

    private void UpdateContextMenuState()
    {
        AlwaysOnTopMenuItem.IsChecked = this.Topmost;
        if (PrefixLayerShortcutMenuItem != null)
        {
            PrefixLayerShortcutMenuItem.IsChecked = _config.EnablePrefixLayerShortcuts;
        }
        if (EmacsNavigationMenuItem != null)
        {
            EmacsNavigationMenuItem.IsChecked = _config.EnableEmacsNavigation;
        }
        if (ViNavigationMenuItem != null)
        {
            ViNavigationMenuItem.IsChecked = _config.EnableViNavigation;
        }
        if (RemoteSessionPriorityMenuItem != null)
        {
            RemoteSessionPriorityMenuItem.IsChecked = _config.PreferRemoteSessions;
        }
        if (StartupAlwaysShowMenuItem != null && StartupRestoreMenuItem != null)
        {
            StartupAlwaysShowMenuItem.IsChecked = _config.StartupBehavior == StartupWindowBehavior.AlwaysShow;
            StartupRestoreMenuItem.IsChecked = _config.StartupBehavior == StartupWindowBehavior.RestoreLastState;
        }
        if (HideEmptySlotNamesMenuItem != null)
        {
            HideEmptySlotNamesMenuItem.IsChecked = _config.HideEmptySlotNames;
        }
        UpdateMacroModeMenu(null);
        UpdatePlacementMenuState();
        PopulateSlotSizeMenu(SlotSizeMenuItem);
        UpdateTrayMenuState();
    }

    private void UpdatePlacementMenuState()
    {
        if (KeyboardPlacementFixedMenuItem != null)
        {
            KeyboardPlacementFixedMenuItem.IsChecked = _keyboardPlacementMode == WindowPlacementMode.Fixed;
        }
        if (KeyboardPlacementFollowMouseMenuItem != null)
        {
            KeyboardPlacementFollowMouseMenuItem.IsChecked = _keyboardPlacementMode == WindowPlacementMode.MouseFollow;
        }
        if (KeyboardPlacementScreenCenterMenuItem != null)
        {
            KeyboardPlacementScreenCenterMenuItem.IsChecked = _keyboardPlacementMode == WindowPlacementMode.CursorScreenCenter;
        }

        bool followKeyboard = _mousePlacementFollowsKeyboard;
        if (MousePlacementFollowKeyboardMenuItem != null)
        {
            MousePlacementFollowKeyboardMenuItem.IsChecked = followKeyboard;
        }
        if (MousePlacementFixedMenuItem != null)
        {
            MousePlacementFixedMenuItem.IsChecked = !followKeyboard && _mousePlacementMode == WindowPlacementMode.Fixed;
        }
        if (MousePlacementFollowMouseMenuItem != null)
        {
            MousePlacementFollowMouseMenuItem.IsChecked = !followKeyboard && _mousePlacementMode == WindowPlacementMode.MouseFollow;
        }
        if (MousePlacementScreenCenterMenuItem != null)
        {
            MousePlacementScreenCenterMenuItem.IsChecked = !followKeyboard && _mousePlacementMode == WindowPlacementMode.CursorScreenCenter;
        }

        if (SearchPlacementFixedMenuItem != null)
        {
            SearchPlacementFixedMenuItem.IsChecked = _searchPlacementMode == SearchOverlayPlacementMode.Fixed;
        }
        if (SearchPlacementFollowMouseMenuItem != null)
        {
            SearchPlacementFollowMouseMenuItem.IsChecked = _searchPlacementMode == SearchOverlayPlacementMode.MouseFollow && !_searchPlacementFollowsKeyboard;
        }
        if (SearchPlacementScreenCenterMenuItem != null)
        {
            SearchPlacementScreenCenterMenuItem.IsChecked = _searchPlacementMode == SearchOverlayPlacementMode.CursorScreenCenter && !_searchPlacementFollowsKeyboard;
        }
        if (SearchPlacementFollowKeyboardMenuItem != null)
        {
            SearchPlacementFollowKeyboardMenuItem.IsChecked = _searchPlacementFollowsKeyboard;
        }
    }

    private void OnConfigureMinimizeTriggers(object sender, RoutedEventArgs e)
    {
        if (_config?.Layers == null)
        {
            return;
        }

        var defaultOptions = _config.DefaultMinimizeOptions ?? SlotMinimizeOptions.CreateDefault();
        var dlg = new SlotMinimizeSettingsWindow(_config.Layers, defaultOptions) { Owner = this };
        WindowCascadeService.Arrange(dlg, this);
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        _config.DefaultMinimizeOptions = dlg.GetDefaultOptions();
        foreach (var update in dlg.GetSlotUpdates())
        {
            if (update.LayerIndex < 0 || update.LayerIndex >= _config.Layers.Count) continue;
            var layer = _config.Layers[update.LayerIndex];
            layer.Slots ??= new List<SlotModel>();
            if (update.SlotIndex < 0 || update.SlotIndex >= layer.Slots.Count) continue;
            var slot = layer.Slots[update.SlotIndex] ?? new SlotModel();
            slot.MinimizeOptions = update.Options?.Clone() ?? SlotMinimizeOptions.CreateDefault();
            layer.Slots[update.SlotIndex] = slot;
        }

        _configService.Save(_config);
        RefreshUi();
    }

    private void OnShowShortcutList(object sender, RoutedEventArgs e)
    {
        var entries = BuildShortcutListEntries();
        if (entries.Count == 0)
        {
            WpfMessageBox.Show("ショートカットが登録されたスロットはありません。", "ショートカット一覧", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_shortcutListWindow == null)
        {
            _shortcutListWindow = new SlotShortcutListWindow
            {
                Owner = this
            };
            _shortcutListWindow.Closed += (_, _) => _shortcutListWindow = null;
            WindowCascadeService.Arrange(_shortcutListWindow, this);
            _shortcutListWindow.SetEntries(entries);
            _shortcutListWindow.Show();
        }
        else
        {
            _shortcutListWindow.SetEntries(entries);
            if (_shortcutListWindow.WindowState == WindowState.Minimized)
            {
                _shortcutListWindow.WindowState = WindowState.Normal;
            }
            _shortcutListWindow.Activate();
        }
    }

    private List<SlotShortcutInfo> BuildShortcutListEntries()
    {
        var items = new List<SlotShortcutInfo>();
        if (_config?.Layers == null || _config.Layers.Count == 0)
        {
            return items;
        }

        int visibleSlots = _slotVisuals.Count > 0
            ? _slotVisuals.Count
            : Math.Max(0, _config.SlotRows * _config.SlotColumns);
        if (visibleSlots == 0)
        {
            return items;
        }

        for (int layerIndex = 0; layerIndex < _config.Layers.Count; layerIndex++)
        {
            var layer = _config.Layers[layerIndex];
            if (layer?.Slots == null || layer.Slots.Count == 0) continue;
            int maxSlots = Math.Min(visibleSlots, layer.Slots.Count);
            for (int slotIndex = 0; slotIndex < maxSlots; slotIndex++)
            {
                var slot = layer.Slots[slotIndex];
                if (slot == null) continue;
                if (string.IsNullOrWhiteSpace(slot.ShortcutKey)) continue;

                string slotId = FormatSlotDisplayName(layerIndex, slotIndex);
                string title = string.IsNullOrWhiteSpace(slot.Title)
                    ? string.Format(CultureInfo.InvariantCulture, "Slot {0}", slotIndex + 1)
                    : slot.Title!.Trim();
                string shortcut = slot.ShortcutKey.Trim();

                bool parseError = !ShortcutSequenceParser.TryParse(shortcut, out var sequence, out _);
                string normalized = parseError ? string.Empty : sequence.NormalizedString;
                var segments = parseError
                    ? Array.Empty<string>()
                    : sequence.Chords.Select(c => c.NormalizedString).ToArray();
                items.Add(new SlotShortcutInfo(slotId, title, shortcut, normalized, segments, parseError, layerIndex, slotIndex));
            }
        }

        var conflictGroups = items
            .Where(item => !item.HasParseError && !string.IsNullOrEmpty(item.NormalizedKey))
            .GroupBy(item => item.NormalizedKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);
        foreach (var conflictEntry in conflictGroups.SelectMany(g => g))
        {
            conflictEntry.HasConflict = true;
        }

        var validForShadow = items
            .Where(item => !item.HasParseError && item.NormalizedSegments.Length > 0)
            .ToList();
        for (int i = 0; i < validForShadow.Count; i++)
        {
            var shorter = validForShadow[i];
            for (int j = 0; j < validForShadow.Count; j++)
            {
                if (i == j) continue;
                var longer = validForShadow[j];
                if (shorter.NormalizedSegments.Length >= longer.NormalizedSegments.Length) continue;
                if (IsPrefix(shorter.NormalizedSegments, longer.NormalizedSegments))
                {
                    longer.IsShadowed = true;
                }
            }
        }

        static bool IsPrefix(string[] shorter, string[] longer)
        {
            if (shorter.Length == 0 || longer.Length == 0) return false;
            if (shorter.Length >= longer.Length) return false;
            for (int idx = 0; idx < shorter.Length; idx++)
            {
                if (!string.Equals(shorter[idx], longer[idx], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        return items;
    }

    private void UpdateMacroModeMenu(MacroConcurrencyMode? overrideMode)
    {
        var current = overrideMode ?? _config.MacroConcurrencyMode;
        if (MacroModeExclusiveMenuItem != null)
        {
            MacroModeExclusiveMenuItem.IsChecked = current == MacroConcurrencyMode.Exclusive;
        }
        if (MacroModeInterruptMenuItem != null)
        {
            MacroModeInterruptMenuItem.IsChecked = current == MacroConcurrencyMode.Interrupt;
        }
        if (MacroModeSuspendMenuItem != null)
        {
            MacroModeSuspendMenuItem.IsChecked = current == MacroConcurrencyMode.SuspendAndResume;
        }
    }

    private void OnMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (_config?.Layers == null || _config.Layers.Count == 0)
        {
            return;
        }

        ChangeLayer(e.Delta > 0 ? -1 : 1);
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
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop) && !IsSlotLayoutDrag(e))
        {
            return;
        }

        if (sender is FrameworkElement fe)
        {
            switch (fe.Tag)
            {
                case int idx:
                    _hoverTargetLayer = idx;
                    _hoverNavigationDirection = 0;
                    break;
                case string s when s == "prev":
                    _hoverTargetLayer = -1;
                    _hoverNavigationDirection = -1;
                    break;
                case string s when s == "next":
                    _hoverTargetLayer = -1;
                    _hoverNavigationDirection = 1;
                    break;
                default:
                    _hoverTargetLayer = -1;
                    _hoverNavigationDirection = 0;
                    break;
            }
        }
        _layerHoverTimer.Stop();
        _layerHoverTimer.Start();
        e.Effects = IsSlotLayoutDrag(e) ? WpfDragDropEffects.Move : WpfDragDropEffects.Link;
        e.Handled = true;
    }

    private void OnLayerDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop) && !IsSlotLayoutDrag(e))
        {
            return;
        }

        if (sender is FrameworkElement fe)
        {
            switch (fe.Tag)
            {
                case int idx:
                    _hoverTargetLayer = idx;
                    _hoverNavigationDirection = 0;
                    break;
                case string s when s == "prev":
                    _hoverTargetLayer = -1;
                    _hoverNavigationDirection = -1;
                    break;
                case string s when s == "next":
                    _hoverTargetLayer = -1;
                    _hoverNavigationDirection = 1;
                    break;
                default:
                    _hoverTargetLayer = -1;
                    _hoverNavigationDirection = 0;
                    break;
            }
        }
        if (!_layerHoverTimer.IsEnabled) _layerHoverTimer.Start();
        e.Effects = IsSlotLayoutDrag(e) ? WpfDragDropEffects.Move : WpfDragDropEffects.Link;
        e.Handled = true;
    }

    private void OnLayerDragLeave(object sender, DragEventArgs e)
    {
        _hoverTargetLayer = -1;
        _hoverNavigationDirection = 0;
        _layerHoverTimer.Stop();
        e.Handled = true;
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
        if (_isSlotLayoutEditMode)
        {
            _slotDragStartPoint = null;
        }
    }
    private void OnSlotMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border b) return;
        if (_isSlotLayoutEditMode)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                _slotDragStartPoint = e.GetPosition(null);
            }
            return;
        }

        int idx = GetSlotIndex(b);
        if (GetSlotMacroState(idx) != SlotMacroState.Idle) return;
        b.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(48, 48, 48));
        b.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(200, 200, 200));
    }

    private void OnSlotDragEnter(object sender, DragEventArgs e)
    {
        if (sender is not Border b) return;
        if (IsSlotLayoutDrag(e))
        {
            e.Handled = true;
            e.Effects = WpfDragDropEffects.Move;
            b.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(40, 48, 56));
            b.BorderBrush = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(0x7C, 0xFF, 0xB0));
            UpdateSlotLayoutPreview(_currentLayer, GetSlotIndex(b));
            return;
        }
        if (TryHandleEditModeDropPreview(b, e))
        {
            return;
        }
        int idx = GetSlotIndex(b);
        if (GetSlotMacroState(idx) != SlotMacroState.Idle) return;
        b.Background = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(48, 48, 48));
        b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
    }
    private void OnSlotDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not Border b) return;
        if (IsSlotLayoutDrag(e))
        {
            e.Handled = true;
            int idx = GetSlotIndex(b);
            if (_slotLayoutPreviewTargetLayer == _currentLayer && _slotLayoutPreviewTargetIndex == idx)
            {
                HideSlotDragPreview();
            }
            RenderSlotMacroState(idx, GetSlotMacroState(idx));
            return;
        }
        OnSlotMouseLeave(sender, null!);
    }
    private void OnSlotDragOver(object sender, DragEventArgs e)
    {
        if (IsSlotLayoutDrag(e))
        {
            e.Handled = true;
            e.Effects = WpfDragDropEffects.Move;
            if (sender is Border b)
            {
                UpdateSlotLayoutPreview(_currentLayer, GetSlotIndex(b));
            }
            return;
        }

        if (sender is FrameworkElement fe && TryHandleEditModeDropPreview(fe, e))
        {
            return;
        }

        if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            e.Handled = true;
            e.Effects = WpfDragDropEffects.Link;
        }
    }

    private void OnSlotMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isSlotLayoutEditMode || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        if (_slotDragStartPoint == null)
        {
            return;
        }

        var current = e.GetPosition(null);
        var start = _slotDragStartPoint.Value;
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _slotDragStartPoint = null;
        TryBeginSlotLayoutDrag(sender as FrameworkElement);
    }

    private Task TriggerVisibleSlotAsync(int displayIndex, SlotTriggerKind trigger, IReadOnlyList<string>? droppedPaths = null)
    {
        if (!TryGetVisibleSlot(displayIndex, out var layerIndex, out var slotIndex))
        {
            return Task.CompletedTask;
        }

        return TriggerSlotAsync(layerIndex, slotIndex, trigger, droppedPaths);
    }

    private async Task TriggerSlotAsync(int layerIndex, int slotIndex, SlotTriggerKind trigger, IReadOnlyList<string>? droppedPaths = null)
    {
        if (slotIndex < 0 || slotIndex >= _slotVisuals.Count) return;
        var layer = _config.Layers[layerIndex];
        var slot = layer.Slots[slotIndex];
        if (trigger == SlotTriggerKind.Click && !slot.ClickEnabled) return;

        var slotTitle = slot.Title?.ReplaceLineEndings(" ").Trim() ?? string.Empty;
        if (slotTitle.Length == 0)
        {
            slotTitle = "(untitled)";
        }
        var dropPathArray = droppedPaths switch
        {
            null => null,
            string[] existing => existing,
            _ => droppedPaths.ToArray()
        };
        var dropPathsOrEmpty = dropPathArray ?? Array.Empty<string>();
        var mode = slot.ExecutionMode;
        var script = slot.KeyboardMacroScript ?? string.Empty;
        var macroConfigured = !string.IsNullOrWhiteSpace(script);
        var commandConfigured = !string.IsNullOrWhiteSpace(slot.Command);
        _logger.Info($"Trigger requested (layer={layerIndex + 1}, slot={slotIndex + 1}, title=\"{slotTitle}\", source={trigger}, mode={mode}, macroConfigured={macroConfigured}, commandConfigured={commandConfigured})");

        if (!macroConfigured && !commandConfigured) return;

        bool shouldRunMacro = mode != SlotExecutionMode.Command && macroConfigured;

        IAsyncDisposable? suspension = null;
        SlotRunContext? pausedContext = null;
        try
        {
            if (_macroService.IsMacroRunning)
            {
                if (shouldRunMacro && IsSlotCurrentlyRunning(layerIndex, slotIndex))
                {
                    if (_macroService.CancelCurrentMacro())
                    {
                        _logger.Info($"Requested cancel for running macro (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}).");
                        MarkSlotMacroCanceling(layerIndex, slotIndex);
                    }
                    return;
                }

                if (shouldRunMacro)
                {
                    switch (_config.MacroConcurrencyMode)
                    {
                        case MacroConcurrencyMode.Exclusive:
                            if (IsSlotCurrentlyRunning(layerIndex, slotIndex))
                            {
                                if (_macroService.CancelCurrentMacro())
                                {
                                    _logger.Info($"Requested cancel for running macro (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}).");
                                    MarkSlotMacroCanceling(layerIndex, slotIndex);
                                }
                            }
                            else
                            {
                                _logger.Warn($"Rejected trigger while another macro is running (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}).");
                                WpfMessageBox.Show("別のスロットのマクロが実行中です。完了または停止してから再度実行してください。", "Macro Running", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            return;
                        case MacroConcurrencyMode.Interrupt:
                            _logger.Info($"Interrupting running macro before executing slot (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}).");
                            if (_currentSlotRun != null)
                            {
                                MarkSlotMacroCanceling(_currentSlotRun.LayerIndex, _currentSlotRun.SlotIndex);
                            }
                            await _macroService.CancelAllRunningMacrosAsync(CancellationToken.None);
                            break;
                        case MacroConcurrencyMode.SuspendAndResume:
                            pausedContext = _currentSlotRun;
                            SetSlotPaused(pausedContext, true);
                            try
                            {
                                suspension = await _macroService.SuspendCurrentMacroAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn($"Failed to suspend macro for nested execution (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}): {ex}");
                                suspension = null;
                            }

                            if (suspension == null)
                            {
                                SetSlotPaused(pausedContext, false);
                                pausedContext = null;
                                WpfMessageBox.Show("現在のマクロを一時停止できませんでした。実行中のマクロが落ち着くまで少し待ってから再度実行してください。", "Macro Busy", MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            break;
                    }
                }
                else
                {
                    _logger.Info($"Command-only slot triggered while macro is active (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}).");
                }
            }

        MacroExecutionContext? macroContext = null;
        if (mode == SlotExecutionMode.MacroScriptExtended && commandConfigured)
        {
            var contextTitle = slotTitle;
            macroContext = new MacroExecutionContext(
                SlotExecutionMode.MacroScriptExtended,
                (overrideArgs, overrideCommandPath) =>
                {
                    var effectiveCommand = string.IsNullOrWhiteSpace(overrideCommandPath)
                        ? slot.Command
                        : overrideCommandPath;
                    var slotOverride = new SlotModel
                    {
                        Title = slot.Title,
                        Command = effectiveCommand,
                        ArgumentsTemplate = slot.ArgumentsTemplate,
                        IconPath = slot.IconPath,
                        ClickEnabled = slot.ClickEnabled,
                        ShortcutKey = slot.ShortcutKey,
                        KeyboardMacroScript = slot.KeyboardMacroScript,
                        ExecutionMode = slot.ExecutionMode,
                        AccentColor = slot.AccentColor,
                        MinimizeOptions = slot.MinimizeOptions,
                        SearchKeywords = slot.SearchKeywords
                    };
                    var launchResult = _launcher.Launch(slotOverride, dropPathsOrEmpty, overrideArgs);
                    if (!launchResult.Success)
                    {
                        _logger.Warn($"Command launch failed via macro (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}): {launchResult.Message}");
                    }
                    return launchResult;
                },
                contextTitle,
                slot.Command ?? string.Empty,
                dropPathsOrEmpty);
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
                        _logger.Info($"Macro canceled (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}).");
                    }
                    else
                    {
                        _logger.Warn($"Macro failed (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}): {macroResult.Message}");
                    }
                    if (!macroResult.IsCanceled)
                    {
                        WpfMessageBox.Show(macroResult.Message, "Macro Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return;
                }
                _logger.Info($"Macro completed (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}).");
                MaybeMinimizeAfterSlot(slot, trigger, macroExecuted: true);
            }
            catch (Exception ex)
            {
                _logger.Error($"Macro execution failed (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}): {ex}");
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
            var result = _launcher.Launch(slot, dropPathsOrEmpty);
            if (!result.Success)
            {
                _logger.Warn($"Command launch failed (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}): {result.Message}");
                WpfMessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                _logger.Info($"Command launch succeeded (layer={layerIndex + 1}, slot={slotIndex + 1}, source={trigger}).");
                MaybeMinimizeAfterSlot(slot, trigger, macroExecuted: false);
            }
        }
        }
        finally
        {
            if (suspension != null)
            {
                await ResumeSuspendedMacroScopeAsync(suspension, pausedContext);
            }

            if (!_macroService.IsMacroRunning && HasAnyRunningSlot())
            {
                _logger.Warn("Macro state mismatch detected. Clearing slot run context.");
                ForceClearAllSlotStates();
            }
        }
    }

    private void MaybeMinimizeAfterSlot(SlotModel slot, SlotTriggerKind trigger, bool macroExecuted)
    {
        var options = slot.MinimizeOptions ?? SlotMinimizeOptions.CreateDefault();
        if (!options.ShouldMinimizeAfter(trigger))
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(MinimizeWindowToTray));
    }

    private async void OnSlotClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        _slotDragStartPoint = null;
        if (_isSlotLayoutEditMode)
        {
            return;
        }
        if (_keyboardNavigationActive)
        {
            DeactivateKeyboardNavigation();
        }
        int idx = GetSlotIndex(fe);
        await TriggerVisibleSlotAsync(idx, SlotTriggerKind.Click);
    }

    private void OnShortcutTriggered(object? sender, ShortcutTriggeredEventArgs e)
    {
        var normalized = e.RegisteredText;
        foreach (var binding in _shortcutBindings)
        {
            if (string.Equals(binding.NormalizedKey, normalized, StringComparison.Ordinal))
            {
                if (binding.LayerIndex != _currentLayer)
                {
                    SetLayer(binding.LayerIndex);
                }
                _ = TriggerSlotAsync(binding.LayerIndex, binding.SlotIndex, SlotTriggerKind.Shortcut);
                break;
            }
        }
    }

    private void OnPrefixPassthroughRequested(object? sender, PrefixPassthroughEventArgs e)
    {
        _ = SendPrefixPassthroughAsync(e.ShortcutText);
    }

    private bool IsWindowHiddenForShow() => _isMinimizedToTray || !IsVisible || WindowState == WindowState.Minimized;

    private static int? NormalizeShowLayerPreference(int value) => value < 0 ? null : value;

    private void PositionWindowAtFixedLocation()
    {
        var rect = GetWindowRect(this.Left, this.Top);
        double targetLeft = _config.WindowLeft ?? this.Left;
        double targetTop = _config.WindowTop ?? this.Top;
        var bounds = ScreenBoundsResolver.ForRect(this, new Rect(targetLeft, targetTop, rect.Width, rect.Height));
        var (left, top) = _placement.Clamp(targetLeft, targetTop, bounds, rect.Width, rect.Height);
        Left = left;
        Top = top;
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (_blockLocationSave)
        {
            _blockLocationSave = false;
            return;
        }

        if (_config == null || _isMinimizedToTray || _suppressFixedCapture || _suppressFixedCaptureDuringSearch)
        {
            return;
        }

        if (GetPlacementMode(ShowLayerTrigger.Prefix) != WindowPlacementMode.Fixed)
        {
            return;
        }

        _config.WindowLeft = this.Left;
        _config.WindowTop = this.Top;
        _pendingPositionSave = true;
        _positionSaveTimer.Stop();
        _positionSaveTimer.Start();
    }

    private void OnPositionSaveTick(object? sender, EventArgs e)
    {
        _positionSaveTimer.Stop();
        if (_pendingPositionSave && _config != null)
        {
            _pendingPositionSave = false;
            _configService.Save(_config);
        }
    }

    private void ApplyShowLayerPreference(ShowLayerTrigger trigger, bool fromHidden)
    {
        if (_config?.Layers == null || _config.Layers.Count == 0)
        {
            return;
        }

        int configured = trigger switch
        {
            ShowLayerTrigger.MouseGesture => fromHidden ? _config.MouseGestureShowLayerWhenHidden : _config.MouseGestureShowLayerWhenVisible,
            ShowLayerTrigger.Prefix => fromHidden ? _config.PrefixShowLayerWhenHidden : _config.PrefixShowLayerWhenVisible,
            _ => -1
        };

        var desired = NormalizeShowLayerPreference(configured);
        if (!desired.HasValue)
        {
            return;
        }

        int clamped = Math.Clamp(desired.Value, 0, _config.Layers.Count - 1);
        if (clamped != _currentLayer)
        {
            SetLayer(clamped);
        }
    }

    private void OnPrefixActivationRequested(object? sender, EventArgs e)
    {
        bool wasHidden = IsWindowHiddenForShow();
        ApplyShowLayerPreference(ShowLayerTrigger.Prefix, wasHidden);
        var placement = GetPlacementMode(ShowLayerTrigger.Prefix);
        _suppressFixedCapture = placement != WindowPlacementMode.Fixed;
        switch (placement)
        {
            case WindowPlacementMode.MouseFollow:
                PositionWindowAtMouse();
                break;
            case WindowPlacementMode.CursorScreenCenter:
                PositionWindowAtCursorScreenCenter();
                break;
            case WindowPlacementMode.Fixed:
            default:
                _suppressFixedCapture = false;
                PositionWindowAtFixedLocation();
                break;
        }
        BringWindowToForeground();
        RearmDragDropTargets();
        ActivateKeyboardNavigation();
        ShowLayerNameOverlay();
        RefreshDragHoverIfMouseButtonDown();
    }

    private void OnPrefixSearchRequested(object? sender, EventArgs e)
    {
        CapturePrefixSearchRestoreContext();
        _suppressFixedCaptureDuringSearch = true;
        _applySearchPlacementForCurrentSearch = true;
        bool wasHidden = IsWindowHiddenForShow();
        ApplyShowLayerPreference(ShowLayerTrigger.Prefix, wasHidden);
        ApplySearchPlacement();
        BringWindowToForeground();
        RearmDragDropTargets();
        OpenSearchLayer();
        RefreshDragHoverIfMouseButtonDown();
    }

    private void ApplySearchPlacement()
    {
        try
        {
            var effectiveMode = GetEffectiveSearchPlacementMode();
            switch (effectiveMode)
            {
                case SearchOverlayPlacementMode.MouseFollow:
                    _suppressFixedCapture = true;
                    PositionWindowAtMouse();
                    break;
                case SearchOverlayPlacementMode.CursorScreenCenter:
                    _suppressFixedCapture = true;
                    PositionWindowAtCursorScreenCenter();
                    break;
                case SearchOverlayPlacementMode.Fixed:
                default:
                    var placement = GetPlacementMode(ShowLayerTrigger.Prefix);
                    _suppressFixedCapture = placement != WindowPlacementMode.Fixed;
                    switch (placement)
                    {
                        case WindowPlacementMode.MouseFollow:
                            PositionWindowAtMouse();
                            break;
                        case WindowPlacementMode.CursorScreenCenter:
                            PositionWindowAtCursorScreenCenter();
                            break;
                        case WindowPlacementMode.Fixed:
                        default:
                            _suppressFixedCapture = false;
                            PositionWindowAtFixedLocation();
                            break;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to apply search placement: {ex.Message}");
        }
    }

    private void OnSearchHotkeyRequested(object? sender, EventArgs e)
    {
        CapturePrefixSearchRestoreContext();
        _suppressFixedCaptureDuringSearch = true;
        _applySearchPlacementForCurrentSearch = true;
        bool wasHidden = IsWindowHiddenForShow();
        ApplyShowLayerPreference(ShowLayerTrigger.Prefix, wasHidden);
        ApplySearchPlacement();
        BringWindowToForeground();
        RearmDragDropTargets();
        OpenSearchLayer();
        RefreshDragHoverIfMouseButtonDown();
    }

    private void OnPrefixMinimizeRequested(object? sender, EventArgs e)
    {
        HideLayerNameOverlayImmediate();
        MinimizeWindowToTray();
    }

    private void OnMouseGestureShowRequested(object? sender, EventArgs e)
    {
        HideLayerNameOverlayImmediate();
        bool wasHidden = IsWindowHiddenForShow();
        ApplyShowLayerPreference(ShowLayerTrigger.MouseGesture, wasHidden);
        bool isDrag = IsAnyMouseButtonPressed();
        if (isDrag)
        {
            PositionWindowNearCursorForDragGesture();
        }
        var placement = GetPlacementMode(ShowLayerTrigger.MouseGesture);
        _suppressFixedCapture = placement != WindowPlacementMode.Fixed;
        if (isDrag)
        {
            // already positioned above
        }
        else
        {
            switch (placement)
            {
                case WindowPlacementMode.MouseFollow:
                    PositionWindowAtMouse();
                    break;
                case WindowPlacementMode.CursorScreenCenter:
                    PositionWindowAtCursorScreenCenter();
                    break;
                case WindowPlacementMode.Fixed:
                default:
                    _suppressFixedCapture = false;
                    PositionWindowAtFixedLocation();
                    break;
            }
        }
        BringWindowToForeground();
        RearmDragDropTargets();
        ActivateKeyboardNavigation();
        ShowLayerNameOverlay();
        RefreshDragHoverIfMouseButtonDown();
    }

    private void OnMouseGestureHideRequested(object? sender, EventArgs e)
    {
        HideLayerNameOverlayImmediate();
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
            if (_currentSlotRun != null)
            {
                MarkSlotMacroCanceling(_currentSlotRun.LayerIndex, _currentSlotRun.SlotIndex);
            }
        }
        else
        {
            _logger.Warn("Prefix cancel macro shortcut could not cancel the current macro.");
        }
    }

    private void OnPrefixPositionToggleRequested(object? sender, EventArgs e)
    {
        TogglePlacementMode(ShowLayerTrigger.Prefix);
    }

    // When the window is summoned during a drag, force a minimal cursor move so WPF delivers DragEnter/DragOver immediately.
    private void RefreshDragHoverIfMouseButtonDown()
    {
        if (!IsAnyMouseButtonPressed())
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(SendMinimalDragHoverPulse));
        SendMinimalDragHoverPulse();
    }

    private static bool IsAnyMouseButtonPressed()
    {
        return NativeMethods.IsKeyPressed(NativeMethods.VK_LBUTTON)
               || NativeMethods.IsKeyPressed(NativeMethods.VK_RBUTTON)
               || NativeMethods.IsKeyPressed(NativeMethods.VK_MBUTTON)
               || NativeMethods.IsKeyPressed(NativeMethods.VK_XBUTTON1)
               || NativeMethods.IsKeyPressed(NativeMethods.VK_XBUTTON2);
    }

    private void RearmDragDropTargets()
    {
        bool allow = AllowDrop;
        AllowDrop = false;
        AllowDrop = allow;

        foreach (var slot in _slotVisuals)
        {
            slot.Border.AllowDrop = false;
            slot.Border.AllowDrop = true;
        }
    }

    private void SendMinimalDragHoverPulse()
    {
        if (!IsAnyMouseButtonPressed())
        {
            return;
        }

        // Zero or 1px wiggle to force drag hit testing without moving far.
        if (!NativeMethods.SendMouseMoveRelative(0, 0))
        {
            NativeMethods.MouseEventMove(0, 0);
        }
        if (!NativeMethods.SendMouseMoveRelative(1, 0))
        {
            NativeMethods.MouseEventMove(1, 0);
        }
        if (!NativeMethods.SendMouseMoveRelative(-1, 0))
        {
            NativeMethods.MouseEventMove(-1, 0);
        }
    }

    private void PositionWindowNearCursorForDragGesture()
    {
        if (!TryGetCursorPosition(out var cursor))
        {
            return;
        }

        var logicalCursor = ConvertScreenToDeviceIndependent(new System.Windows.Point(cursor.X, cursor.Y));
        var rect = GetWindowRect();
        double desiredLeft = logicalCursor.X - rect.Width / 2.0;
        const double offset = 16;

        var bounds = ScreenBoundsResolver.ForRect(this, new Rect(desiredLeft, logicalCursor.Y, rect.Width, rect.Height));
        double spaceBelow = bounds.Bottom - (logicalCursor.Y + offset);
        double spaceAbove = (logicalCursor.Y - offset) - bounds.Top;
        bool preferBelow = spaceBelow >= rect.Height || spaceBelow >= spaceAbove;
        double desiredTop = preferBelow
            ? logicalCursor.Y + offset
            : logicalCursor.Y - offset - rect.Height;

        var (left, top) = _placement.Clamp(desiredLeft, desiredTop, bounds, rect.Width, rect.Height);
        Left = left;
        Top = top;
    }

    private System.Windows.Point ConvertScreenToDeviceIndependent(System.Windows.Point screenPoint)
    {
        var source = PresentationSource.FromVisual(this);
        var matrix = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        return matrix.Transform(screenPoint);
    }

    private static bool TryGetCursorPosition(out DrawingPoint point)
    {
        if (NativeMethods.GetCursorPos(out var nativePoint))
        {
            point = new DrawingPoint(nativePoint.X, nativePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    private void OnPrefixNextLayerRequested(object? sender, EventArgs e)
    {
        ChangeLayer(1);
    }

    private void OnPrefixPreviousLayerRequested(object? sender, EventArgs e)
    {
        ChangeLayer(-1);
    }

    private void OnPrefixStateChanged(object? sender, PrefixStateChangedEventArgs e)
    {
        UpdatePrefixGuard(e.IsArmed);

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
        if (_isSlotLayoutEditMode)
        {
            UpdateSlotPanelEditModePadding();
        }
    }

    private void UpdatePrefixGuard(bool isArmed)
    {
        if (isArmed)
        {
            _prefixGuardToken++;
            _suppressLayerSelectionForPrefix = true;
            return;
        }

        var token = ++_prefixGuardToken;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_prefixGuardToken == token)
            {
                _suppressLayerSelectionForPrefix = false;
            }
        }), DispatcherPriority.Background);
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
        if (_config?.Layers == null || _config.Layers.Count == 0)
        {
            return;
        }

        _currentLayer = Math.Clamp(_currentLayer, 0, _config.Layers.Count - 1);
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";

        if (_searchLayerActive)
        {
            RebuildSearchResults();
        }
        RefreshVisibleSlotMappings();
        int baseNo = _searchLayerActive ? 0 : _currentLayer * _slotVisuals.Count;
        for (int i = 0; i < _slotVisuals.Count; i++)
        {
            if (!TryGetVisibleSlotModel(i, out var layerIndex, out var slotIndex, out var slot))
            {
                RenderEmptySlot(i);
                continue;
            }

            string title = string.IsNullOrWhiteSpace(slot.Title)
                ? GetEmptySlotTitle(slot, baseNo + i)
                : slot.Title;
            var visual = _slotVisuals[i];
            visual.Title.Text = title;
            visual.DragPreviewHost.Visibility = Visibility.Collapsed;
            ApplySlotColor(i);
        }
        // Layer button highlight with stronger contrast
        UpdateLayerButtonVisuals();
        UpdateAllSlotMacroStates();
        if (_keyboardNavigationActive)
        {
            NormalizeKeyboardSelectionIndex();
            UpdateKeyboardSelectionVisual();
        }
        UpdateShortcutRegistrations();
    }

    private string GetEmptySlotTitle(SlotModel slot, int displayNumberZeroBased)
    {
        bool hideEmptyNames = _config?.HideEmptySlotNames ?? false;
        if (hideEmptyNames && IsSlotEmpty(slot))
        {
            return string.Empty;
        }
        return $"Slot {displayNumberZeroBased + 1}";
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
                if (!ShortcutSequenceParser.TryParse(trimmed, out var sequence, out var error))
                {
                    _logger.Warn($"ショートカット設定の解析に失敗しました (Layer={layerIndex + 1}, Slot={slotIndex + 1}): {error}");
                    continue;
                }
                orderedBindings.Add(new ShortcutBinding(sequence.NormalizedString, layerIndex, slotIndex));
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
        if (_layerButtons == null || _config?.Layers == null || _config.Layers.Count == 0)
        {
            return;
        }

        var models = BuildLayerButtonModels(_config.Layers.Count, Math.Clamp(_currentLayer, 0, _config.Layers.Count - 1));
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

        for (int i = 0; i < _layerButtons.Count; i++)
        {
            var button = _layerButtons[i];
            var model = models[i];
            button.Visibility = model.Visible ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = model.Visible;
            button.Content = model.Content;
            button.Tag = model.Tag;
            button.ToolTip = model.ToolTip;
            // ドラッグ中のホバーでのレイヤー切替に使うため、矢印でも AllowDrop を有効化する
            button.AllowDrop = true;
            bool active = model.IsLayer && model.LayerIndex == _currentLayer;
            SetState(button, active);
        }
    }

    private IReadOnlyList<LayerButtonModel> BuildLayerButtonModels(int totalLayers, int currentLayer)
    {
        int buttonCount = _layerButtons?.Count ?? 4;
        var models = new List<LayerButtonModel>(buttonCount);
        if (totalLayers <= 0)
        {
            while (models.Count < buttonCount)
            {
                models.Add(LayerButtonModel.Hidden);
            }
            return models;
        }

        if (totalLayers <= buttonCount)
        {
            for (int i = 0; i < buttonCount; i++)
            {
                models.Add(i < totalLayers ? LayerButtonModel.Layer(i) : LayerButtonModel.Hidden);
            }
            return models;
        }

        int numericCount = Math.Min(3, buttonCount);
        int start = Math.Clamp(currentLayer - 1, 0, Math.Max(0, totalLayers - numericCount));
        int end = start + numericCount - 1;
        bool leftHidden = start > 0;
        bool rightHidden = end < totalLayers - 1;

        if (leftHidden && rightHidden)
        {
            numericCount = 2;
            start = _lastLayerNavigationDirection switch
            {
                > 0 => Math.Clamp(currentLayer - 1, 1, totalLayers - numericCount - 1),
                < 0 => Math.Clamp(currentLayer, 1, totalLayers - numericCount - 1),
                _ => Math.Clamp(currentLayer, 1, totalLayers - numericCount - 1)
            };
            end = start + numericCount - 1;
            leftHidden = start > 0;
            rightHidden = end < totalLayers - 1;
        }

        if (leftHidden)
        {
            models.Add(LayerButtonModel.Arrow("◀", "prev", "前のレイヤーへ"));
        }

        for (int i = 0; i < numericCount && models.Count < buttonCount; i++)
        {
            int layerIndex = start + i;
            if (layerIndex >= totalLayers)
            {
                break;
            }
            models.Add(LayerButtonModel.Layer(layerIndex));
        }

        if (rightHidden && models.Count < buttonCount)
        {
            models.Add(LayerButtonModel.Arrow("▶", "next", "次のレイヤーへ"));
        }

        while (models.Count < buttonCount)
        {
            models.Add(LayerButtonModel.Hidden);
        }

        return models;
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
        _shortcutService.PrefixNextLayerRequested -= OnPrefixNextLayerRequested;
        _shortcutService.PrefixPreviousLayerRequested -= OnPrefixPreviousLayerRequested;
        _shortcutService.SearchHotkeyRequested -= OnSearchHotkeyRequested;
        _shortcutService.PrefixSearchRequested -= OnPrefixSearchRequested;
        _shortcutService.MouseGestureShowRequested -= OnMouseGestureShowRequested;
        _shortcutService.MouseGestureHideRequested -= OnMouseGestureHideRequested;
        _shortcutService.PrefixStateChanged -= OnPrefixStateChanged;
        SystemEvents.SessionSwitch -= OnSystemSessionSwitch;
        _shortcutService.Dispose();
        _macroService.Dispose();
        if (_searchOverlayWindow != null)
        {
            _searchOverlayWindow.SearchTextChanged -= OnSearchOverlayTextChanged;
            _searchOverlayWindow.CancelRequested -= OnSearchOverlayCancelRequested;
            _searchOverlayWindow.SlotNavigationRequested -= OnSearchOverlaySlotNavigationRequested;
            _searchOverlayWindow.Close();
            _searchOverlayWindow = null;
        }
    }

    private void OnSystemSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason != SessionSwitchReason.SessionLock)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                bool canceled = await _macroService.CancelAllRunningMacrosAsync(cts.Token).ConfigureAwait(true);
                if (canceled)
                {
                    _logger.Info("Canceled running macros because session was locked.");
                }
            }
            catch (OperationCanceledException)
            {
                // ignore cancellation
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to cancel macros on session lock: {ex.Message}");
            }
        }), DispatcherPriority.Background);
    }

    private void TogglePlacementMode(ShowLayerTrigger trigger)
    {
        var current = GetPlacementMode(trigger);
        var next = current switch
        {
            WindowPlacementMode.Fixed => WindowPlacementMode.MouseFollow,
            WindowPlacementMode.MouseFollow => WindowPlacementMode.CursorScreenCenter,
            _ => WindowPlacementMode.Fixed
        };
        SetPlacementMode(next, trigger, initiatedByToggle: true);
    }

    private WindowPlacementMode GetPlacementMode(ShowLayerTrigger trigger)
    {
        return trigger switch
        {
            ShowLayerTrigger.MouseGesture when _mousePlacementFollowsKeyboard => _keyboardPlacementMode,
            ShowLayerTrigger.MouseGesture => _mousePlacementMode,
            _ => _keyboardPlacementMode
        };
    }

    private void SetPlacementMode(WindowPlacementMode mode, ShowLayerTrigger trigger, bool initiatedByToggle)
    {
        bool isFixed = mode == WindowPlacementMode.Fixed;
        if (trigger == ShowLayerTrigger.Prefix)
        {
            if (_keyboardPlacementMode == mode)
            {
                if (!isFixed && initiatedByToggle)
                {
                    PositionWindowForMode(mode);
                }
            }
            else
            {
                _keyboardPlacementMode = mode;
                _config.WindowPlacementMode = mode;
                _config.KeyboardPlacementMode = mode;
                if (initiatedByToggle)
                {
                    _blockLocationSave = true;
                    if (isFixed)
                    {
                        RestoreWindowPosition();
                    }
                    else
                    {
                        PositionWindowForMode(mode);
                    }
                }
                _suppressFixedCapture = !isFixed;
            }

            if (_mousePlacementFollowsKeyboard)
            {
                _mousePlacementMode = _keyboardPlacementMode;
                _config.MousePlacementMode = _mousePlacementMode;
                _config.MousePlacementFollowsKeyboard = true;
            }
        }
        else
        {
            if (_mousePlacementFollowsKeyboard)
            {
                _mousePlacementMode = _keyboardPlacementMode;
                _config.MousePlacementMode = _mousePlacementMode;
                _config.MousePlacementFollowsKeyboard = true;
                return;
            }

            if (_mousePlacementMode == mode)
            {
                if (!isFixed && initiatedByToggle)
                {
                    PositionWindowForMode(mode);
                }
                return;
            }

            _mousePlacementMode = mode;
            _config.MousePlacementMode = mode;
            if (initiatedByToggle)
            {
                _blockLocationSave = true;
                if (isFixed)
                {
                    RestoreWindowPosition();
                }
                else
                {
                    PositionWindowForMode(mode);
                }
            }
            _suppressFixedCapture = !isFixed;
        }

        _configService.Save(_config);
    }

    private void PositionWindowForMode(WindowPlacementMode mode)
    {
        switch (mode)
        {
            case WindowPlacementMode.MouseFollow:
                PositionWindowAtMouse();
                break;
            case WindowPlacementMode.CursorScreenCenter:
                PositionWindowAtCursorScreenCenter();
                break;
            case WindowPlacementMode.Fixed:
            default:
                PositionWindowAtFixedLocation();
                break;
        }
    }

    private void CaptureFixedWindowPosition(bool clampBeforeStoring)
    {
        if (_suppressFixedCapture)
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

    private void PositionWindowAtCursorScreenCenter()
    {
        try
        {
            var cursor = Forms.Control.MousePosition;
            var bounds = ScreenBoundsResolver.ForPoint(cursor.X, cursor.Y);
            var rect = GetWindowRect(this.Left, this.Top);
            double newLeft = bounds.Left + (bounds.Width - rect.Width) / 2.0;
            double newTop = bounds.Top + (bounds.Height - rect.Height) / 2.0;
            var (left, top) = _placement.Clamp(newLeft, newTop, bounds, rect.Width, rect.Height);
            Left = left;
            Top = top;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to position window at screen center: {ex.Message}");
        }
    }

    private SearchOverlayPlacementMode GetEffectiveSearchPlacementMode()
    {
        if (_searchPlacementFollowsKeyboard)
        {
            return MapWindowPlacementToSearch(_keyboardPlacementMode);
        }

        if (!Enum.IsDefined(typeof(SearchOverlayPlacementMode), (int)_searchPlacementMode))
        {
            return SearchOverlayPlacementMode.Fixed;
        }

        return _searchPlacementMode;
    }

    private static SearchOverlayPlacementMode MapWindowPlacementToSearch(WindowPlacementMode mode)
    {
        return mode switch
        {
            WindowPlacementMode.MouseFollow => SearchOverlayPlacementMode.MouseFollow,
            WindowPlacementMode.CursorScreenCenter => SearchOverlayPlacementMode.CursorScreenCenter,
            _ => SearchOverlayPlacementMode.Fixed
        };
    }

    private void PositionWindowForSearchOverlayAnchor()
    {
        try
        {
            var effectiveMode = GetEffectiveSearchPlacementMode();
            switch (effectiveMode)
            {
                case SearchOverlayPlacementMode.MouseFollow:
                    _suppressFixedCapture = true;
                    PositionWindowAtMouse();
                    break;
                case SearchOverlayPlacementMode.CursorScreenCenter:
                    _suppressFixedCapture = true;
                    PositionWindowAtCursorScreenCenter();
                    break;
                case SearchOverlayPlacementMode.Fixed:
                default:
                    var placement = GetPlacementMode(ShowLayerTrigger.Prefix);
                    _suppressFixedCapture = placement != WindowPlacementMode.Fixed;
                    switch (placement)
                    {
                        case WindowPlacementMode.MouseFollow:
                            PositionWindowAtMouse();
                            break;
                        case WindowPlacementMode.CursorScreenCenter:
                            PositionWindowAtCursorScreenCenter();
                            break;
                        case WindowPlacementMode.Fixed:
                        default:
                            _suppressFixedCapture = false;
                            PositionWindowAtFixedLocation();
                            break;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
        _logger.Warn($"Failed to position window for search anchor: {ex.Message}");
        }
    }

    private bool TryMinimizeOnEscape(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
    {
        if (key != System.Windows.Input.Key.Escape || modifiers != System.Windows.Input.ModifierKeys.None)
        {
            return false;
        }
        if (_searchLayerActive || _isMinimizedToTray || !IsActive || Topmost)
        {
            return false;
        }

        MinimizeWindowToTray();
        return true;
    }

    private static class NativeMethods
    {
        internal const int SW_RESTORE = 9;
        internal const int VK_LBUTTON = 0x01;
        internal const int VK_RBUTTON = 0x02;
        internal const int VK_MBUTTON = 0x04;
        internal const int VK_XBUTTON1 = 0x05;
        internal const int VK_XBUTTON2 = 0x06;
        internal const ushort VK_LSHIFT = 0xA0;
        internal const ushort VK_RSHIFT = 0xA1;
        internal const ushort VK_LCONTROL = 0xA2;
        internal const ushort VK_RCONTROL = 0xA3;
        internal const ushort VK_LMENU = 0xA4;
        internal const ushort VK_RMENU = 0xA5;
        internal const ushort VK_LWIN = 0x5B;
        internal const ushort VK_RWIN = 0x5C;
        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        internal static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public int type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        internal static bool IsKeyPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

        internal static void MouseEventMove(int dx, int dy) => mouse_event(MOUSEEVENTF_MOVE, dx, dy, 0, UIntPtr.Zero);

        internal static bool SendMouseMoveRelative(int dx, int dy)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                Data = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = 0,
                        dwFlags = MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
        }

        internal static bool SendKeyUp(ushort virtualKey)
        {
            uint flags = KEYEVENTF_KEYUP;
            if (IsExtendedKey(virtualKey))
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = virtualKey,
                        wScan = 0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
        }

        private static bool IsExtendedKey(ushort virtualKey) =>
            virtualKey is VK_RMENU or VK_RCONTROL;
    }
}
