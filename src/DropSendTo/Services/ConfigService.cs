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

    private const int MinSlotDimension = 2;
    private const int MaxSlotDimension = 4;

    private static int NormalizeSlotDimension(int value) =>
        value is >= MinSlotDimension and <= MaxSlotDimension ? value : MinSlotDimension;

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
        cfg.SlotRows = NormalizeSlotDimension(cfg.SlotRows);
        cfg.SlotColumns = NormalizeSlotDimension(cfg.SlotColumns);
        cfg.CurrentLayer = Math.Clamp(cfg.CurrentLayer, 0, 3);
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
            layer.Slots ??= new List<SlotModel>();
            EnsureSlotCapacity(layer, requiredSlots);
            foreach (var slot in layer.Slots)
            {
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
            cfg.SlotRows = NormalizeSlotDimension(cfg.SlotRows);
            cfg.SlotColumns = NormalizeSlotDimension(cfg.SlotColumns);
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

        return changed;
    }
}
