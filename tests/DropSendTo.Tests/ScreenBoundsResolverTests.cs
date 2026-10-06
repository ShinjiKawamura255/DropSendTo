using System.Windows;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ScreenBoundsResolverTests
{
    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void SelectBounds_ShouldUseReferenceDpiForAllScreens_WhenDesktopHasFiveScreens(double scale)
    {
        var screens = new[]
        {
            new ScreenBounds(5760, -1080, 1920, 1040),
            new ScreenBounds(-2560, -1440, 2560, 1400),
            new ScreenBounds(1920, 0, 3840, 2120),
            new ScreenBounds(0, 0, 1920, 1040),
            new ScreenBounds(-1920, 0, 1920, 1040)
        };
        // WPF coordinates belong to the reference window's DPI, not each monitor's DPI.
        var rect = new Rect(5000 / scale, 300 / scale, 400 / scale, 300 / scale);

        var result = ScreenBoundsResolver.SelectBounds(rect, screens, new DpiScale(scale, scale));

        result.Should().Be(new ScreenBounds(1920 / scale, 0, 3840 / scale, 2120 / scale));
        ScreenBoundsResolver.SelectBounds(rect, screens.Reverse().ToArray(), new DpiScale(scale, scale))
            .Should().Be(result);
    }

    [Fact]
    public void SelectBounds_ShouldPreferScreenWithGreaterOverlap()
    {
        var screens = new[]
        {
            new ScreenBounds(-1920, 0, 1920, 1080),
            new ScreenBounds(0, 0, 1920, 1080)
        };

        var rect = new Rect(-400, 100, 500, 400);

        var result = ScreenBoundsResolver.SelectBounds(rect, screens);

        result.Left.Should().Be(-1920);
    }

    [Fact]
    public void SelectBounds_ShouldKeepWindowInsideExistingScreen()
    {
        var screens = new[]
        {
            new ScreenBounds(-1920, 0, 1920, 1080),
            new ScreenBounds(0, 0, 1920, 1080)
        };

        var rect = new Rect(200, 200, 800, 600);

        var result = ScreenBoundsResolver.SelectBounds(rect, screens);

        result.Left.Should().Be(0);
    }

    [Fact]
    public void SelectBounds_ShouldFallbackToNearestScreenWhenNoOverlap()
    {
        var screens = new[]
        {
            new ScreenBounds(-1920, 0, 1920, 1080),
            new ScreenBounds(0, 0, 1920, 1080)
        };

        var rect = new Rect(2600, 200, 400, 400);

        var result = ScreenBoundsResolver.SelectBounds(rect, screens);

        result.Left.Should().Be(0);
    }
}
