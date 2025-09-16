using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class PlacementServiceTests
{
    [Fact]
    public void Clamp_Should_Keep_Window_Inside_Bounds()
    {
        var svc = new WindowPlacementService();
        var b = new ScreenBounds(0, 0, 1920, 1080);

        var p1 = svc.Clamp(-100, -100, b, 260, 260);
        p1.left.Should().BeGreaterOrEqualTo(0);
        p1.top.Should().BeGreaterOrEqualTo(0);

        var p2 = svc.Clamp(3000, 3000, b, 260, 260);
        p2.left.Should().BeLessOrEqualTo(b.Left + b.Width - 1);
        p2.top.Should().BeLessOrEqualTo(b.Top + b.Height - 1);
    }
}

