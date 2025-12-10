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
        KeyboardMacroService.SetInputPromptOverrideForTesting((message, title, defaultValue) =>
        {
            message.Should().Contain("入力");
            title.Should().NotBeNull();
            defaultValue.Should().Be("Alice");
            return (true, "Bob");
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
        KeyboardMacroService.SetInputPromptOverrideForTesting((_, _, _) => (false, null));

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
}
