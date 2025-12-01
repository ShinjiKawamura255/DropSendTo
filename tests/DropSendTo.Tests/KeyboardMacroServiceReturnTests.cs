using System.Threading.Tasks;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceReturnTests
{
    [Fact]
    public async Task RunMacroAsync_ShouldStopImmediately_WithReturnMessage()
    {
        var service = new KeyboardMacroService();

        var result = await service.RunMacroAsync("""
SET Foo 1
RETURN "stop here"
SET Foo 2
""");

        result.Success.Should().BeTrue();
        result.IsCanceled.Should().BeFalse();
        result.Message.Should().Be("stop here");
    }

    [Fact]
    public void Validate_Return_WithMessage()
    {
        var ok = KeyboardMacroService.TryValidateScript("RETURN \"done\"", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
    }
}
