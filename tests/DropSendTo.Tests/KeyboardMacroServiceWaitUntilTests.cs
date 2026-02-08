using System.Threading.Tasks;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceWaitUntilTests
{
    [Fact]
    public async Task RunMacroAsync_ShouldContinue_WhenWaitUntilConditionAlreadyTrue()
    {
        using var service = new KeyboardMacroService();

        var result = await service.RunMacroAsync("""
SET Ready 1
WAIT_UNTIL {{Ready}} == 1 TIMEOUT 100 INTERVAL 10
RETURN "ok"
""");

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("ok");
    }

    [Fact]
    public async Task RunMacroAsync_ShouldFail_WhenWaitUntilTimedOut()
    {
        using var service = new KeyboardMacroService();

        var result = await service.RunMacroAsync("""
WAIT_UNTIL 0 TIMEOUT 30 INTERVAL 10
RETURN "unreachable"
""");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("タイムアウト");
    }
}
