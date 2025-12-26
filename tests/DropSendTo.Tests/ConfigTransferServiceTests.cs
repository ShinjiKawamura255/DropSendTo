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
            Version = 11,
            CurrentLayer = 2,
            WindowLeft = 123.4,
            WindowTop = 56.7,
            AlwaysOnTop = false,
            StartupBehavior = StartupWindowBehavior.RestoreLastState,
            LastWindowVisibility = WindowVisibilityState.Tray,
            ShortcutPrefix = "CTRL+ALT+K",
            ShortcutPrefixDisabled = true,
            EnablePrefixLayerShortcuts = true,
            SlotRows = 3,
            SlotColumns = 3,
            SlotSize = SlotSize.Medium,
            EnableMouseGestures = false,
            EnableDragMiddleClickShow = true,
            MouseGestureClockwiseTurnsToShow = 5,
            MouseGestureCounterClockwiseTurnsToHide = 4,
            MouseGestureInvertDirections = true,
            MouseGestureRequireCtrl = true,
            MouseGestureSuppressDuringPresentation = true,
            MouseGestureEnforceRadiusLimit = false,
            MouseGestureMinRadiusPixels = 80,
            MouseGestureMaxRadiusPixels = 180,
            SearchPlacementMode = SearchOverlayPlacementMode.CursorScreenCenter,
            SearchPlacementFollowsKeyboard = true,
            KeyboardPlacementMode = WindowPlacementMode.CursorScreenCenter,
            Language = AppLanguage.English
        };

        config.Layers[0].Slots[0].Title = "Test Slot";
        config.Layers[0].Slots[0].Command = "notepad.exe";
        config.Layers[0].Slots[0].ArgumentsTemplate = "{args}";
        config.Layers[0].Slots[0].ShortcutKey = "Ctrl+1";
        config.Layers[0].Slots[0].KeyboardMacroScript = "KEY A";
        config.Layers[0].Slots[0].AccentColor = SlotAccentColor.Amber;
        config.Layers[0].Name = "Layer One";
        config.Layers[1].Name = "Second";

        var payload = service.CreateExportPayload(config, "secret-pass");
        payload.Should().NotBeNullOrWhiteSpace();

        var imported = service.ImportConfig(payload, "secret-pass");
        imported.Should().NotBeNull();
        imported.ShortcutPrefix.Should().Be(config.ShortcutPrefix);
        imported.ShortcutPrefixDisabled.Should().BeTrue();
        imported.AlwaysOnTop.Should().BeFalse();
        imported.StartupBehavior.Should().Be(StartupWindowBehavior.RestoreLastState);
        imported.LastWindowVisibility.Should().Be(WindowVisibilityState.Tray);
        imported.EnablePrefixLayerShortcuts.Should().BeTrue();
        imported.CurrentLayer.Should().Be(config.CurrentLayer);
        imported.EnableMouseGestures.Should().BeFalse();
        imported.EnableDragMiddleClickShow.Should().BeTrue();
        imported.MouseGestureClockwiseTurnsToShow.Should().Be(5);
        imported.MouseGestureCounterClockwiseTurnsToHide.Should().Be(4);
        imported.MouseGestureInvertDirections.Should().BeTrue();
        imported.MouseGestureRequireCtrl.Should().BeTrue();
        imported.MouseGestureSuppressDuringPresentation.Should().BeTrue();
        imported.MouseGestureEnforceRadiusLimit.Should().BeFalse();
        imported.MouseGestureMaxRadiusPixels.Should().Be(180);
        imported.SearchPlacementMode.Should().Be(SearchOverlayPlacementMode.CursorScreenCenter);
        imported.SearchPlacementFollowsKeyboard.Should().BeTrue();
        imported.Language.Should().Be(AppLanguage.English);
        imported.Layers[0].Slots[0].Title.Should().Be("Test Slot");
        imported.Layers[0].Slots[0].KeyboardMacroScript.Should().Be("KEY A");
        imported.Layers[0].Slots[0].AccentColor.Should().Be(SlotAccentColor.Amber);
        imported.Layers[0].Name.Should().Be("Layer One");
        imported.Layers[1].Name.Should().Be("Second");
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
