using System.Windows;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ScreenBoundsResolverTests
{
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
