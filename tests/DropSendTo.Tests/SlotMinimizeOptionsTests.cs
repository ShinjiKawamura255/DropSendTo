using DropSendTo.Models;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class SlotMinimizeOptionsTests
{
    [Fact]
    public void Default_DoesNotMinimize()
    {
        var opt = SlotMinimizeOptions.CreateDefault();

        opt.ShouldMinimizeAfter(SlotTriggerKind.Click).Should().BeFalse();
        opt.ShouldMinimizeAfter(SlotTriggerKind.Shortcut).Should().BeFalse();
        opt.ShouldMinimizeAfter(SlotTriggerKind.Drop).Should().BeFalse();
        opt.ShouldMinimizeAfter(SlotTriggerKind.Keyboard).Should().BeFalse();
    }

    [Fact]
    public void EnableOnClickOnly()
    {
        var opt = new SlotMinimizeOptions
        {
            EnableOnClick = true
        };

        opt.ShouldMinimizeAfter(SlotTriggerKind.Click).Should().BeTrue();
        opt.ShouldMinimizeAfter(SlotTriggerKind.Shortcut).Should().BeFalse();
        opt.ShouldMinimizeAfter(SlotTriggerKind.Drop).Should().BeFalse();
        opt.ShouldMinimizeAfter(SlotTriggerKind.Keyboard).Should().BeFalse();
    }

    [Fact]
    public void EnableOnShortcutAndDrop()
    {
        var opt = new SlotMinimizeOptions
        {
            EnableOnShortcut = true,
            EnableOnDrop = true,
            EnableOnKeyboard = true
        };

        opt.ShouldMinimizeAfter(SlotTriggerKind.Shortcut).Should().BeTrue();
        opt.ShouldMinimizeAfter(SlotTriggerKind.Drop).Should().BeTrue();
        opt.ShouldMinimizeAfter(SlotTriggerKind.Keyboard).Should().BeTrue();
        opt.ShouldMinimizeAfter(SlotTriggerKind.Click).Should().BeFalse();
    }
}
