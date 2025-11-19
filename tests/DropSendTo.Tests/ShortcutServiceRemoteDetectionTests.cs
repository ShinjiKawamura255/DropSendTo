using System.Reflection;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutServiceRemoteDetectionTests
{
    private static readonly MethodInfo IsRemoteClassMethod =
        typeof(ShortcutService).GetMethod("IsRemoteClassName", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo IsRemoteProcessMethod =
        typeof(ShortcutService).GetMethod("IsRemoteProcessName", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Theory]
    [InlineData("CitrixHdxDesktopX")]
    [InlineData("CtxEmbeddedWindow")]
    [InlineData("WFICA_Child")]
    public void IsRemoteClassName_ShouldMatchCitrixPatterns(string className)
    {
        InvokeClassCheck(className).Should().BeTrue();
    }

    [Fact]
    public void IsRemoteClassName_ShouldReturnFalse_ForUnrelatedClass()
    {
        InvokeClassCheck("Chrome_WidgetWin_1").Should().BeFalse();
    }

    [Theory]
    [InlineData("CitrixWorkspace")]
    [InlineData("WFICA32")]
    [InlineData("SelfServicePlugin")]
    public void IsRemoteProcessName_ShouldMatchCitrixProcesses(string processName)
    {
        InvokeProcessCheck(processName).Should().BeTrue();
    }

    [Fact]
    public void IsRemoteProcessName_ShouldReturnFalse_ForUnrelatedProcess()
    {
        InvokeProcessCheck("explorer").Should().BeFalse();
    }

    private static bool InvokeClassCheck(string? className) =>
        (bool)IsRemoteClassMethod.Invoke(null, new object?[] { className })!;

    private static bool InvokeProcessCheck(string? processName) =>
        (bool)IsRemoteProcessMethod.Invoke(null, new object?[] { processName })!;
}
