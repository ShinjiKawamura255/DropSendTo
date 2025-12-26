using System;
using System.Threading.Tasks;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

[Collection("KeyboardMacroServiceTests")]
public class KeyboardMacroServicePromptTests
{
    [Fact]
    public async Task RunMacroAsync_ShouldStorePromptResult_WhenConfirmed()
    {
        using var service = new KeyboardMacroService();
        KeyboardMacroService.SetInputPromptOverrideForTesting((message, title, defaultValue, timeout, timeoutValue) =>
        {
            message.Should().Contain("入力");
            title.Should().NotBeNull();
            defaultValue.Should().Be("Alice");
            timeout.Should().BeNull();
            timeoutValue.Should().BeNull();
            return (KeyboardMacroService.PromptOutcome.Confirmed, "Bob");
        });

        try
        {
            var result = await service.RunMacroAsync("""
SET DefaultName Alice
PROMPT Name "名前を入力してください" DEFAULT "{{DefaultName}}"
RETURN "{{Name}}"
""");

            result.Success.Should().BeTrue();
            result.Message.Should().Be("Bob");
        }
        finally
        {
            KeyboardMacroService.SetInputPromptOverrideForTesting(null);
        }
    }

    [Fact]
    public async Task RunMacroAsync_ShouldFail_WhenPromptCanceled()
    {
        using var service = new KeyboardMacroService();
        KeyboardMacroService.SetInputPromptOverrideForTesting((_, _, _, _, _) => (KeyboardMacroService.PromptOutcome.Canceled, null));

        try
        {
            var result = await service.RunMacroAsync("PROMPT Value \"値を入力してください\"");

            result.Success.Should().BeFalse();
            result.IsCanceled.Should().BeFalse();
            result.Message.Should().Contain("PROMPT");
        }
        finally
        {
            KeyboardMacroService.SetInputPromptOverrideForTesting(null);
        }
    }

    [Fact]
    public async Task RunMacroAsync_ShouldUseTimeoutValue_WhenPromptTimesOut()
    {
        using var service = new KeyboardMacroService();
        KeyboardMacroService.SetInputPromptOverrideForTesting((_, _, defaultValue, timeout, timeoutValue) =>
        {
            defaultValue.Should().BeNull();
            timeout.Should().Be(TimeSpan.FromMilliseconds(1000));
            timeoutValue.Should().Be("Timed");
            return (KeyboardMacroService.PromptOutcome.TimedOut, null);
        });

        try
        {
            var result = await service.RunMacroAsync("""
PROMPT Value "値を入力してください" TIMEOUT 1000 "Timed"
RETURN "{{Value}}"
""");

            result.Success.Should().BeTrue();
            result.Message.Should().Be("Timed");
        }
        finally
        {
            KeyboardMacroService.SetInputPromptOverrideForTesting(null);
        }
    }
}
