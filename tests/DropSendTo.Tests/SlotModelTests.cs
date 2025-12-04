using System;
using System.IO;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class SlotModelTests
{
    [Fact]
    public void ClickEnabled_Defaults_To_True_And_Persists()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var cfgSvc = new ConfigService(temp);
        var cfg = cfgSvc.LoadOrCreate();
        var slot = cfg.Layers[0].Slots[0];
        slot.ClickEnabled.Should().BeTrue();
        slot.ClickEnabled = false;
        cfgSvc.Save(cfg);

        var cfg2 = cfgSvc.LoadOrCreate();
        cfg2.Layers[0].Slots[0].ClickEnabled.Should().BeFalse();
    }

    [Fact]
    public void KeyboardMacroScript_Defaults_To_Empty_And_Persists()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var cfgSvc = new ConfigService(temp);
        var cfg = cfgSvc.LoadOrCreate();
        var slot = cfg.Layers[1].Slots[2];
        slot.KeyboardMacroScript.Should().Be(string.Empty);

        slot.KeyboardMacroScript = "KEY Ctrl+C";
        cfgSvc.Save(cfg);

        var cfg2 = cfgSvc.LoadOrCreate();
        cfg2.Layers[1].Slots[2].KeyboardMacroScript.Should().Be("KEY Ctrl+C");
    }

    [Fact]
    public void MinimizeOptions_Defaults_To_Disabled_And_Persists()
    {
        var temp = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var cfgSvc = new ConfigService(temp);
        var cfg = cfgSvc.LoadOrCreate();
        var slot = cfg.Layers[0].Slots[0];
        slot.MinimizeOptions.EnableOnClick.Should().BeFalse();
        slot.MinimizeOptions.EnableOnShortcut.Should().BeFalse();
        slot.MinimizeOptions.EnableOnDrop.Should().BeFalse();
        slot.MinimizeOptions.EnableOnKeyboard.Should().BeFalse();

        slot.MinimizeOptions.EnableOnClick = true;
        slot.MinimizeOptions.EnableOnShortcut = true;
        slot.MinimizeOptions.EnableOnKeyboard = true;
        cfgSvc.Save(cfg);

        var cfg2 = cfgSvc.LoadOrCreate();
        cfg2.Layers[0].Slots[0].MinimizeOptions.EnableOnClick.Should().BeTrue();
        cfg2.Layers[0].Slots[0].MinimizeOptions.EnableOnShortcut.Should().BeTrue();
        cfg2.Layers[0].Slots[0].MinimizeOptions.EnableOnKeyboard.Should().BeTrue();
    }
}
