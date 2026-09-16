using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class LayerButtonModelFactoryTests
{
    [Fact]
    public void Create_ShouldReturnEveryLayer_WhenLayersFitAvailableButtons()
    {
        var models = LayerButtonModelFactory.Create(
            totalLayers: 4,
            currentLayer: 2,
            buttonCount: 4,
            navigationDirection: 0);

        models.Should().HaveCount(4);
        models.Should().OnlyContain(model => model.Visible && model.IsLayer);
        models.Select(model => model.LayerIndex).Should().Equal(0, 1, 2, 3);
        models.Select(model => model.Content).Should().Equal("1", "2", "3", "4");
    }

    [Theory]
    [InlineData(5, 0, 0, "1|2|3|▶")]
    [InlineData(5, 2, 1, "◀|2|3|▶")]
    [InlineData(5, 2, -1, "◀|3|4|▶")]
    [InlineData(6, 3, 1, "◀|3|4|▶")]
    [InlineData(7, 3, -1, "◀|4|5|▶")]
    [InlineData(8, 7, 1, "◀|6|7|8")]
    public void Create_ShouldPreserveNavigationWindow(
        int totalLayers,
        int currentLayer,
        int navigationDirection,
        string expectedContents)
    {
        var models = LayerButtonModelFactory.Create(
            totalLayers,
            currentLayer,
            buttonCount: 4,
            navigationDirection);

        models.Should().HaveCount(4);
        string.Join('|', models.Select(model => model.Content)).Should().Be(expectedContents);
        models.Should().OnlyContain(model => model.Visible);
    }

    [Fact]
    public void Create_ShouldPadUnusedButtonsWithHiddenModels()
    {
        var models = LayerButtonModelFactory.Create(
            totalLayers: 4,
            currentLayer: 0,
            buttonCount: 6,
            navigationDirection: 0);

        models.Take(4).Should().OnlyContain(model => model.Visible && model.IsLayer);
        models.Skip(4).Should().OnlyContain(model => !model.Visible);
    }

    [Fact]
    public void Create_ShouldReturnOnlyHiddenModels_WhenNoLayersExist()
    {
        var models = LayerButtonModelFactory.Create(
            totalLayers: 0,
            currentLayer: 0,
            buttonCount: 4,
            navigationDirection: 0);

        models.Should().HaveCount(4);
        models.Should().OnlyContain(model => !model.Visible);
    }
}
