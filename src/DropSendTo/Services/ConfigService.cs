using System;
using System.IO;
using System.Text.Json;
using DropSendTo.Models;

namespace DropSendTo.Services;

public class ConfigService
{
    private string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropSendTo");
    private string ConfigPath => Path.Combine(ConfigDir, "config.json");
    private string BackupPath => Path.Combine(ConfigDir, "config.json.bak");

    public AppConfig LoadOrCreate()
    {
        try
        {
            if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                Validate(cfg);
                return cfg;
            }
        }
        catch
        {
            if (File.Exists(BackupPath))
            {
                try
                {
                    var json = File.ReadAllText(BackupPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                    Validate(cfg);
                    return cfg;
                }
                catch { /* fall through */ }
            }
        }
        var fresh = new AppConfig();
        Save(fresh);
        return fresh;
    }

    public void Save(AppConfig config)
    {
        Validate(config);
        if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
        if (File.Exists(ConfigPath)) File.Copy(ConfigPath, BackupPath, overwrite: true);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    private static void Validate(AppConfig cfg)
    {
        if (cfg.Layers.Count != 4) throw new InvalidDataException("Config must have 4 layers.");
        foreach (var layer in cfg.Layers)
        {
            if (layer.Slots.Count != 4) throw new InvalidDataException("Each layer must have 4 slots.");
        }
        cfg.CurrentLayer = Math.Clamp(cfg.CurrentLayer, 0, 3);
    }
}

