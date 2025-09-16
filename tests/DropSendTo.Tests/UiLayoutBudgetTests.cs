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
        double menuBtn = 28;            // menu button width
        double menuMargins = 2 + 14 + 2; // left + right (14) + vertical ignored
        double layerBtn = 24;           // each layer button width
        double layerMargin = 2 * 2;     // left+right margin around button
        int layerCount = 4;

        double contentWidth = windowWidth - borderPadding;
        double needed = handleMin + menuBtn + menuMargins + (layerBtn + layerMargin) * layerCount;

        needed.Should().BeLessOrEqualTo(contentWidth);
    }
}

