using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

[Collection("KeyboardMacroServiceTests")]
public class KeyboardMacroServicePrefixPassthroughTests
{
    [Fact]
    public async Task RunMacroAsync_ShouldSendPassthroughDirectly_WhenMacroRunning()
    {
        using var service = new KeyboardMacroService();
        KeyChordParser.TryParse("CTRL+Q", out var chord, out var error).Should().BeTrue($"prefix chord should parse: {error}");
        service.SetPrefixStateAccessors(() => chord, () => false, () => { });

        var cts = new CancellationTokenSource();
        SetField(service, "_currentMacroCts", cts);
        SetField(service, "_macroRunningFlag", 1);

        int invocationCount = 0;
        KeyboardMacroService.SetSendInputOverrideForTesting((count, size) =>
        {
            invocationCount++;
            return count;
        });

        try
        {
            var result = await service.RunMacroAsync("PREFIX PASSTHROUGH");

            result.Success.Should().BeTrue();
            invocationCount.Should().Be(1);
        }
        finally
        {
            KeyboardMacroService.SetSendInputOverrideForTesting(null);
            SetField(service, "_macroRunningFlag", 0);
            SetField(service, "_currentMacroCts", null);
            cts.Dispose();
        }
    }

    [Fact]
    public async Task RunMacroAsync_ShouldReportFailure_WhenPassthroughSendFails()
    {
        using var service = new KeyboardMacroService();
        KeyChordParser.TryParse("CTRL+Q", out var chord, out var error).Should().BeTrue($"prefix chord should parse: {error}");
        service.SetPrefixStateAccessors(() => chord, () => false, () => { });

        var cts = new CancellationTokenSource();
        SetField(service, "_currentMacroCts", cts);
        SetField(service, "_macroRunningFlag", 1);

        KeyboardMacroService.SetSendInputOverrideForTesting((_, _) => 0);

        try
        {
            var result = await service.RunMacroAsync("PREFIX PASSTHROUGH");

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("SendInput");
        }
        finally
        {
            KeyboardMacroService.SetSendInputOverrideForTesting(null);
            SetField(service, "_macroRunningFlag", 0);
            SetField(service, "_currentMacroCts", null);
            cts.Dispose();
        }
    }

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field {name} should exist");
        field!.SetValue(target, value);
    }
}
