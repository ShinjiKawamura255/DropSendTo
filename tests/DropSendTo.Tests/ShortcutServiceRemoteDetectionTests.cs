using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutServiceRemoteDetectionTests
{
    [Theory]
    [MemberData(nameof(ExactRemoteClassNames))]
    public void IsRemoteClassName_ShouldMatchExactPatterns(string className)
    {
        ShortcutRemoteSessionMatcher.IsRemoteClassName(className).Should().BeTrue();
    }

    [Theory]
    [InlineData("CitrixHdxDesktopX")]
    [InlineData("CtxEmbeddedWindow")]
    [InlineData("WFICA_Child")]
    [InlineData("HDXOverlayWindow")]
    public void IsRemoteClassName_ShouldMatchWildcardPatterns(string className)
    {
        ShortcutRemoteSessionMatcher.IsRemoteClassName(className).Should().BeTrue();
    }

    [Theory]
    [InlineData("tscshellcontainerclass")]
    [InlineData("citrixhdxclientwindowclass")]
    public void IsRemoteClassName_ShouldIgnoreCase(string className)
    {
        ShortcutRemoteSessionMatcher.IsRemoteClassName(className).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Chrome_WidgetWin_1")]
    public void IsRemoteClassName_ShouldReturnFalse_ForNonRemoteClass(string? className)
    {
        ShortcutRemoteSessionMatcher.IsRemoteClassName(className).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ExactRemoteProcessNames))]
    public void IsRemoteProcessName_ShouldMatchExactPatterns(string processName)
    {
        ShortcutRemoteSessionMatcher.IsRemoteProcessName(processName).Should().BeTrue();
    }

    [Theory]
    [InlineData("CitrixWorkspace")]
    [InlineData("WFICA32")]
    [InlineData("WFCRun32")]
    [InlineData("HDXEngine")]
    public void IsRemoteProcessName_ShouldMatchWildcardPatterns(string processName)
    {
        ShortcutRemoteSessionMatcher.IsRemoteProcessName(processName).Should().BeTrue();
    }

    [Theory]
    [InlineData("MSTSC")]
    [InlineData("CitrixViewer")]
    public void IsRemoteProcessName_ShouldIgnoreCase(string processName)
    {
        ShortcutRemoteSessionMatcher.IsRemoteProcessName(processName).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("explorer")]
    public void IsRemoteProcessName_ShouldReturnFalse_ForNonRemoteProcess(string? processName)
    {
        ShortcutRemoteSessionMatcher.IsRemoteProcessName(processName).Should().BeFalse();
    }

    public static TheoryData<string> ExactRemoteClassNames() => new()
    {
        "TscShellContainerClass",
        "TscShellContainerClass2",
        "TSSHELLWND",
        "TscShellWindowClass",
        "TransparentWndClass",
        "CitrixHDXClientWindowClass",
        "CitrixWorkspaceDesktop",
        "CtxGPCClass",
        "WFICATopLevelWindow",
        "WFICATopLevel"
    };

    public static TheoryData<string> ExactRemoteProcessNames() => new()
    {
        "mstsc",
        "mstsc64",
        "wfica32",
        "wfcrun32",
        "citrixworkspace",
        "citrixviewer",
        "selfserviceplugin",
        "receiver",
        "cdviewer",
        "hdxengine"
    };
}
