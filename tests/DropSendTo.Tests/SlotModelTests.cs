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
        var cfgSvc = new ConfigService();
        var cfg = cfgSvc.LoadOrCreate();
        var slot = cfg.Layers[0].Slots[0];
        slot.ClickEnabled.Should().BeTrue();
        slot.ClickEnabled = false;
        cfgSvc.Save(cfg);

        var cfg2 = cfgSvc.LoadOrCreate();
        cfg2.Layers[0].Slots[0].ClickEnabled.Should().BeFalse();
    }
}

