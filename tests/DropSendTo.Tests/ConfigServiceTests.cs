using System;
using System.IO;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ConfigServiceTests
{
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
        }
        cfg.AlwaysOnTop.Should().BeTrue();
        cfg.Version.Should().Be(3);
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
        reloaded.Version.Should().Be(3);
    }
}
