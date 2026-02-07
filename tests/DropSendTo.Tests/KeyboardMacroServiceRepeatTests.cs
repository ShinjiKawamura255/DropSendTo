using System;
using System.Collections.Generic;
using System.Reflection;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceRepeatTests
{
    private static MethodInfo GetExpandMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod("TryExpandRepeatAt", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("REPEAT ブロック展開の内部メソッドが存在すること");
        return method!;
    }

    private static bool TryExpandAllRepeats(List<string> lines, IReadOnlyDictionary<string, string> variables, out string? error)
    {
        error = null;
        var method = GetExpandMethod();
        for (int i = 0; i < lines.Count; i++)
        {
            if (!IsRepeatLine(lines[i]))
            {
                continue;
            }

            var args = new object?[] { lines, i, variables, null, null, null, null };
            var result = (bool)method.Invoke(null, args)!;
            if (!result)
            {
                error = args[5] as string;
                return false;
            }

            i = (int)args[4]!;
        }

        return true;
    }

    private static bool IsRepeatLine(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("REPEAT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length == 6)
        {
            return true;
        }

        var next = trimmed[6];
        return char.IsWhiteSpace(next) || !char.IsLetter(next);
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldRepeatInnerLines_AsConfigured()
    {
        var lines = new List<string> { "TEXT One", "REPEAT 3", "TEXT Two", "ENDREPEAT" };

        var result = TryExpandAllRepeats(lines, new Dictionary<string, string>(), out var error);

        result.Should().BeTrue();
        lines.Should().Equal("TEXT One", "TEXT Two", "TEXT Two", "TEXT Two");
        error.Should().BeNull();
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldSupportNestedRepeats()
    {
        var lines = new List<string>
        {
            "REPEAT 2",
            "TEXT Outer",
            "REPEAT 2",
            "TEXT Inner",
            "ENDREPEAT",
            "ENDREPEAT"
        };

        var result = TryExpandAllRepeats(lines, new Dictionary<string, string>(), out var error);

        result.Should().BeTrue();
        lines.Should().Equal(
            "TEXT Outer",
            "TEXT Inner",
            "TEXT Inner",
            "TEXT Outer",
            "TEXT Inner",
            "TEXT Inner");
        error.Should().BeNull();
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldAllowZeroRepeat()
    {
        var lines = new List<string> { "TEXT Begin", "REPEAT 0", "TEXT Skipped", "ENDREPEAT", "TEXT End" };

        var result = TryExpandAllRepeats(lines, new Dictionary<string, string>(), out var error);

        result.Should().BeTrue();
        lines.Should().Equal("TEXT Begin", "TEXT End");
        error.Should().BeNull();
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldFail_WhenCountIsInvalid()
    {
        var lines = new List<string> { "REPEAT foo", "TEXT Sample", "ENDREPEAT" };

        var result = TryExpandAllRepeats(lines, new Dictionary<string, string>(), out var error);

        result.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("REPEAT の回数指定が不正です");
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldFail_WhenEndRepeatIsMissing()
    {
        var lines = new List<string> { "REPEAT 2", "TEXT Sample" };

        var result = TryExpandAllRepeats(lines, new Dictionary<string, string>(), out var error);

        result.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("REPEAT ブロックが ENDREPEAT で閉じられていません");
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldFail_WhenRepeatCountIsTooLarge()
    {
        var lines = new List<string> { "REPEAT 1001", "TEXT Sample", "ENDREPEAT" };

        var result = TryExpandAllRepeats(lines, new Dictionary<string, string>(), out var error);

        result.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("REPEAT に指定できる回数は 0〜1000 です");
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldAllowVariableCount()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Count"] = "2"
        };
        var lines = new List<string> { "REPEAT {{Count}}", "TEXT Sample", "ENDREPEAT" };

        var result = TryExpandAllRepeats(lines, variables, out var error);

        result.Should().BeTrue();
        lines.Should().Equal("TEXT Sample", "TEXT Sample");
        error.Should().BeNull();
    }

    [Fact]
    public void TryExpandRepeatBlocks_ShouldFail_WhenEndRepeatIsUnmatched()
    {
        const string script = "TEXT Sample\nENDREPEAT\n";

        var result = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        result.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("ENDREPEAT");
    }
}
