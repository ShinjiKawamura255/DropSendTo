using System.Collections.Generic;
using System.Reflection;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceForeachDropTests
{
    private static MethodInfo GetExpandMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod("TryExpandForeachDropBlocks", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("FOREACH_DROP ブロック展開の内部メソッドが存在すること");
        return method!;
    }

    [Fact]
    public void TryExpandForeachDropBlocks_ShouldUnrollForEachDrop()
    {
        var method = GetExpandMethod();
        var lines = new[]
        {
            "TEXT Start",
            "FOREACH_DROP Item INDEX idx",
            "TEXT {{Item}}",
            "ENDFOREACH",
            "TEXT End"
        };
        var drops = new List<string> { @"C:\One.txt", @"D:\Two.txt" };
        var args = new object?[] { lines, drops, null, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeTrue();
        var expanded = args[2].Should().BeAssignableTo<List<string>>().Subject;
        expanded.Should().Equal(
            "TEXT Start",
            "SET Item {{drop_path:1}}",
            "SET idx 1",
            "TEXT {{Item}}",
            "SET Item {{drop_path:2}}",
            "SET idx 2",
            "TEXT {{Item}}",
            "TEXT End");
        args[3].Should().BeNull();
    }

    [Fact]
    public void TryExpandForeachDropBlocks_ShouldSkipBlock_WhenNoDrops()
    {
        var method = GetExpandMethod();
        var lines = new[]
        {
            "TEXT Before",
            "FOREACH_DROP Item",
            "TEXT {{Item}}",
            "ENDFOREACH",
            "TEXT After"
        };
        var drops = new List<string>();
        var args = new object?[] { lines, drops, null, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeTrue();
        var expanded = args[2].Should().BeAssignableTo<List<string>>().Subject;
        expanded.Should().Equal("TEXT Before", "TEXT After");
        args[3].Should().BeNull();
    }

    [Fact]
    public void TryExpandForeachDropBlocks_ShouldFail_WhenBlockNotClosed()
    {
        var method = GetExpandMethod();
        var lines = new[]
        {
            "FOREACH_DROP Item",
            "TEXT {{Item}}"
        };
        var drops = new List<string> { "a" };
        var args = new object?[] { lines, drops, null, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeFalse();
        args[3].Should().BeOfType<string>()
            .Subject.Should().Contain("FOREACH_DROP").And.Contain("ENDFOREACH");
        args[2].Should().BeAssignableTo<List<string>>()
            .Subject.Should().BeEmpty();
    }
}
