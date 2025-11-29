using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DropSendTo.Models;

namespace DropSendTo.Services;

public class ConfigService
{
    private readonly string _baseDir;
    private readonly LoggerService _logger = LoggerService.Instance;
    public ConfigService(string? baseDir = null)
    {
        _baseDir = baseDir ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }
    private string ConfigDir => Path.Combine(_baseDir, "DropSendTo");
    private string ConfigPath => Path.Combine(ConfigDir, "config.json");
    public string GetConfigPath() => ConfigPath;
    private string BackupPath => Path.Combine(ConfigDir, "config.json.bak");

    public AppConfig LoadOrCreate()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
                _logger.Info($"Config directory created: {ConfigDir}");
            }
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                Validate(cfg);
                if (Migrate(cfg))
                {
                    _logger.Info($"Config migrated to version {cfg.Version}.");
                    Save(cfg);
                }
                _logger.Info($"Config loaded from {ConfigPath} (version={cfg.Version}).");
                return cfg;
            }
        }
        catch (Exception ex)
        {
            if (File.Exists(BackupPath))
            {
                try
                {
                    var json = File.ReadAllText(BackupPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                    Validate(cfg);
                    if (Migrate(cfg))
                    {
                        _logger.Info($"Config migrated from backup to version {cfg.Version}.");
                        Save(cfg);
                    }
                    _logger.Warn($"Config restored from backup {BackupPath}.");
                    return cfg;
                }
                catch (Exception backupEx)
                {
                    _logger.Error($"Failed to load backup config from {BackupPath}: {backupEx}");
                }
            }
            _logger.Warn($"Failed to load config from {ConfigPath}: {ex}");
        }
        var fresh = new AppConfig();
        Save(fresh);
        _logger.Info($"Created new default config at {ConfigPath}.");
        return fresh;
    }

    public void Save(AppConfig config)
    {
        Validate(config);
        if (!Directory.Exists(ConfigDir))
        {
            Directory.CreateDirectory(ConfigDir);
            _logger.Info($"Config directory created: {ConfigDir}");
        }
        if (File.Exists(ConfigPath))
        {
            File.Copy(ConfigPath, BackupPath, overwrite: true);
            _logger.Info($"Config backup updated at {BackupPath}.");
        }
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
        _logger.Info($"Config saved to {ConfigPath}.");
    }

    private const int MinSlotRows = 2;
    private const int MaxSlotRows = 8;
    private const int MinSlotColumns = 2;
    private const int MaxSlotColumns = 4;
    private const int MinGestureTurns = 1;
    private const int MaxGestureTurns = 50;
    private const int MinGestureRadius = 40;
    private const int MaxGestureRadius = 320;

    private static int NormalizeSlotRows(int value) => Math.Clamp(value, MinSlotRows, MaxSlotRows);
    private static int NormalizeSlotColumns(int value) => Math.Clamp(value, MinSlotColumns, MaxSlotColumns);
    private static int NormalizeGestureTurns(int value) => Math.Clamp(value, MinGestureTurns, MaxGestureTurns);
    private static int NormalizeGestureRadius(int value) => Math.Clamp(value, MinGestureRadius, MaxGestureRadius);

    private static void EnsureSlotCapacity(Layer layer, int requiredSlots)
    {
        while (layer.Slots.Count < requiredSlots)
        {
            layer.Slots.Add(new SlotModel());
        }
    }

    private static void Validate(AppConfig cfg)
    {
        if (cfg.Layers.Count != 4) throw new InvalidDataException("Config must have 4 layers.");
        cfg.ShortcutPrefix ??= string.Empty;
        cfg.SlotRows = NormalizeSlotRows(cfg.SlotRows);
        cfg.SlotColumns = NormalizeSlotColumns(cfg.SlotColumns);
        if (!Enum.IsDefined(typeof(SlotSize), cfg.SlotSize))
        {
            cfg.SlotSize = SlotSize.Medium;
        }
        cfg.CurrentLayer = Math.Clamp(cfg.CurrentLayer, 0, 3);
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
        if (!Enum.IsDefined(typeof(MacroConcurrencyMode), cfg.MacroConcurrencyMode))
        {
            cfg.MacroConcurrencyMode = MacroConcurrencyMode.Exclusive;
        }
        cfg.MouseGestureClockwiseTurnsToShow = NormalizeGestureTurns(cfg.MouseGestureClockwiseTurnsToShow);
        cfg.MouseGestureCounterClockwiseTurnsToHide = NormalizeGestureTurns(cfg.MouseGestureCounterClockwiseTurnsToHide);
        cfg.MouseGestureRadiusPixels = NormalizeGestureRadius(cfg.MouseGestureRadiusPixels <= 0 ? 120 : cfg.MouseGestureRadiusPixels);
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
            cfg.EnablePrefixLayerShortcuts = false;
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
            cfg.EnableEmacsNavigation = true;
            cfg.EnableViNavigation = true;
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
            cfg.MouseGestureRadiusPixels = NormalizeGestureRadius(cfg.MouseGestureRadiusPixels <= 0 ? 120 : cfg.MouseGestureRadiusPixels);
            cfg.Version = 22;
            changed = true;
        }

        return changed;
    }
}
