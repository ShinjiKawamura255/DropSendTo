using System;
using System.IO;
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
            }
        }
        cfg.AlwaysOnTop.Should().BeTrue();
        cfg.Version.Should().Be(CurrentConfigVersion);
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
        foreach (var layer in cfg.Layers)
        foreach (var slot in layer.Slots)
        {
            slot.KeyboardMacroScript.Should().Be(string.Empty);
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
}
