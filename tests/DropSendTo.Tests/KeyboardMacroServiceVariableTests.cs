using System;
using System.Collections.Generic;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

[Collection("KeyboardMacroServiceTests")]
public class KeyboardMacroServiceVariableTests
{
    private static IDisposable UseActiveWindowBounds(int left, int top, int right, int bottom)
    {
        KeyboardMacroService.SetActiveWindowBoundsForTesting(left, top, right, bottom);
        return new DelegateDisposable(KeyboardMacroService.ClearActiveWindowBoundsForTesting);
    }

    private sealed class DelegateDisposable : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public DelegateDisposable(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _onDispose();
        }
    }

    [Fact]
    public void TryExpandVariables_ShouldReplacePlaceholders()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User"] = "Alice"
        };

        var ok = KeyboardMacroService.TryExpandVariables("Hello {{User}}", variables, out var result, out var error);

        ok.Should().BeTrue();
        result.Should().Be("Hello Alice");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplyMathDirective_ShouldAddNumbers()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Counter"] = "5"
        };

        var ok = KeyboardMacroService.TryApplyMathDirective("ADD Counter 3", variables, out var name, out var before, out var operand, out var result, out var error);

        ok.Should().BeTrue();
        name.Should().Be("Counter");
        before.Should().Be(5);
        operand.Should().Be(3);
        result.Should().Be(8);
        variables.Should().ContainKey("Counter").WhoseValue.Should().Be("8");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplyMathDirective_ShouldFailOnDivideByZero()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Value"] = "10"
        };

        var ok = KeyboardMacroService.TryApplyMathDirective("DIV Value 0", variables, out _, out _, out _, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("DIV");
    }

    [Fact]
    public void TryApplyConcatDirective_ShouldAppendAndExpand()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Greeting"] = "Hello",
            ["Suffix"] = "!"
        };

        var ok = KeyboardMacroService.TryApplyConcatDirective("APPEND Greeting {{Suffix}}", variables, prepend: false, out var name, out var newValue, out var error);

        ok.Should().BeTrue();
        name.Should().Be("Greeting");
        newValue.Should().Be("Hello!");
        variables.Should().ContainKey("Greeting").WhoseValue.Should().Be("Hello!");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplyConcatDirective_ShouldCreateVariableWhenMissing()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ok = KeyboardMacroService.TryApplyConcatDirective("PREPEND Message Start-", variables, prepend: true, out var name, out var newValue, out var error);

        ok.Should().BeTrue();
        name.Should().Be("Message");
        newValue.Should().Be("Start-");
        variables.Should().ContainKey("Message").WhoseValue.Should().Be("Start-");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplyReplaceDirective_ShouldReplaceAllOccurrences()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Body"] = "alpha beta beta"
        };

        var ok = KeyboardMacroService.TryApplyReplaceDirective(
            "REPLACE Body \"beta\" \"gamma\"",
            variables,
            out var name,
            out var newValue,
            out var error,
            specialResolver: null,
            out var replacements);

        ok.Should().BeTrue();
        name.Should().Be("Body");
        newValue.Should().Be("alpha gamma gamma");
        replacements.Should().Be(2);
        variables.Should().ContainKey("Body").WhoseValue.Should().Be("alpha gamma gamma");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplyReplaceDirective_ShouldAllowDeletion()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Body"] = "Hello  World"
        };

        var ok = KeyboardMacroService.TryApplyReplaceDirective(
            "REPLACE Body \" \" \"\"",
            variables,
            out var name,
            out var newValue,
            out var error,
            specialResolver: null,
            out var replacements);

        ok.Should().BeTrue();
        name.Should().Be("Body");
        newValue.Should().Be("HelloWorld");
        replacements.Should().Be(2);
        variables.Should().ContainKey("Body").WhoseValue.Should().Be("HelloWorld");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplyReplaceDirective_ShouldFail_OnEmptySearch()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Body"] = "data"
        };

        var ok = KeyboardMacroService.TryApplyReplaceDirective(
            "REPLACE Body \"\" \"x\"",
            variables,
            out _,
            out _,
            out var error,
            specialResolver: null,
            out _);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("検索文字列");
    }

    [Fact]
    public void TryApplyRegexReplaceDirective_ShouldSupportGroups()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Body"] = "abc123def456"
        };

        var ok = KeyboardMacroService.TryApplyRegexReplaceDirective(
            @"REPLACE_REGEX Body ""(\\d+)"" ""[#$1]""",
            variables,
            out var name,
            out var newValue,
            out var error,
            specialResolver: null,
            out var replacements);

        ok.Should().BeTrue();
        replacements.Should().Be(2);
        name.Should().Be("Body");
        newValue.Should().Be("abc[#123]def[#456]");
        variables.Should().ContainKey("Body").WhoseValue.Should().Be("abc[#123]def[#456]");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplyRegexReplaceDirective_ShouldHonorOptions()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Body"] = "Hello\nWORLD"
        };

        var ok = KeyboardMacroService.TryApplyRegexReplaceDirective(
            @"REPLACE_REGEX Body ""^world$"" ""match"" IGNORECASE MULTILINE",
            variables,
            out var name,
            out var newValue,
            out var error,
            specialResolver: null,
            out var replacements);

        ok.Should().BeTrue();
        replacements.Should().Be(1);
        name.Should().Be("Body");
        newValue.Should().Be("Hello\nmatch");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplyRegexReplaceDirective_ShouldFail_OnInvalidPattern()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Body"] = "data"
        };

        var ok = KeyboardMacroService.TryApplyRegexReplaceDirective(
            @"REPLACE_REGEX Body ""[A-"" ""x""",
            variables,
            out _,
            out _,
            out var error,
            specialResolver: null,
            out _);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("パターン");
    }

    [Fact]
    public void TryExpandVariables_ShouldFail_WhenVariableIsMissing()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ok = KeyboardMacroService.TryExpandVariables("{{Unknown}}", variables, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("定義");
    }

    [Fact]
    public void TryApplySetDirective_ShouldSupportVariableInterpolation()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Base"] = "World"
        };

        var ok = KeyboardMacroService.TryApplySetDirective("SET Greeting Hello {{Base}}", variables, out var name, out var value, out var error);

        ok.Should().BeTrue();
        name.Should().Be("Greeting");
        value.Should().Be("Hello World");
        variables.Should().ContainKey("Greeting").WhoseValue.Should().Be("Hello World");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplyUnsetDirective_ShouldRemoveExistingVariable()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flag"] = "1"
        };

        var ok = KeyboardMacroService.TryApplyUnsetDirective("UNSET Flag", variables, out var name, out var removed, out var error);

        ok.Should().BeTrue();
        removed.Should().BeTrue();
        name.Should().Be("Flag");
        variables.Should().NotContainKey("Flag");
        error.Should().BeNull();
    }

    [Fact]
    public void TryApplySetDirective_ShouldFail_OnInvalidVariableName()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ok = KeyboardMacroService.TryApplySetDirective("SET 1Invalid value", variables, out _, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("変数名");
    }

    [Fact]
    public void TryApplySetDirective_ShouldResolveWindowCoordinateComponent()
    {
        using var _ = UseActiveWindowBounds(100, 200, 300, 400);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ok = KeyboardMacroService.TryApplySetDirective("SET TargetX WIN_TOPLEFT_X", variables, out var name, out var value, out var error);

        ok.Should().BeTrue();
        name.Should().Be("TargetX");
        value.Should().Be("100");
        variables.Should().ContainKey("TargetX").WhoseValue.Should().Be("100");
        error.Should().BeNull();
    }

    [Fact]
    public void TryExpandVariables_ShouldResolveClipboardVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var snapshot = new ClipboardSnapshot(" Hello World ", new[] { "First", "Second" }, new[] { "Line1", "Line2" });
        var resolver = KeyboardMacroService.CreateSpecialVariableResolverForTesting(snapshot);

        var okRaw = KeyboardMacroService.TryExpandVariables("{{clipboard}}", variables, out var raw, out var rawError, resolver);
        okRaw.Should().BeTrue();
        rawError.Should().BeNull();
        raw.Should().Be("Hello World");

        var okLatest = KeyboardMacroService.TryExpandVariables("{{clipboard_args}}", variables, out var latest, out var latestError, resolver);
        okLatest.Should().BeTrue();
        latestError.Should().BeNull();
        latest.Should().Be("Line1" + Environment.NewLine + "Line2");

        var okLimited = KeyboardMacroService.TryExpandVariables("{{clipboard:1}}", variables, out var limited, out var limitedError, resolver);
        okLimited.Should().BeTrue();
        limitedError.Should().BeNull();
        limited.Should().Be("Second");
    }

    [Fact]
    public void TryExpandVariables_ShouldResolveDropVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dropPaths = new[] { @"C:\Data\file one.txt", @"D:\Second.bin" };
        var resolver = KeyboardMacroService.CreateSpecialVariableResolverForTesting(ClipboardSnapshot.Empty, dropPaths);

        var okArgs = KeyboardMacroService.TryExpandVariables("{{drop_args}}", variables, out var joined, out var argsError, resolver);
        okArgs.Should().BeTrue();
        argsError.Should().BeNull();
        joined.Should().Be("\"C:\\Data\\file one.txt\" D:\\Second.bin");

        var okCount = KeyboardMacroService.TryExpandVariables("{{drop_count}}", variables, out var count, out var countError, resolver);
        okCount.Should().BeTrue();
        countError.Should().BeNull();
        count.Should().Be("2");

        var okFirst = KeyboardMacroService.TryExpandVariables("{{drop_path}}", variables, out var first, out var firstError, resolver);
        okFirst.Should().BeTrue();
        firstError.Should().BeNull();
        first.Should().Be(@"C:\Data\file one.txt");

        var okSecond = KeyboardMacroService.TryExpandVariables("{{drop_path:2}}", variables, out var second, out var secondError, resolver);
        okSecond.Should().BeTrue();
        secondError.Should().BeNull();
        second.Should().Be(@"D:\Second.bin");

        var okInvalid = KeyboardMacroService.TryExpandVariables("{{drop_path:abc}}", variables, out _, out var invalidError, resolver);
        okInvalid.Should().BeFalse();
        invalidError.Should().NotBeNull();
        invalidError.Should().Contain("drop_path");
    }

    [Fact]
    public void TryExpandVariables_ShouldReportError_OnInvalidClipboardSpecifier()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var snapshot = new ClipboardSnapshot("data", Array.Empty<string>(), Array.Empty<string>());
        var resolver = KeyboardMacroService.CreateSpecialVariableResolverForTesting(snapshot);

        var ok = KeyboardMacroService.TryExpandVariables("{{clipboard:abc}}", variables, out _, out var error, resolver);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("clipboard");
    }

    [Fact]
    public void TryApplyMathDirective_ShouldUseWindowCoordinateOperand()
    {
        using var _ = UseActiveWindowBounds(100, 200, 300, 400);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PosX"] = "0"
        };

        var ok = KeyboardMacroService.TryApplyMathDirective("ADD PosX WIN_BOTTOMCENTER_X", variables, out var name, out var before, out var operand, out var result, out var error);

        ok.Should().BeTrue();
        name.Should().Be("PosX");
        before.Should().Be(0);
        operand.Should().Be(199); // left 100, right 299 -> midpoint 199
        result.Should().Be(199);
        variables.Should().ContainKey("PosX").WhoseValue.Should().Be("199");
        error.Should().BeNull();
    }

    [Fact]
    public void TryEvaluateCondition_ShouldCompareStrings()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mode"] = "Admin"
        };

        var ok = KeyboardMacroService.TryEvaluateCondition("{{Mode}} == Admin", variables, null, out var result, out var error);

        ok.Should().BeTrue();
        result.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void TryEvaluateCondition_ShouldCompareNumbers()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Count"] = "10"
        };

        var ok = KeyboardMacroService.TryEvaluateCondition("{{Count}} >= 5", variables, null, out var result, out var error);

        ok.Should().BeTrue();
        result.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void TryEvaluateCondition_ShouldSupportContains()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ok = KeyboardMacroService.TryEvaluateCondition("\"Hello World\" CONTAINS \"World\"", variables, null, out var result, out var error);

        ok.Should().BeTrue();
        result.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void TryEvaluateCondition_ShouldFail_OnUnknownOperator()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flag"] = "1"
        };

        var ok = KeyboardMacroService.TryEvaluateCondition("{{Flag}} ??? 1", variables, null, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("演算子");
    }
}
