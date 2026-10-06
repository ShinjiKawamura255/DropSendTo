using System.Windows;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class CursorWindowPlacementServiceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void CalculatePosition_ShouldCenterOnPhysicalCursor_WhenWindowIsScaled(double scale)
    {
        var result = CursorWindowPlacementService.CalculatePosition(
            new Point(4800, 600), new Size(240, 160), new ScreenBounds(1920, 0, 3840, 2120),
            new DpiScale(scale, scale), CursorWindowPlacement.MouseFollow);

        result.Should().Be(new Point(4800 - 120 * scale, 600 - 80 * scale));
    }

    [Fact]
    public void CalculatePosition_ShouldStayOnCursorScreen_WhenWindowWouldOverlapNeighbor()
    {
        var result = CursorWindowPlacementService.CalculatePosition(
            new Point(-5, -500), new Size(300, 200), new ScreenBounds(-2560, -1440, 2560, 1400),
            new DpiScale(1.5, 1.5), CursorWindowPlacement.MouseFollow);

        result.Should().Be(new Point(-450, -650));
    }

    [Fact]
    public void CalculatePosition_ShouldUseWorkingAreaCenter_WhenScreenHasNegativeOrigin()
    {
        var result = CursorWindowPlacementService.CalculatePosition(
            new Point(-1800, -900), new Size(240, 160), new ScreenBounds(-2560, -1440, 2560, 1400),
            new DpiScale(2, 2), CursorWindowPlacement.ScreenCenter);

        result.Should().Be(new Point(-1520, -900));
    }

    [Theory]
    [InlineData(500, 524)]
    [InlineData(1000, 676)]
    public void CalculatePosition_ShouldScaleDragGapAndPreferAvailableSpace(double cursorY, double expectedTop)
    {
        var result = CursorWindowPlacementService.CalculatePosition(
            new Point(960, cursorY), new Size(240, 200), new ScreenBounds(0, 0, 1920, 1040),
            new DpiScale(1.5, 1.5), CursorWindowPlacement.NearMouse);

        result.Should().Be(new Point(780, expectedTop));
    }
}
