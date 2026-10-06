using System;
using System.Threading.Tasks;
using System.Windows;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

public partial class MainWindow
{
    private SettingsWindow? _settingsWindow;

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null)
        {
            var window = new SettingsWindow(() => _config, ReadStartupRegistration,
                ApplySetting, ExecuteSettingsActionAsync)
            { Owner = this };
            window.Closed += (_, _) => _settingsWindow = null;
            WindowCascadeService.Arrange(window, this);
            _settingsWindow = window;
            window.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.RefreshSettings();
            _settingsWindow.Activate();
        }
    }

    private bool ReadStartupRegistration()
    {
        try { return _startupRegistrationService.IsRegistered(); }
        catch (Exception ex)
        {
            _logger.Warn($"Could not read Windows sign-in startup registration: {ex.Message}");
            return false;
        }
    }

    private void ApplySetting(SettingsOption option, object value)
    {
        try { ApplySettingCore(option, value); }
        catch (Exception ex)
        {
            _logger.Error($"Settings update {option} failed: {ex}");
            throw;
        }
    }

    private void ApplySettingCore(SettingsOption option, object value)
    {
        switch (option)
        {
            case SettingsOption.SlotLayout:
                var layout = (SlotLayoutSelection)value;
                int rows = Math.Clamp(layout.Rows, MinSlotRows, MaxSlotRows);
                int columns = Math.Clamp(layout.Columns, MinSlotColumns, MaxSlotColumns);
                if (rows == _config.SlotRows && columns == _config.SlotColumns) return;
                _config.SlotRows = rows;
                _config.SlotColumns = columns;
                ApplySlotLayout();
                RefreshUi();
                ClampWindowWithinBounds();
                break;
            case SettingsOption.SlotSize:
                if (_config.SlotSize == (SlotSize)value) return;
                _config.SlotSize = (SlotSize)value;
                ApplySlotLayout();
                RefreshUi();
                ClampWindowWithinBounds();
                break;
            case SettingsOption.HideEmptySlotNames:
                _config.HideEmptySlotNames = (bool)value;
                RefreshUi();
                break;
            case SettingsOption.AlwaysOnTop:
                _config.AlwaysOnTop = (bool)value;
                ApplyTopmostState();
                break;
            case SettingsOption.Theme:
                SetTheme((AppTheme)value);
                return;
            case SettingsOption.KeyboardPlacement:
                SetPlacementMode((WindowPlacementMode)(int)value, ShowLayerTrigger.Prefix, initiatedByToggle: false);
                return;
            case SettingsOption.MousePlacement:
                int mouseMode = (int)value;
                _mousePlacementFollowsKeyboard = mouseMode == -1;
                _config.MousePlacementFollowsKeyboard = _mousePlacementFollowsKeyboard;
                if (_mousePlacementFollowsKeyboard)
                {
                    _mousePlacementMode = _keyboardPlacementMode;
                    _config.MousePlacementMode = _mousePlacementMode;
                }
                else
                {
                    // Detaching from keyboard placement must be saved even when the coordinates mode is unchanged.
                    if (_mousePlacementMode == (WindowPlacementMode)mouseMode)
                    {
                        _configService.Save(_config);
                        return;
                    }
                    SetPlacementMode((WindowPlacementMode)mouseMode, ShowLayerTrigger.MouseGesture, initiatedByToggle: false);
                    return;
                }
                break;
            case SettingsOption.SearchPlacement:
                int searchMode = (int)value;
                _searchPlacementFollowsKeyboard = searchMode == -1;
                _config.SearchPlacementFollowsKeyboard = _searchPlacementFollowsKeyboard;
                if (!_searchPlacementFollowsKeyboard)
                {
                    _searchPlacementMode = (SearchOverlayPlacementMode)searchMode;
                    _config.SearchPlacementMode = _searchPlacementMode;
                }
                if (_searchLayerActive && _searchOverlayWindow?.IsVisible == true) PositionSearchOverlay();
                break;
            case SettingsOption.EmacsNavigation:
                _config.EnableEmacsNavigation = (bool)value;
                UpdateSearchOverlayNavigationModes();
                break;
            case SettingsOption.ViNavigation:
                _config.EnableViNavigation = (bool)value;
                UpdateSearchOverlayNavigationModes();
                break;
            case SettingsOption.LayerCount:
                int count = Math.Clamp((int)value, MinLayers, MaxLayers);
                if (_config.Layers.Count == count) return;
                ApplyLayerCount(count);
                return;
            case SettingsOption.StartupBehavior:
                _config.StartupBehavior = (StartupWindowBehavior)value;
                break;
            case SettingsOption.StartupRegistration:
                if ((bool)value) _startupRegistrationService.Register();
                else _startupRegistrationService.Unregister();
                return;
            case SettingsOption.MacroConcurrency:
                _config.MacroConcurrencyMode = (MacroConcurrencyMode)value;
                break;
            case SettingsOption.Language:
                SetLanguage((AppLanguage)value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(option));
        }
        _configService.Save(_config);
    }

    private async Task ExecuteSettingsActionAsync(SettingsAction action)
    {
        try
        {
            switch (action)
            {
                case SettingsAction.CustomSlotSize:
                    var dialog = new CustomSlotSizeDialog(_config.CustomSlotSize ?? CustomSlotSizeOptions.CreateDefault())
                    { Owner = _settingsWindow ?? (Window)this };
                    dialog.SlotSizeApplied += (_, options) => ApplyCustomSlotSize(options);
                    WindowCascadeService.Arrange(dialog, dialog.Owner);
                    if (await dialog.ShowForResultAsync()) ApplyCustomSlotSize(dialog.ResultOptions.Clone());
                    break;
                case SettingsAction.Prefix: await ChangePrefixAsync(); break;
                case SettingsAction.SearchHotkey: await ConfigureSearchHotkeyAsync(); break;
                case SettingsAction.MouseGestures: await ConfigureMouseGesturesAsync(); break;
                case SettingsAction.LayerNames: await EditLayerNamesAsync(); break;
                case SettingsAction.AppLayers: await ConfigureAppLayersAsync(); break;
                case SettingsAction.ShowLayerPreferences: await ConfigureShowLayerPreferencesAsync(); break;
                case SettingsAction.StartupSlots: await ConfigureStartupSlotsAsync(); break;
                case SettingsAction.MinimizeTriggers: await ConfigureMinimizeTriggersAsync(); break;
                case SettingsAction.ExportConfig: await ExportConfigAsync(); break;
                case SettingsAction.ImportConfig: await ImportConfigAsync(); break;
                case SettingsAction.OpenConfig: OnOpenConfig(this, new RoutedEventArgs()); break;
                case SettingsAction.OpenLogs: OnOpenLogs(this, new RoutedEventArgs()); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Settings action {action} failed: {ex}");
            throw;
        }
    }
}
