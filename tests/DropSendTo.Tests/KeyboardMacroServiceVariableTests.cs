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
}
