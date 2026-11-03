using System.Windows;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class OwnedWindowPlacementServiceTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1.5, 1)]
    [InlineData(1, 1.5)]
    [InlineData(1.25, 2)]
    [InlineData(2, 1.25)]
    public void CalculatePosition_ShouldAnchorInPhysicalPixels_WhenOwnerAndOverlayDpiDiffer(
        double ownerScale, double overlayScale)
    {
        var result = OwnedWindowPlacementService.CalculatePosition(new Rect(4300, 900, 360, 240),
            new Size(200, 40), new ScreenBounds(2560, 0, 3840, 2100),
            new DpiScale(ownerScale, ownerScale), new DpiScale(overlayScale, overlayScale));
        result.Position.Should().Be(new Point(4480 - 100 * overlayScale,
            900 - 40 * overlayScale - 10 * ownerScale));
        result.PlacedBelow.Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void CalculatePosition_ShouldPlaceBelow_WhenOwnerIsAtTopOfNegativeMonitor(double scale)
    {
        var result = OwnedWindowPlacementService.CalculatePosition(new Rect(-1800, -1440, 360, 240),
            new Size(200, 40), new ScreenBounds(-2560, -1440, 2560, 1400),
            new DpiScale(scale, scale), new DpiScale(scale, scale));
        result.Position.Should().Be(new Point(-1620 - 100 * scale, -1200 + 10 * scale));
        result.PlacedBelow.Should().BeTrue();
    }

    [Theory]
    [InlineData(-2550, -2560)]
    [InlineData(-100, -400)]
    public void CalculatePosition_ShouldClampWideOverlay_ToOwnersWorkingArea(double ownerLeft, double expectedLeft)
    {
        var result = OwnedWindowPlacementService.CalculatePosition(new Rect(ownerLeft, -800, 100, 200),
            new Size(200, 40), new ScreenBounds(-2560, -1440, 2560, 1400),
            new DpiScale(1.5, 1.5), new DpiScale(2, 2));
        result.Position.X.Should().Be(expectedLeft);
        result.Position.Y.Should().Be(-895);
        result.PlacedBelow.Should().BeFalse();
    }
}
