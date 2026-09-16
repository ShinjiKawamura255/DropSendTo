using System;
using System.IO;
using System.Text.Json;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ConfigServiceTests
{
    private static int CurrentConfigVersion => new AppConfig().Version;

    [Fact]
    public void LoadOrCreate_Should_Create_Default_Config_When_Missing()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var svc = new ConfigService(temp);
        var cfg = svc.LoadOrCreate();
        cfg.Layers.Count.Should().Be(4);
        foreach (var layer in cfg.Layers)
        {
            layer.Slots.Count.Should().Be(4);
            foreach (var slot in layer.Slots)
            {
                slot.KeyboardMacroScript.Should().NotBeNull();
                slot.ExecutionMode.Should().Be(SlotExecutionMode.Command);
            }
        }
        cfg.AlwaysOnTop.Should().BeTrue();
        cfg.StartupBehavior.Should().Be(StartupWindowBehavior.AlwaysShow);
        cfg.LastWindowVisibility.Should().Be(WindowVisibilityState.Visible);
        cfg.MacroConcurrencyMode.Should().Be(MacroConcurrencyMode.Exclusive);
        cfg.EnablePrefixDropCapture.Should().BeTrue();
        cfg.EnableEmacsNavigation.Should().BeFalse();
        cfg.EnableViNavigation.Should().BeFalse();
        cfg.EnableMouseGestures.Should().BeTrue();
        cfg.EnableDragMiddleClickShow.Should().BeFalse();
        cfg.MouseGestureClockwiseTurnsToShow.Should().Be(3);
        cfg.MouseGestureCounterClockwiseTurnsToHide.Should().Be(2);
        cfg.Theme.Should().Be(AppTheme.Dark);
        cfg.CustomSlotSize.Should().NotBeNull();
        cfg.CustomSlotSize.SlotHeight.Should().BeGreaterThan(0);
        cfg.Version.Should().Be(CurrentConfigVersion);
    }

    [Fact]
    public void LoadOrCreate_Should_Clamp_Rows_And_Columns_To_Max()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        var cfgDir = Path.Combine(temp, "DropSendTo");
        Directory.CreateDirectory(cfgDir);
        var cfg = new AppConfig
        {
            SlotRows = 10,
            SlotColumns = 10,
            Layers = new() { new Layer(), new Layer(), new Layer(), new Layer() }
        };
        File.WriteAllText(Path.Combine(cfgDir, "config.json"), JsonSerializer.Serialize(cfg));

        var svc = new ConfigService(temp);
        var loaded = svc.LoadOrCreate();

        loaded.SlotRows.Should().Be(8);
        loaded.SlotColumns.Should().Be(8);
    }

    [Fact]
    public void LoadOrCreate_Should_Reset_Invalid_StartupBehavior()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        var cfgDir = Path.Combine(temp, "DropSendTo");
        Directory.CreateDirectory(cfgDir);
        var cfg = new AppConfig
        {
            StartupBehavior = (StartupWindowBehavior)999,
            Layers = new() { new Layer(), new Layer(), new Layer(), new Layer() }
        };
        File.WriteAllText(Path.Combine(cfgDir, "config.json"), JsonSerializer.Serialize(cfg));

        var svc = new ConfigService(temp);
        var loaded = svc.LoadOrCreate();

        loaded.StartupBehavior.Should().Be(StartupWindowBehavior.AlwaysShow);
    }

    [Fact]
    public void Save_Should_Persist_AlwaysOnTop_Flag()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var svc = new ConfigService(temp);
        var cfg = svc.LoadOrCreate();
        cfg.AlwaysOnTop = false;
        svc.Save(cfg);

        var reloaded = svc.LoadOrCreate();
        reloaded.AlwaysOnTop.Should().BeFalse();
        reloaded.Version.Should().Be(CurrentConfigVersion);
    }

    [Fact]
    public void LoadOrCreate_Should_Migrate_V3_Config_And_Add_Macro_Field()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        var cfgDir = Path.Combine(temp, "DropSendTo");
        Directory.CreateDirectory(cfgDir);
        var legacyJson = """
        {
          "Version": 3,
          "CurrentLayer": 0,
          "AlwaysOnTop": true,
          "Layers": [
            { "Slots": [
                { "Title": "One", "Command": "cmd.exe", "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": "Two", "Command": "cmd.exe", "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": "Three", "Command": "cmd.exe", "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": "Four", "Command": "cmd.exe", "ArgumentsTemplate": "{args}", "ClickEnabled": true }
              ] },
            { "Slots": [
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true }
              ] },
            { "Slots": [
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true }
              ] },
            { "Slots": [
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true }
              ] }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(cfgDir, "config.json"), legacyJson);

        var svc = new ConfigService(temp);
        var cfg = svc.LoadOrCreate();
        cfg.Version.Should().Be(CurrentConfigVersion);
        cfg.StartupBehavior.Should().Be(StartupWindowBehavior.AlwaysShow);
        cfg.LastWindowVisibility.Should().Be(WindowVisibilityState.Visible);
        foreach (var layer in cfg.Layers)
            foreach (var slot in layer.Slots)
            {
                slot.KeyboardMacroScript.Should().Be(string.Empty);
                slot.ExecutionMode.Should().Be(SlotExecutionMode.Command);
            }
    }

    [Fact]
    public void Save_Should_Obfuscate_Macro_Script_On_Disk()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var svc = new ConfigService(temp);
        var cfg = svc.LoadOrCreate();
        cfg.Layers[0].Slots[0].KeyboardMacroScript = "SECRET";

        svc.Save(cfg);

        var cfgPath = Path.Combine(temp, "DropSendTo", "config.json");
        var json = File.ReadAllText(cfgPath);
        json.Should().Contain("!obf!");
        json.Should().NotContain("SECRET");

        var reloaded = svc.LoadOrCreate();
        reloaded.Layers[0].Slots[0].KeyboardMacroScript.Should().Be("SECRET");
    }

    [Fact]
    public void LoadOrCreate_Should_Migrate_Legacy_Macro_Mode_To_ExecutionMode()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        var cfgDir = Path.Combine(temp, "DropSendTo");
        Directory.CreateDirectory(cfgDir);
        var legacyJson = """
        {
          "Version": 11,
          "CurrentLayer": 0,
          "AlwaysOnTop": true,
          "SlotRows": 2,
          "SlotColumns": 2,
          "Layers": [
            { "Slots": [
                { "Title": "MacroOne", "Command": "", "ArgumentsTemplate": "{args}", "ClickEnabled": true, "KeyboardMacroScript": "TEXT Hello" },
                { "Title": "MacroExtended", "Command": "cmd.exe", "ArgumentsTemplate": "{args}", "ClickEnabled": true, "KeyboardMacroScript": "TEXT World" },
                { "Title": "CmdOnly", "Command": "cmd.exe", "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true }
              ] },
            { "Slots": [
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true }
              ] },
            { "Slots": [
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true }
              ] },
            { "Slots": [
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true },
                { "Title": null, "Command": null, "ArgumentsTemplate": "{args}", "ClickEnabled": true }
              ] }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(cfgDir, "config.json"), legacyJson);

        var svc = new ConfigService(temp);
        var cfg = svc.LoadOrCreate();

        cfg.Version.Should().Be(CurrentConfigVersion);
        var firstSlot = cfg.Layers[0].Slots[0];
        firstSlot.KeyboardMacroScript.Should().Be("TEXT Hello");
        firstSlot.ExecutionMode.Should().Be(SlotExecutionMode.MacroScript);

        var secondSlot = cfg.Layers[0].Slots[1];
        secondSlot.ExecutionMode.Should().Be(SlotExecutionMode.MacroScriptExtended);

        var thirdSlot = cfg.Layers[0].Slots[2];
        thirdSlot.ExecutionMode.Should().Be(SlotExecutionMode.Command);
    }

    [Fact]
    public void Save_Should_Normalize_ExecutionMode_Based_On_Content()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var svc = new ConfigService(temp);
        var cfg = svc.LoadOrCreate();

        var macroSlot = cfg.Layers[0].Slots[0];
        macroSlot.KeyboardMacroScript = "TEXT Macro";
        macroSlot.Command = string.Empty;
        macroSlot.ExecutionMode = SlotExecutionMode.Command;

        var extendedSlot = cfg.Layers[0].Slots[1];
        extendedSlot.KeyboardMacroScript = "TEXT Extended";
        extendedSlot.Command = "cmd.exe";
        extendedSlot.ArgumentsTemplate = "{args}";
        extendedSlot.ExecutionMode = SlotExecutionMode.Command;

        svc.Save(cfg);

        var reloaded = svc.LoadOrCreate();
        reloaded.Layers[0].Slots[0].ExecutionMode.Should().Be(SlotExecutionMode.MacroScript);
        reloaded.Layers[0].Slots[1].ExecutionMode.Should().Be(SlotExecutionMode.MacroScriptExtended);
    }

    [Fact]
    public void LoadOrCreate_Should_Migrate_V14_SlotSizes_To_NewScale()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));

        var smallDir = Path.Combine(tempRoot, "small");
        var legacySmall = new AppConfig
        {
            Version = 14,
            SlotSize = SlotSize.Small,
            SlotRows = 10,
            SlotColumns = 5
        };
        WriteLegacyConfig(smallDir, legacySmall);

        var smallService = new ConfigService(smallDir);
        var migratedSmall = smallService.LoadOrCreate();
        migratedSmall.SlotSize.Should().Be(SlotSize.Medium);
        migratedSmall.SlotRows.Should().Be(8);
        migratedSmall.SlotColumns.Should().Be(5);

        var largeDir = Path.Combine(tempRoot, "large");
        var legacyLarge = new AppConfig
        {
            Version = 14,
            SlotSize = SlotSize.Medium,
            SlotRows = 3,
            SlotColumns = 2
        };
        WriteLegacyConfig(largeDir, legacyLarge);

        var largeService = new ConfigService(largeDir);
        var migratedLarge = largeService.LoadOrCreate();
        migratedLarge.SlotSize.Should().Be(SlotSize.Large);
    }

    [Fact]
    public void Save_Should_Create_Primary_Atomically_On_First_Save()
    {
        var temp = CreateTempRoot();
        var service = CreateService(temp, new PhysicalConfigFileSystem());

        service.Save(new AppConfig { AlwaysOnTop = false });

        var configDir = Path.Combine(temp, "DropSendTo");
        File.Exists(Path.Combine(configDir, "config.json")).Should().BeTrue();
        Directory.GetFiles(configDir, "config.json.*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Save_Should_Preserve_Primary_And_Backup_When_Temp_Write_Fails()
    {
        var temp = CreateTempRoot();
        var fileSystem = PreparePrimaryAndBackup(temp, out var primaryBefore, out var backupBefore);
        fileSystem.Failure = ConfigFileFailure.Write;
        var service = CreateService(temp, fileSystem);

        var action = () => service.Save(new AppConfig { AlwaysOnTop = false });

        action.Should().Throw<IOException>();
        AssertPrimaryBackupAndTemps(temp, primaryBefore, backupBefore);
    }

    [Fact]
    public void Save_Should_Preserve_Primary_And_Backup_When_Flush_Fails()
    {
        var temp = CreateTempRoot();
        var fileSystem = PreparePrimaryAndBackup(temp, out var primaryBefore, out var backupBefore);
        fileSystem.Failure = ConfigFileFailure.Flush;
        var service = CreateService(temp, fileSystem);

        var action = () => service.Save(new AppConfig { AlwaysOnTop = false });

        action.Should().Throw<IOException>();
        AssertPrimaryBackupAndTemps(temp, primaryBefore, backupBefore);
    }

    [Fact]
    public void Save_Should_Preserve_Primary_And_Backup_When_Replace_Fails()
    {
        var temp = CreateTempRoot();
        var fileSystem = PreparePrimaryAndBackup(temp, out var primaryBefore, out var backupBefore);
        fileSystem.Failure = ConfigFileFailure.Replace;
        var service = CreateService(temp, fileSystem);

        var action = () => service.Save(new AppConfig { AlwaysOnTop = false });

        action.Should().Throw<IOException>();
        AssertPrimaryBackupAndTemps(temp, primaryBefore, backupBefore);
    }

    [Fact]
    public void Save_Should_Restore_Primary_When_Backup_Promotion_Fails_Without_Existing_Backup()
    {
        var temp = CreateTempRoot();
        var physical = new PhysicalConfigFileSystem();
        var initialService = CreateService(temp, physical);
        initialService.Save(new AppConfig { AlwaysOnTop = false });
        var configDir = Path.Combine(temp, "DropSendTo");
        var primaryPath = Path.Combine(configDir, "config.json");
        var backupPath = Path.Combine(configDir, "config.json.bak");
        var primaryBefore = File.ReadAllText(primaryPath);
        var fileSystem = new FaultInjectingConfigFileSystem(physical)
        {
            Failure = ConfigFileFailure.BackupPromotion
        };
        var service = CreateService(temp, fileSystem);

        var action = () => service.Save(new AppConfig { AlwaysOnTop = true });

        action.Should().Throw<IOException>();
        File.ReadAllText(primaryPath).Should().Be(primaryBefore);
        File.Exists(backupPath).Should().BeFalse();
        Directory.GetFiles(configDir, "config.json.*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Save_Should_Preserve_Primary_And_Backup_When_Backup_Promotion_Fails()
    {
        var temp = CreateTempRoot();
        var fileSystem = PreparePrimaryAndBackup(temp, out var primaryBefore, out var backupBefore);
        fileSystem.Failure = ConfigFileFailure.BackupPromotion;
        var service = CreateService(temp, fileSystem);

        var action = () => service.Save(new AppConfig { AlwaysOnTop = false });

        action.Should().Throw<IOException>();
        AssertPrimaryBackupAndTemps(temp, primaryBefore, backupBefore);
    }

    [Fact]
    public void Save_Should_Preserve_Recovery_Artifact_When_Backup_Promotion_And_Rollback_Fail()
    {
        var temp = CreateTempRoot();
        var physical = new PhysicalConfigFileSystem();
        var initialService = CreateService(temp, physical);
        initialService.Save(new AppConfig { AlwaysOnTop = false });
        var configDir = Path.Combine(temp, "DropSendTo");
        var primaryPath = Path.Combine(configDir, "config.json");
        var primaryBefore = File.ReadAllText(primaryPath);
        var fileSystem = new FaultInjectingConfigFileSystem(physical)
        {
            Failure = ConfigFileFailure.BackupPromotionAndRollback
        };
        var service = CreateService(temp, fileSystem);

        var action = () => service.Save(new AppConfig { AlwaysOnTop = true });

        action.Should().Throw<IOException>();
        var recoveryArtifacts = Directory.GetFiles(configDir, "config.json.previous.*.recovery");
        recoveryArtifacts.Should().ContainSingle();
        File.ReadAllText(recoveryArtifacts[0]).Should().Be(primaryBefore);
        Directory.GetFiles(configDir, "config.json.*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Save_Should_Remove_Stale_Owned_Temp_Files()
    {
        var temp = CreateTempRoot();
        var configDir = Path.Combine(temp, "DropSendTo");
        Directory.CreateDirectory(configDir);
        var staleTemp = Path.Combine(configDir, "config.json.stale.tmp");
        File.WriteAllText(staleTemp, "stale");
        var service = CreateService(temp, new PhysicalConfigFileSystem());

        service.Save(new AppConfig());

        File.Exists(staleTemp).Should().BeFalse();
        Directory.GetFiles(configDir, "config.json.*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void LoadOrCreate_Should_Repair_Corrupt_Primary_Without_Overwriting_Valid_Backup()
    {
        var temp = CreateTempRoot();
        var service = CreateService(temp, new PhysicalConfigFileSystem());
        service.Save(new AppConfig { AlwaysOnTop = false });
        service.Save(new AppConfig { AlwaysOnTop = true });
        var configDir = Path.Combine(temp, "DropSendTo");
        var primaryPath = Path.Combine(configDir, "config.json");
        var backupPath = Path.Combine(configDir, "config.json.bak");
        var backupBefore = File.ReadAllText(backupPath);
        File.WriteAllText(primaryPath, "{ corrupt");

        var recovered = service.LoadOrCreate();

        recovered.AlwaysOnTop.Should().BeFalse();
        JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(primaryPath)).Should().NotBeNull();
        File.ReadAllText(backupPath).Should().Be(backupBefore);
        Directory.GetFiles(configDir, "config.json.*.tmp").Should().BeEmpty();
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));

    private static ConfigService CreateService(string baseDir, IConfigFileSystem fileSystem) =>
        new(baseDir, fileSystem, NullAppLogger.Instance);

    private static FaultInjectingConfigFileSystem PreparePrimaryAndBackup(
        string baseDir,
        out string primaryBefore,
        out string backupBefore)
    {
        var physical = new PhysicalConfigFileSystem();
        var service = CreateService(baseDir, physical);
        service.Save(new AppConfig { AlwaysOnTop = false });
        service.Save(new AppConfig { AlwaysOnTop = true });
        var configDir = Path.Combine(baseDir, "DropSendTo");
        primaryBefore = File.ReadAllText(Path.Combine(configDir, "config.json"));
        backupBefore = File.ReadAllText(Path.Combine(configDir, "config.json.bak"));
        return new FaultInjectingConfigFileSystem(physical);
    }

    private static void AssertPrimaryBackupAndTemps(string baseDir, string primaryBefore, string backupBefore)
    {
        var configDir = Path.Combine(baseDir, "DropSendTo");
        File.ReadAllText(Path.Combine(configDir, "config.json")).Should().Be(primaryBefore);
        File.ReadAllText(Path.Combine(configDir, "config.json.bak")).Should().Be(backupBefore);
        Directory.GetFiles(configDir, "config.json.*.tmp").Should().BeEmpty();
    }

    private enum ConfigFileFailure
    {
        None,
        Write,
        Flush,
        Replace,
        BackupPromotion,
        BackupPromotionAndRollback
    }

    private sealed class FaultInjectingConfigFileSystem(IConfigFileSystem inner) : IConfigFileSystem
    {
        public ConfigFileFailure Failure { get; set; }

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public bool FileExists(string path) => inner.FileExists(path);
        public string ReadAllText(string path) => inner.ReadAllText(path);
        public IEnumerable<string> EnumerateFiles(string path, string pattern) => inner.EnumerateFiles(path, pattern);

        public void WriteAllText(string path, string contents)
        {
            if (Failure == ConfigFileFailure.Write) throw new IOException("Injected write failure.");
            inner.WriteAllText(path, contents);
        }

        public void FlushFile(string path)
        {
            if (Failure == ConfigFileFailure.Flush) throw new IOException("Injected flush failure.");
            inner.FlushFile(path);
        }

        public void ReplaceFile(string sourcePath, string destinationPath, string? backupPath)
        {
            if (Failure == ConfigFileFailure.Replace) throw new IOException("Injected replace failure.");
            if (Failure == ConfigFileFailure.BackupPromotionAndRollback &&
                backupPath == null &&
                sourcePath.Contains("config.json.previous.", StringComparison.Ordinal))
            {
                throw new IOException("Injected primary rollback failure.");
            }
            inner.ReplaceFile(sourcePath, destinationPath, backupPath);
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            if ((Failure == ConfigFileFailure.BackupPromotion ||
                 Failure == ConfigFileFailure.BackupPromotionAndRollback) &&
                destinationPath.EndsWith("config.json.bak", StringComparison.Ordinal))
            {
                throw new IOException("Injected backup promotion failure.");
            }
            inner.MoveFile(sourcePath, destinationPath, overwrite);
        }
        public void DeleteFile(string path) => inner.DeleteFile(path);
    }

    private static void WriteLegacyConfig(string baseDir, AppConfig config)
    {
        var cfgDir = Path.Combine(baseDir, "DropSendTo");
        Directory.CreateDirectory(cfgDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(cfgDir, "config.json"), json);
    }
}
