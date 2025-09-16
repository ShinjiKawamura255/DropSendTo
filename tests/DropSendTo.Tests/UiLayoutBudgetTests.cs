using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class UiLayoutBudgetTests
{
    [Fact]
    public void TopBar_Should_Fit_Within_Window_Width()
    {
        // These values mirror XAML constants in MainWindow.xaml
        double windowWidth = 246;       // Window Width
        double borderPadding = 6 * 2;   // left+right padding of outer Border
        double handleMin = 60;          // drag handle min width
        double layerBtn = 24;           // each button width (menu + layers)
        double layerMargin = 2 * 2;     // left+right margin around button
        int buttonCount = 5;            // menu + 4 layers

        double contentWidth = windowWidth - borderPadding;
        double needed = handleMin + (layerBtn + layerMargin) * buttonCount;

        needed.Should().BeLessOrEqualTo(contentWidth);
    }
}
