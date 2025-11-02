using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ConfigTransferServiceTests
{
    [Fact]
    public void ExportImport_ShouldRoundTripConfig()
    {
        var service = new ConfigTransferService();
        var config = new AppConfig
        {
            Version = 10,
            CurrentLayer = 2,
            WindowLeft = 123.4,
            WindowTop = 56.7,
            AlwaysOnTop = false,
            ShortcutPrefix = "CTRL+ALT+K",
            ShortcutPrefixDisabled = true,
            SlotRows = 3,
            SlotColumns = 3,
            SlotSize = SlotSize.Small
        };

        config.Layers[0].Slots[0].Title = "Test Slot";
        config.Layers[0].Slots[0].Command = "notepad.exe";
        config.Layers[0].Slots[0].ArgumentsTemplate = "{args}";
        config.Layers[0].Slots[0].ShortcutKey = "Ctrl+1";
        config.Layers[0].Slots[0].KeyboardMacroScript = "KEY A";

        var payload = service.CreateExportPayload(config, "secret-pass");
        payload.Should().NotBeNullOrWhiteSpace();

        var imported = service.ImportConfig(payload, "secret-pass");
        imported.Should().NotBeNull();
        imported.ShortcutPrefix.Should().Be(config.ShortcutPrefix);
        imported.ShortcutPrefixDisabled.Should().BeTrue();
        imported.AlwaysOnTop.Should().BeFalse();
        imported.CurrentLayer.Should().Be(config.CurrentLayer);
        imported.Layers[0].Slots[0].Title.Should().Be("Test Slot");
        imported.Layers[0].Slots[0].KeyboardMacroScript.Should().Be("KEY A");
    }

    [Fact]
    public void Import_ShouldFailWithWrongPassword()
    {
        var service = new ConfigTransferService();
        var payload = service.CreateExportPayload(new AppConfig(), "correct");

        var action = () => service.ImportConfig(payload, "wrong");

        action.Should().Throw<InvalidOperationException>().WithMessage("*パスワード*");
    }
}
