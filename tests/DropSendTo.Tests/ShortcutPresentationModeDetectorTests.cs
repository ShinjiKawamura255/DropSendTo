using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutPresentationModeDetectorTests
{
    [Theory]
    [InlineData("screenClass")]
    [InlineData("PPTFullScreenClass")]
    public void IsPowerPointSlideShow_ShouldReturnTrue_ForPowerPointSlideShowClass(string className)
    {
        ShortcutPresentationModeDetector.IsPowerPointSlideShow("POWERPNT", className, "Deck")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("PowerPoint Slide Show - Deck")]
    [InlineData("PowerPoint スライド ショー - Deck")]
    [InlineData("PowerPoint スライドショー - Deck")]
    public void IsPowerPointSlideShow_ShouldReturnTrue_ForPowerPointSlideShowTitle(string title)
    {
        ShortcutPresentationModeDetector.IsPowerPointSlideShow("powerpnt", "NormalWindow", title)
            .Should().BeTrue();
    }

    [Fact]
    public void IsPowerPointSlideShow_ShouldReturnFalse_ForNonPowerPointProcess()
    {
        ShortcutPresentationModeDetector.IsPowerPointSlideShow("chrome", "PPTFullScreenClass", "PowerPoint Slide Show")
            .Should().BeFalse();
    }

    [Fact]
    public void IsPowerPointSlideShow_ShouldReturnFalse_ForPowerPointNormalWindow()
    {
        ShortcutPresentationModeDetector.IsPowerPointSlideShow("powerpnt", "PPTFrameClass", "Quarterly Review.pptx")
            .Should().BeFalse();
    }

    [Fact]
    public void IsPowerPointSlideShow_ShouldReturnFalse_ForBlankValues()
    {
        ShortcutPresentationModeDetector.IsPowerPointSlideShow(null, null, null)
            .Should().BeFalse();
    }
}
