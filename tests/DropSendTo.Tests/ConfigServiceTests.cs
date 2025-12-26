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
        cfg.EnablePrefixLayerShortcuts.Should().BeFalse();
        cfg.EnableEmacsNavigation.Should().BeFalse();
        cfg.EnableViNavigation.Should().BeFalse();
        cfg.EnableMouseGestures.Should().BeTrue();
        cfg.EnableDragMiddleClickShow.Should().BeFalse();
        cfg.MouseGestureClockwiseTurnsToShow.Should().Be(3);
        cfg.MouseGestureCounterClockwiseTurnsToHide.Should().Be(2);
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

    private static void WriteLegacyConfig(string baseDir, AppConfig config)
    {
        var cfgDir = Path.Combine(baseDir, "DropSendTo");
        Directory.CreateDirectory(cfgDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(cfgDir, "config.json"), json);
    }
}
