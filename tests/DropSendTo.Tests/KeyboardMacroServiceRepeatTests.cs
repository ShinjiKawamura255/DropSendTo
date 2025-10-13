using System.Collections.Generic;
using System.Reflection;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceRepeatTests
{
    private static MethodInfo GetExpandMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod("TryExpandRepeatBlocks", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("REPEAT ブロック展開の内部メソッドが存在すること");
        return method!;
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldRepeatInnerLines_AsConfigured()
    {
        var method = GetExpandMethod();
        var lines = new[] { "TEXT One", "REPEAT 3", "TEXT Two", "ENDREPEAT" };
        var args = new object?[] { lines, null, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        var expanded = args[1].Should().BeAssignableTo<List<string>>().Subject;
        expanded.Should().Equal("TEXT One", "TEXT Two", "TEXT Two", "TEXT Two");
        args[2].Should().BeNull();
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldSupportNestedRepeats()
    {
        var method = GetExpandMethod();
        var lines = new[]
        {
            "REPEAT 2",
            "TEXT Outer",
            "REPEAT 2",
            "TEXT Inner",
            "ENDREPEAT",
            "ENDREPEAT"
        };
        var args = new object?[] { lines, null, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        var expanded = args[1].Should().BeAssignableTo<List<string>>().Subject;
        expanded.Should().Equal(
            "TEXT Outer",
            "TEXT Inner",
            "TEXT Inner",
            "TEXT Outer",
            "TEXT Inner",
            "TEXT Inner");
        args[2].Should().BeNull();
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldAllowZeroRepeat()
    {
        var method = GetExpandMethod();
        var lines = new[] { "TEXT Begin", "REPEAT 0", "TEXT Skipped", "ENDREPEAT", "TEXT End" };
        var args = new object?[] { lines, null, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        var expanded = args[1].Should().BeAssignableTo<List<string>>().Subject;
        expanded.Should().Equal("TEXT Begin", "TEXT End");
        args[2].Should().BeNull();
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldFail_WhenCountIsInvalid()
    {
        var method = GetExpandMethod();
        var lines = new[] { "REPEAT foo", "TEXT Sample", "ENDREPEAT" };
        var args = new object?[] { lines, null, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeFalse();
        args[2].Should().BeOfType<string>()
            .Subject.Should().Contain("REPEAT の回数指定が不正です");
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldFail_WhenEndRepeatIsMissing()
    {
        var method = GetExpandMethod();
        var lines = new[] { "REPEAT 2", "TEXT Sample" };
        var args = new object?[] { lines, null, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeFalse();
        args[2].Should().BeOfType<string>()
            .Subject.Should().Contain("REPEAT ブロックが ENDREPEAT で閉じられていません");
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldFail_WhenRepeatCountIsTooLarge()
    {
        var method = GetExpandMethod();
        var lines = new[] { "REPEAT 1001", "TEXT Sample", "ENDREPEAT" };
        var args = new object?[] { lines, null, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeFalse();
        args[2].Should().BeOfType<string>()
            .Subject.Should().Contain("REPEAT に指定できる回数は 0〜1000 です");
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldFail_WhenEndRepeatIsUnmatched()
    {
        var method = GetExpandMethod();
        var lines = new[] { "TEXT Sample", "ENDREPEAT" };
        var args = new object?[] { lines, null, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeFalse();
        args[2].Should().BeOfType<string>()
            .Subject.Should().Contain("ENDREPEAT に対応する REPEAT が見つかりません");
    }
}
