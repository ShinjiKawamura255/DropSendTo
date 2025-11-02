using System.Collections.Generic;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class MacroRecordingOptimizerTests
{
    [Fact]
    public void Optimize_ShouldCollapseModifierTapToKey()
    {
        var events = new List<MacroRecordingEvent>
        {
            MacroRecordingEvent.KeyDown("Ctrl", ModifierKind.Control),
            MacroRecordingEvent.KeyUp("Ctrl", ModifierKind.Control)
        };

        var result = MacroRecordingOptimizer.Optimize(events);

        result.Should().Equal("KEY Ctrl");
    }

    [Fact]
    public void Optimize_ShouldCollapseSingleComboToKeyCommand()
    {
        var events = new List<MacroRecordingEvent>
        {
            MacroRecordingEvent.KeyDown("Ctrl", ModifierKind.Control),
            MacroRecordingEvent.KeyDown("C", null),
            MacroRecordingEvent.KeyUp("C", null),
            MacroRecordingEvent.KeyUp("Ctrl", ModifierKind.Control)
        };

        var result = MacroRecordingOptimizer.Optimize(events);

        result.Should().Equal("KEY Ctrl+C");
    }

    [Fact]
    public void Optimize_ShouldHoldModifierWhenUsedMultipleTimes()
    {
        var events = new List<MacroRecordingEvent>
        {
            MacroRecordingEvent.KeyDown("Alt", ModifierKind.Alt),
            MacroRecordingEvent.KeyDown("Tab", null),
            MacroRecordingEvent.KeyUp("Tab", null),
            MacroRecordingEvent.KeyDown("Tab", null),
            MacroRecordingEvent.KeyUp("Tab", null),
            MacroRecordingEvent.KeyUp("Alt", ModifierKind.Alt)
        };

        var result = MacroRecordingOptimizer.Optimize(events);

        result.Should().Equal(new[]
        {
            "KEYDOWN Alt",
            "KEY Tab",
            "KEY Tab",
            "KEYUP Alt"
        });
    }

    [Fact]
    public void Optimize_ShouldIncludeMultipleModifiersInCombination()
    {
        var events = new List<MacroRecordingEvent>
        {
            MacroRecordingEvent.KeyDown("Ctrl", ModifierKind.Control),
            MacroRecordingEvent.KeyDown("Shift", ModifierKind.Shift),
            MacroRecordingEvent.KeyDown("Esc", null),
            MacroRecordingEvent.KeyUp("Esc", null),
            MacroRecordingEvent.KeyUp("Shift", ModifierKind.Shift),
            MacroRecordingEvent.KeyUp("Ctrl", ModifierKind.Control)
        };

        var result = MacroRecordingOptimizer.Optimize(events);

        result.Should().Equal("KEY Ctrl+Shift+Esc");
    }

    [Fact]
    public void Optimize_ShouldConvertSingleKeyTapWithoutModifiers()
    {
        var events = new List<MacroRecordingEvent>
        {
            MacroRecordingEvent.KeyDown("A", null),
            MacroRecordingEvent.KeyUp("A", null)
        };

        var result = MacroRecordingOptimizer.Optimize(events);

        result.Should().Equal("KEY A");
    }
}
