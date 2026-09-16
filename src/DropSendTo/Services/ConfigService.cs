using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DropSendTo.Models;

namespace DropSendTo.Services;

public class ConfigService
{
    private readonly string _baseDir;
    private readonly IConfigFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    public ConfigService(string? baseDir = null) :
        this(baseDir, new PhysicalConfigFileSystem(), LoggerService.Instance)
    {
    }

    internal ConfigService(string? baseDir, IConfigFileSystem fileSystem, IAppLogger logger)
    {
        _baseDir = baseDir ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _fileSystem = fileSystem;
        _logger = logger;
    }

    private string ConfigDir => Path.Combine(_baseDir, "DropSendTo");
    private string ConfigPath => Path.Combine(ConfigDir, "config.json");
    public string GetConfigPath() => ConfigPath;
    private string BackupPath => Path.Combine(ConfigDir, "config.json.bak");

    public AppConfig LoadOrCreate()
    {
        EnsureConfigDirectory();
        Exception? primaryError = null;
        if (_fileSystem.FileExists(ConfigPath))
        {
            try
            {
                var cfg = LoadAndValidate(ConfigPath, out var migrated);
                if (migrated)
                {
                    _logger.Info($"Config migrated to version {cfg.Version}.");
                    Save(cfg);
                }
                _logger.Info($"Config loaded from {ConfigPath} (version={cfg.Version}).");
                return cfg;
            }
            catch (Exception ex)
            {
                primaryError = ex;
            }
        }

        if (_fileSystem.FileExists(BackupPath))
        {
            try
            {
                var cfg = LoadAndValidate(BackupPath, out var migrated);
                try
                {
                    RepairPrimaryFromBackup(cfg);
                }
                catch (Exception repairEx)
                {
                    _logger.Error($"Loaded backup but failed to repair primary config: {repairEx}");
                }
                if (migrated) _logger.Info($"Config migrated from backup to version {cfg.Version}.");
                _logger.Warn($"Config restored from backup {BackupPath}.");
                return cfg;
            }
            catch (Exception backupEx)
            {
                _logger.Error($"Failed to load backup config from {BackupPath}: {backupEx}");
            }
        }

        if (primaryError != null) _logger.Warn($"Failed to load config from {ConfigPath}: {primaryError}");
        var fresh = new AppConfig();
        Save(fresh);
        _logger.Info($"Created new default config at {ConfigPath}.");
        return fresh;
    }

    public void Save(AppConfig config)
    {
        NormalizeForUse(config);
        EnsureConfigDirectory();
        CleanupOwnedTempFiles();
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        CommitJson(json, preserveExistingBackup: false);
        _logger.Info($"Config saved to {ConfigPath}.");
    }

    private AppConfig LoadAndValidate(string path, out bool migrated)
    {
        var json = _fileSystem.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        NormalizeForUse(cfg);
        migrated = Migrate(cfg);
        return cfg;
    }

    private void RepairPrimaryFromBackup(AppConfig config)
    {
        EnsureConfigDirectory();
        CleanupOwnedTempFiles();
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        CommitJson(json, preserveExistingBackup: true);
    }

    private void EnsureConfigDirectory()
    {
        if (_fileSystem.DirectoryExists(ConfigDir)) return;
        _fileSystem.CreateDirectory(ConfigDir);
        _logger.Info($"Config directory created: {ConfigDir}");
    }

    private void CleanupOwnedTempFiles()
    {
        foreach (var tempPath in _fileSystem.EnumerateFiles(ConfigDir, "config.json.*.tmp"))
        {
            try
            {
                _fileSystem.DeleteFile(tempPath);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to remove stale config temp file: {ex.Message}");
            }
        }
    }

    private void CommitJson(string json, bool preserveExistingBackup)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(ConfigDir, $"config.json.{operationId}.tmp");
        string? backupCandidatePath = null;
        bool deleteBackupCandidate = true;
        try
        {
            _fileSystem.WriteAllText(tempPath, json);
            _fileSystem.FlushFile(tempPath);
            if (!_fileSystem.FileExists(ConfigPath))
            {
                _fileSystem.MoveFile(tempPath, ConfigPath);
                return;
            }

            if (preserveExistingBackup)
            {
                _fileSystem.ReplaceFile(tempPath, ConfigPath, backupPath: null);
                return;
            }

            backupCandidatePath = Path.Combine(ConfigDir, $"config.json.previous.{operationId}.recovery");
            _fileSystem.ReplaceFile(tempPath, ConfigPath, backupCandidatePath);
            try
            {
                _fileSystem.MoveFile(backupCandidatePath, BackupPath, overwrite: true);
                _logger.Info($"Config backup updated at {BackupPath}.");
            }
            catch (Exception ex)
            {
                try
                {
                    _fileSystem.ReplaceFile(backupCandidatePath, ConfigPath, backupPath: null);
                    backupCandidatePath = null;
                }
                catch (Exception rollbackEx)
                {
                    deleteBackupCandidate = false;
                    throw new IOException(
                        "Config backup refresh and primary rollback both failed; the previous config recovery artifact was preserved.",
                        new AggregateException(ex, rollbackEx));
                }

                throw new IOException(
                    "Config backup refresh failed; the previous primary config was restored.",
                    ex);
            }
        }
        finally
        {
            DeleteOwnedTempIfPresent(tempPath);
            if (deleteBackupCandidate && backupCandidatePath != null)
            {
                DeleteOwnedTempIfPresent(backupCandidatePath);
            }
        }
    }

    private void DeleteOwnedTempIfPresent(string path)
    {
        if (!_fileSystem.FileExists(path)) return;
        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to remove config temp file: {ex.Message}");
        }
    }

    private const int MinSlotRows = 2;
    private const int MaxSlotRows = 8;
    private const int MinSlotColumns = 2;
    private const int MaxSlotColumns = 8;
    private const int MinGestureTurns = 1;
    private const int MaxGestureTurns = 50;
    private const int MinGestureRadius = 0;
    private const int MaxGestureRadius = 320;
    private const int MinLayers = 4;
    private const int MaxLayers = 8;
    private static int NormalizeGestureRadius(int value) => Math.Clamp(value, MinGestureRadius, MaxGestureRadius);
    private static int NormalizeShowLayerPreference(int value, int layerCount)
    {
        if (layerCount <= 0) return -1;
        return value < 0 ? -1 : Math.Clamp(value, 0, layerCount - 1);
    }

    private static int NormalizeSlotRows(int value) => Math.Clamp(value, MinSlotRows, MaxSlotRows);
    private static int NormalizeSlotColumns(int value) => Math.Clamp(value, MinSlotColumns, MaxSlotColumns);
    private static int NormalizeGestureTurns(int value) => Math.Clamp(value, MinGestureTurns, MaxGestureTurns);

    private static void EnsureSlotCapacity(Layer layer, int requiredSlots)
    {
        while (layer.Slots.Count < requiredSlots)
        {
            layer.Slots.Add(new SlotModel());
        }
    }

    private static void EnsureLayerCount(AppConfig cfg)
    {
        cfg.Layers ??= new List<Layer>();
        int current = cfg.Layers.Count;
        int target = current <= 0 ? MinLayers : Math.Clamp(current, MinLayers, MaxLayers);
        if (current < target)
        {
            for (int i = current; i < target; i++)
            {
                cfg.Layers.Add(new Layer());
            }
        }
        else if (current > target)
        {
            cfg.Layers.RemoveRange(target, current - target);
        }
    }

    internal static void NormalizeForUse(AppConfig cfg)
    {
        EnsureLayerCount(cfg);
        cfg.ShortcutPrefix ??= string.Empty;
        cfg.SlotRows = NormalizeSlotRows(cfg.SlotRows);
        cfg.SlotColumns = NormalizeSlotColumns(cfg.SlotColumns);
        if (!Enum.IsDefined(typeof(SlotSize), cfg.SlotSize))
        {
            cfg.SlotSize = SlotSize.Medium;
        }
        cfg.CustomSlotSize = CustomSlotSizeNormalizer.Normalize(cfg.CustomSlotSize ?? CustomSlotSizeOptions.CreateDefault());
        cfg.CurrentLayer = Math.Clamp(cfg.CurrentLayer, 0, cfg.Layers.Count - 1);
        if (!Enum.IsDefined(typeof(StartupWindowBehavior), cfg.StartupBehavior))
        {
            cfg.StartupBehavior = StartupWindowBehavior.AlwaysShow;
        }
        if (!Enum.IsDefined(typeof(WindowVisibilityState), cfg.LastWindowVisibility))
        {
            cfg.LastWindowVisibility = WindowVisibilityState.Visible;
        }
        if (!Enum.IsDefined(typeof(WindowPlacementMode), cfg.WindowPlacementMode))
        {
            cfg.WindowPlacementMode = WindowPlacementMode.Fixed;
        }
        if (!Enum.IsDefined(typeof(WindowPlacementMode), cfg.KeyboardPlacementMode))
        {
            cfg.KeyboardPlacementMode = WindowPlacementMode.Fixed;
        }
        if (!Enum.IsDefined(typeof(WindowPlacementMode), cfg.MousePlacementMode))
        {
            cfg.MousePlacementMode = WindowPlacementMode.Fixed;
        }
        if (!Enum.IsDefined(typeof(SearchOverlayPlacementMode), cfg.SearchPlacementMode))
        {
            cfg.SearchPlacementMode = SearchOverlayPlacementMode.Fixed;
        }
        if (!Enum.IsDefined(typeof(AppLanguage), cfg.Language))
        {
            cfg.Language = AppLanguage.Japanese;
        }
        if (!Enum.IsDefined(typeof(AppTheme), cfg.Theme))
        {
            cfg.Theme = AppTheme.Dark;
        }
        if (!Enum.IsDefined(typeof(MacroConcurrencyMode), cfg.MacroConcurrencyMode))
        {
            cfg.MacroConcurrencyMode = MacroConcurrencyMode.Exclusive;
        }
        cfg.MouseGestureClockwiseTurnsToShow = NormalizeGestureTurns(cfg.MouseGestureClockwiseTurnsToShow);
        cfg.MouseGestureCounterClockwiseTurnsToHide = NormalizeGestureTurns(cfg.MouseGestureCounterClockwiseTurnsToHide);
        cfg.MouseGestureMinRadiusPixels = NormalizeGestureRadius(cfg.MouseGestureMinRadiusPixels <= 0 ? 40 : cfg.MouseGestureMinRadiusPixels);
        cfg.MouseGestureMaxRadiusPixels = NormalizeGestureRadius(cfg.MouseGestureMaxRadiusPixels <= 0 ? 140 : cfg.MouseGestureMaxRadiusPixels);
        if (cfg.MouseGestureMinRadiusPixels > cfg.MouseGestureMaxRadiusPixels)
        {
            cfg.MouseGestureMinRadiusPixels = cfg.MouseGestureMaxRadiusPixels;
        }
        if (cfg.ShortcutPrefixDisabled)
        {
            cfg.ShortcutPrefix = cfg.ShortcutPrefix.Trim();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(cfg.ShortcutPrefix))
            {
                cfg.ShortcutPrefix = "CTRL+Q";
            }
            else
            {
                cfg.ShortcutPrefix = cfg.ShortcutPrefix.Trim();
            }
        }

        cfg.SearchHotkey ??= string.Empty;
        cfg.SearchHotkey = cfg.SearchHotkey.Trim();
        if (cfg.SearchHotkeyEnabled && string.IsNullOrWhiteSpace(cfg.SearchHotkey))
        {
            cfg.SearchHotkeyEnabled = false;
        }

        int requiredSlots = cfg.SlotRows * cfg.SlotColumns;
        foreach (var layer in cfg.Layers)
        {
            layer.Name ??= string.Empty;
            layer.Name = layer.Name.Trim();
            layer.Slots ??= new List<SlotModel>();
            EnsureSlotCapacity(layer, requiredSlots);
            foreach (var slot in layer.Slots)
            {
                if (!Enum.IsDefined(typeof(SlotExecutionMode), slot.ExecutionMode))
                {
                    slot.ExecutionMode = SlotExecutionMode.Command;
                }

                if (!Enum.IsDefined(typeof(SlotAccentColor), slot.AccentColor))
                {
                    slot.AccentColor = SlotAccentColor.Default;
                }

                if (slot.KeyboardMacroScript == null)
                {
                    slot.KeyboardMacroScript = string.Empty;
                }

                if (slot.ShortcutKey == null)
                {
                    slot.ShortcutKey = string.Empty;
                }
                else
                {
                    slot.ShortcutKey = slot.ShortcutKey.Trim();
                }

                if (slot.SearchKeywords == null)
                {
                    slot.SearchKeywords = string.Empty;
                }
                else
                {
                    slot.SearchKeywords = slot.SearchKeywords.Trim();
                }

                bool hasMacro = !string.IsNullOrWhiteSpace(slot.KeyboardMacroScript);
                bool hasCommand = !string.IsNullOrWhiteSpace(slot.Command);

                slot.ExecutionMode = slot.ExecutionMode switch
                {
                    SlotExecutionMode.MacroScript when !hasMacro && hasCommand => SlotExecutionMode.Command,
                    SlotExecutionMode.MacroScript when !hasMacro => SlotExecutionMode.Command,
                    SlotExecutionMode.MacroScriptExtended when !hasMacro && hasCommand => SlotExecutionMode.Command,
                    SlotExecutionMode.MacroScriptExtended when hasMacro && !hasCommand => SlotExecutionMode.MacroScript,
                    _ when hasMacro && hasCommand => SlotExecutionMode.MacroScriptExtended,
                    _ when hasMacro => SlotExecutionMode.MacroScript,
                    _ => SlotExecutionMode.Command
                };
            }
        }

        cfg.MouseGestureShowLayerWhenVisible = NormalizeShowLayerPreference(cfg.MouseGestureShowLayerWhenVisible, cfg.Layers.Count);
        cfg.MouseGestureShowLayerWhenHidden = NormalizeShowLayerPreference(cfg.MouseGestureShowLayerWhenHidden, cfg.Layers.Count);
        cfg.PrefixShowLayerWhenVisible = NormalizeShowLayerPreference(cfg.PrefixShowLayerWhenVisible, cfg.Layers.Count);
        cfg.PrefixShowLayerWhenHidden = NormalizeShowLayerPreference(cfg.PrefixShowLayerWhenHidden, cfg.Layers.Count);
    }

    private static bool Migrate(AppConfig cfg)
    {
        bool changed = false;
        if (cfg.Version < 2)
        {
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    // v2 introduces ClickEnabled default true
                    if (slot is { ClickEnabled: false })
                    {
                        slot.ClickEnabled = true;
                        changed = true;
                    }
                }
            }
            cfg.Version = 2;
            changed = true;
        }

        if (cfg.Version < 3)
        {
            // v3 preserves legacy behavior of always-on-top window
            cfg.AlwaysOnTop = true;
            cfg.Version = 3;
            changed = true;
        }

        if (cfg.Version < 4)
        {
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    if (slot.KeyboardMacroScript == null)
                    {
                        slot.KeyboardMacroScript = string.Empty;
                        changed = true;
                    }
                }
            }
            cfg.Version = 4;
            changed = true;
        }

        if (cfg.Version < 5)
        {
            cfg.Version = 5;
            changed = true;
        }

        if (cfg.Version < 6)
        {
            if (string.IsNullOrWhiteSpace(cfg.ShortcutPrefix))
            {
                cfg.ShortcutPrefix = "CTRL+Q";
                changed = true;
            }
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    if (slot.ShortcutKey == null)
                    {
                        slot.ShortcutKey = string.Empty;
                        changed = true;
                    }
                }
            }
            cfg.Version = 6;
            changed = true;
        }

        if (cfg.Version < 7)
        {
            cfg.SlotRows = NormalizeSlotRows(cfg.SlotRows);
            cfg.SlotColumns = NormalizeSlotColumns(cfg.SlotColumns);
            int requiredSlots = cfg.SlotRows * cfg.SlotColumns;
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                EnsureSlotCapacity(layer, requiredSlots);
            }
            cfg.Version = 7;
            changed = true;
        }

        if (cfg.Version < 8)
        {
            // 新しいフラグが追加されたバージョン。既存設定ではプレフィックス無効化をオフとして扱う。
            cfg.ShortcutPrefixDisabled = false;
            cfg.Version = 8;
            changed = true;
        }

        if (cfg.Version < 9)
        {
            if (!Enum.IsDefined(typeof(SlotSize), cfg.SlotSize))
            {
                cfg.SlotSize = SlotSize.Medium;
            }
            cfg.Version = 9;
            changed = true;
        }

        if (cfg.Version < 10)
        {
            // v10 initializes window coordinates to the origin when missing.
            if (!cfg.WindowLeft.HasValue)
            {
                cfg.WindowLeft = 0;
                changed = true;
            }
            if (!cfg.WindowTop.HasValue)
            {
                cfg.WindowTop = 0;
                changed = true;
            }
            cfg.Version = 10;
            changed = true;
        }

        if (cfg.Version < 11)
        {
            if (!Enum.IsDefined(typeof(StartupWindowBehavior), cfg.StartupBehavior))
            {
                cfg.StartupBehavior = StartupWindowBehavior.AlwaysShow;
            }
            if (!Enum.IsDefined(typeof(WindowVisibilityState), cfg.LastWindowVisibility))
            {
                cfg.LastWindowVisibility = WindowVisibilityState.Visible;
            }
            cfg.Version = 11;
            changed = true;
        }

        if (cfg.Version < 12)
        {
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    if (!Enum.IsDefined(typeof(SlotExecutionMode), slot.ExecutionMode))
                    {
                        slot.ExecutionMode = SlotExecutionMode.Command;
                        changed = true;
                    }

                    bool hasMacro = !string.IsNullOrWhiteSpace(slot.KeyboardMacroScript);
                    bool hasCommand = !string.IsNullOrWhiteSpace(slot.Command);

                    var targetMode = hasMacro
                        ? (hasCommand ? SlotExecutionMode.MacroScriptExtended : SlotExecutionMode.MacroScript)
                        : SlotExecutionMode.Command;
                    if (slot.ExecutionMode != targetMode)
                    {
                        slot.ExecutionMode = targetMode;
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(slot.ArgumentsTemplate))
                    {
                        slot.ArgumentsTemplate = "{args}";
                        changed = true;
                    }
                }
            }

            cfg.Version = 12;
            changed = true;
        }

        if (cfg.Version < 13)
        {
            if (!Enum.IsDefined(typeof(WindowPlacementMode), cfg.WindowPlacementMode))
            {
                cfg.WindowPlacementMode = WindowPlacementMode.Fixed;
                changed = true;
            }
            cfg.Version = 13;
            changed = true;
        }

        if (cfg.Version < 14)
        {
            if (!Enum.IsDefined(typeof(MacroConcurrencyMode), cfg.MacroConcurrencyMode))
            {
                cfg.MacroConcurrencyMode = MacroConcurrencyMode.Exclusive;
                changed = true;
            }
            cfg.Version = 14;
            changed = true;
        }

        if (cfg.Version < 15)
        {
            if (cfg.SlotSize == SlotSize.Small)
            {
                cfg.SlotSize = SlotSize.Medium;
                changed = true;
            }
            else if (cfg.SlotSize == SlotSize.Medium)
            {
                cfg.SlotSize = SlotSize.Large;
                changed = true;
            }

            cfg.SlotRows = NormalizeSlotRows(cfg.SlotRows);
            cfg.SlotColumns = NormalizeSlotColumns(cfg.SlotColumns);
            cfg.Version = 15;
            changed = true;
        }

        if (cfg.Version < 16)
        {
            cfg.Version = 16;
            changed = true;
        }

        if (cfg.Version < 17)
        {
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    if (!Enum.IsDefined(typeof(SlotAccentColor), slot.AccentColor))
                    {
                        slot.AccentColor = SlotAccentColor.Default;
                        changed = true;
                    }
                }
            }

            cfg.Version = 17;
            changed = true;
        }

        if (cfg.Version < 18)
        {
            cfg.PreferRemoteSessions = true;
            cfg.Version = 18;
            changed = true;
        }

        if (cfg.Version < 19)
        {
            cfg.Language = AppLanguage.Japanese;
            cfg.Version = 19;
            changed = true;
        }

        if (cfg.Version < 19)
        {
            cfg.EnableEmacsNavigation = false;
            cfg.EnableViNavigation = false;
            cfg.Version = 19;
            changed = true;
        }

        if (cfg.Version < 20)
        {
            foreach (var layer in cfg.Layers)
            {
                layer.Name ??= string.Empty;
            }
            cfg.Version = 20;
            changed = true;
        }

        if (cfg.Version < 21)
        {
            cfg.EnableMouseGestures = true;
            cfg.MouseGestureClockwiseTurnsToShow = NormalizeGestureTurns(cfg.MouseGestureClockwiseTurnsToShow <= 0 ? 3 : cfg.MouseGestureClockwiseTurnsToShow);
            cfg.MouseGestureCounterClockwiseTurnsToHide = NormalizeGestureTurns(cfg.MouseGestureCounterClockwiseTurnsToHide <= 0 ? 2 : cfg.MouseGestureCounterClockwiseTurnsToHide);
            cfg.MouseGestureInvertDirections = false;
            cfg.MouseGestureRequireCtrl = false;
            cfg.MouseGestureSuppressDuringPresentation = false;
            cfg.Version = 21;
            changed = true;
        }

        if (cfg.Version < 22)
        {
            cfg.MouseGestureMinRadiusPixels = 0;
            cfg.MouseGestureMaxRadiusPixels = NormalizeGestureRadius(cfg.MouseGestureMaxRadiusPixels <= 0 ? 140 : cfg.MouseGestureMaxRadiusPixels);
            if (cfg.MouseGestureMinRadiusPixels > cfg.MouseGestureMaxRadiusPixels)
            {
                cfg.MouseGestureMinRadiusPixels = cfg.MouseGestureMaxRadiusPixels;
            }
            cfg.MouseGestureEnforceRadiusLimit = true;
            cfg.Version = 22;
            changed = true;
        }

        if (cfg.Version < 23)
        {
            cfg.Version = 23;
            changed = true;
        }

        if (cfg.Version < 24)
        {
            cfg.MouseGestureMinRadiusPixels = NormalizeGestureRadius(cfg.MouseGestureMinRadiusPixels);
            cfg.Version = 24;
            changed = true;
        }

        if (cfg.Version < 25)
        {
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    slot.MinimizeOptions ??= SlotMinimizeOptions.CreateDefault();
                }
            }
            cfg.Version = 25;
            changed = true;
        }

        if (cfg.Version < 26)
        {
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    slot.MinimizeOptions ??= SlotMinimizeOptions.CreateDefault();
                }
            }
            cfg.Version = 26;
            changed = true;
        }

        if (cfg.Version < 27)
        {
            cfg.MouseGestureShowLayerWhenVisible = NormalizeShowLayerPreference(-1, cfg.Layers.Count);
            cfg.MouseGestureShowLayerWhenHidden = NormalizeShowLayerPreference(-1, cfg.Layers.Count);
            cfg.PrefixShowLayerWhenVisible = NormalizeShowLayerPreference(-1, cfg.Layers.Count);
            cfg.PrefixShowLayerWhenHidden = NormalizeShowLayerPreference(-1, cfg.Layers.Count);
            cfg.Version = 27;
            changed = true;
        }

        if (cfg.Version < 28)
        {
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    if (slot.SearchKeywords == null)
                    {
                        slot.SearchKeywords = string.Empty;
                        changed = true;
                    }
                    else
                    {
                        var trimmed = slot.SearchKeywords.Trim();
                        if (!string.Equals(trimmed, slot.SearchKeywords, StringComparison.Ordinal))
                        {
                            slot.SearchKeywords = trimmed;
                            changed = true;
                        }
                    }
                }
            }

            cfg.Version = 28;
            changed = true;
        }

        if (cfg.Version < 29)
        {
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    slot.MinimizeOptions ??= SlotMinimizeOptions.CreateDefault();
                }
            }
            cfg.Version = 29;
            changed = true;
        }

        if (cfg.Version < 30)
        {
            cfg.SearchHotkeyEnabled = false;
            cfg.SearchHotkey ??= string.Empty;
            cfg.Version = 30;
            changed = true;
        }

        if (cfg.Version < 31)
        {
            cfg.DefaultMinimizeOptions ??= SlotMinimizeOptions.CreateDefault();
            foreach (var layer in cfg.Layers)
            {
                layer.Slots ??= new List<SlotModel>();
                foreach (var slot in layer.Slots)
                {
                    slot.MinimizeOptions ??= cfg.DefaultMinimizeOptions.Clone();
                }
            }
            cfg.Version = 31;
            changed = true;
        }

        if (cfg.Version < 32)
        {
            cfg.HideEmptySlotNames = false;
            cfg.Version = 32;
            changed = true;
        }

        if (cfg.Version < 33)
        {
            cfg.CustomSlotSize = CustomSlotSizeNormalizer.Normalize(cfg.CustomSlotSize ?? CustomSlotSizeOptions.CreateDefault());
            cfg.Version = 33;
            changed = true;
        }

        if (cfg.Version < 34)
        {
            cfg.KeyboardPlacementMode = cfg.WindowPlacementMode;
            cfg.MousePlacementMode = cfg.WindowPlacementMode;
            cfg.MousePlacementFollowsKeyboard = true;
            cfg.Version = 34;
            changed = true;
        }

        if (cfg.Version < 35)
        {
            cfg.Version = 35;
            changed = true;
        }

        if (cfg.Version < 36)
        {
            if (!Enum.IsDefined(typeof(AppTheme), cfg.Theme))
            {
                cfg.Theme = AppTheme.Dark;
            }
            cfg.Version = 36;
            changed = true;
        }

        if (cfg.Version < 37)
        {
            cfg.EnablePrefixDropCapture = true;
            cfg.Version = 37;
            changed = true;
        }

        return changed;
    }
}
