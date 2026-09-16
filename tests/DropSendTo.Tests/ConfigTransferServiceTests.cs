using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            StartupBehavior = StartupWindowBehavior.StartInTray,
            LastWindowVisibility = WindowVisibilityState.Tray,
            ShortcutPrefix = "CTRL+ALT+K",
            ShortcutPrefixDisabled = true,
            EnablePrefixDropCapture = false,
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
            WindowPlacementMode = WindowPlacementMode.MouseFollow,
            KeyboardPlacementMode = WindowPlacementMode.CursorScreenCenter,
            MousePlacementMode = WindowPlacementMode.MouseFollow,
            MousePlacementFollowsKeyboard = false,
            MacroConcurrencyMode = MacroConcurrencyMode.SuspendAndResume,
            Language = AppLanguage.English,
            Theme = AppTheme.Light
        };

        config.Layers[0].Slots[0].Title = "Test Slot";
        config.Layers[0].Slots[0].Command = "notepad.exe";
        config.Layers[0].Slots[0].ArgumentsTemplate = "{args}";
        config.Layers[0].Slots[0].ShortcutKey = "Ctrl+1";
        config.Layers[0].Slots[0].KeyboardMacroScript = "KEY A";
        config.Layers[0].Slots[0].AccentColor = SlotAccentColor.Amber;
        config.Layers[0].Slots[0].RunOnStartup = true;
        config.Layers[0].Name = "Layer One";
        config.Layers[1].Name = "Second";

        var payload = service.CreateExportPayload(config, "secret-pass");
        payload.Should().NotBeNullOrWhiteSpace();

        var imported = service.ImportConfig(payload, "secret-pass");
        imported.Should().NotBeNull();
        imported.ShortcutPrefix.Should().Be(config.ShortcutPrefix);
        imported.ShortcutPrefixDisabled.Should().BeTrue();
        imported.AlwaysOnTop.Should().BeFalse();
        imported.StartupBehavior.Should().Be(StartupWindowBehavior.StartInTray);
        imported.LastWindowVisibility.Should().Be(WindowVisibilityState.Tray);
        imported.EnablePrefixDropCapture.Should().BeFalse();
        imported.CurrentLayer.Should().Be(config.CurrentLayer);
        imported.EnableMouseGestures.Should().BeFalse();
        imported.EnableDragMiddleClickShow.Should().BeTrue();
        imported.MouseGestureClockwiseTurnsToShow.Should().Be(5);
        imported.MouseGestureCounterClockwiseTurnsToHide.Should().Be(4);
        imported.MouseGestureInvertDirections.Should().BeTrue();
        imported.MouseGestureRequireCtrl.Should().BeTrue();
        imported.MouseGestureSuppressDuringPresentation.Should().BeTrue();
        imported.MouseGestureEnforceRadiusLimit.Should().BeFalse();
        imported.MouseGestureMinRadiusPixels.Should().Be(80);
        imported.MouseGestureMaxRadiusPixels.Should().Be(180);
        imported.SearchPlacementMode.Should().Be(SearchOverlayPlacementMode.CursorScreenCenter);
        imported.SearchPlacementFollowsKeyboard.Should().BeTrue();
        imported.WindowPlacementMode.Should().Be(WindowPlacementMode.MouseFollow);
        imported.KeyboardPlacementMode.Should().Be(WindowPlacementMode.CursorScreenCenter);
        imported.MousePlacementMode.Should().Be(WindowPlacementMode.MouseFollow);
        imported.MousePlacementFollowsKeyboard.Should().BeFalse();
        imported.MacroConcurrencyMode.Should().Be(MacroConcurrencyMode.SuspendAndResume);
        imported.Language.Should().Be(AppLanguage.English);
        imported.Theme.Should().Be(AppTheme.Light);
        imported.Layers[0].Slots[0].Title.Should().Be("Test Slot");
        imported.Layers[0].Slots[0].KeyboardMacroScript.Should().Be("KEY A");
        imported.Layers[0].Slots[0].AccentColor.Should().Be(SlotAccentColor.Amber);
        imported.Layers[0].Slots[0].RunOnStartup.Should().BeTrue();
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

    [Theory]
    [InlineData(typeof(AppConfig), "ExportConfigSnapshot")]
    [InlineData(typeof(Layer), "ExportLayerSnapshot")]
    [InlineData(typeof(SlotModel), "ExportSlotSnapshot")]
    [InlineData(typeof(SlotMinimizeOptions), "ExportMinimizeOptions")]
    public void ExportSnapshot_ShouldDeclareEveryPersistedProperty(Type modelType, string snapshotTypeName)
    {
        var snapshotType = GetSnapshotType(snapshotTypeName);
        var modelProperties = modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite)
            .Select(property => property.Name);
        var snapshotProperties = snapshotType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite)
            .Select(property => property.Name);

        snapshotProperties.Should().Contain(modelProperties);
    }

    [Fact]
    public void LegacySnapshot_ShouldUseSafeDefaults_WhenNewPropertiesAreMissing()
    {
        var snapshotType = GetSnapshotType("ExportConfigSnapshot");
        var snapshot = JsonSerializer.Deserialize("{\"Version\":37,\"Layers\":[]}", snapshotType);
        snapshot.Should().NotBeNull();

        var imported = snapshotType.GetMethod("ToAppConfig", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(snapshot, null).Should().BeOfType<AppConfig>().Subject;

        imported.WindowPlacementMode.Should().Be(WindowPlacementMode.Fixed);
        imported.MacroConcurrencyMode.Should().Be(MacroConcurrencyMode.Exclusive);
        imported.MouseGestureMinRadiusPixels.Should().Be(0);
    }

    [Fact]
    public void Import_ShouldRejectOversizedPackageBeforeParsing()
    {
        var service = new ConfigTransferService();
        var payload = new string('x', ConfigTransferService.MaxPackageBytes + 1);

        var action = () => service.ImportConfig(payload, "password");

        action.Should().Throw<InvalidOperationException>().WithMessage("*サイズ*");
    }

    [Fact]
    public void Import_ShouldRejectExcessiveKdfIterationsBeforeKeyDerivation()
    {
        var service = new ConfigTransferService();
        var package = JsonNode.Parse(service.CreateExportPayload(new AppConfig(), "password"))!.AsObject();
        package["KdfIterations"] = ConfigTransferService.MaxKdfIterations + 1;

        var action = () => service.ImportConfig(package.ToJsonString(), "password");

        action.Should().Throw<InvalidOperationException>().WithMessage("*KDF*");
    }

    [Theory]
    [InlineData("Salt", 15)]
    [InlineData("Nonce", 11)]
    [InlineData("Tag", 15)]
    public void Import_ShouldRejectInvalidCryptoElementLengthBeforeDecryption(string propertyName, int byteLength)
    {
        var service = new ConfigTransferService();
        var package = JsonNode.Parse(service.CreateExportPayload(new AppConfig(), "password"))!.AsObject();
        package[propertyName] = Convert.ToBase64String(new byte[byteLength]);

        var action = () => service.ImportConfig(package.ToJsonString(), "password");

        action.Should().Throw<InvalidOperationException>().WithMessage($"*{propertyName}*");
    }

    private static Type GetSnapshotType(string name) =>
        typeof(ConfigTransferService).GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Snapshot type not found: {name}");
}
