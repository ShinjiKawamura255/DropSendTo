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
        var svc = new ConfigService();
        var cfg = svc.LoadOrCreate();
        cfg.Layers.Count.Should().Be(4);
        foreach (var layer in cfg.Layers)
        {
            layer.Slots.Count.Should().Be(4);
        }
    }
}

