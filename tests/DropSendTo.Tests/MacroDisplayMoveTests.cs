using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

[Collection("KeyboardMacroServiceTests")]
public class MacroDisplayMoveTests
{
    [Theory]
    [InlineData("TOPLEFT", -1920, -1080)]
    [InlineData("TOPCENTER", -961, -1080)]
    [InlineData("TOPRIGHT", -1, -1080)]
    [InlineData("LEFTCENTER", -1920, -541)]
    [InlineData("CENTER", -961, -541)]
    [InlineData("RIGHTCENTER", -1, -541)]
    [InlineData("BOTTOMLEFT", -1920, -1)]
    [InlineData("BOTTOMCENTER", -961, -1)]
    [InlineData("BOTTOMRIGHT", -1, -1)]
    public void TryResolve_ShouldUseNinePointsInsidePhysicalDisplay_WithNegativeOrigin(
        string position, int expectedX, int expectedY)
    {
        MacroDisplayMove.TryParse($"2 {position}", out var move, out var parseError).Should().BeTrue();
        parseError.Should().BeNull();

        move.TryResolve(new(-1920, -1080, 1920, 1080), out var x, out var y, out var error).Should().BeTrue();

        (x, y).Should().Be((expectedX, expectedY));
        error.Should().BeNull();
    }

    [Fact]
    public void TryResolve_ShouldApplySignedOffset_WhenKeywordsHaveMixedCase()
    {
        MacroDisplayMove.TryParse("2 bOtToMlEfT oFfSeT +20 -20", out var move, out _).Should().BeTrue();

        move.TryResolve(new(100, 200, 1920, 1080), out var x, out var y, out var error).Should().BeTrue();

        (x, y).Should().Be((120, 1259));
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(1, 1, 100, 200)]
    [InlineData(3, 5, 101, 202)]
    [InlineData(3840, 2160, 2019, 1279)]
    public void TryResolve_ShouldHandleSinglePixelOddAndHighResolutionDisplays(
        int width, int height, int expectedX, int expectedY)
    {
        new MacroDisplayMove(1, "CENTER", 0, 0)
            .TryResolve(new(100, 200, width, height), out var x, out var y, out _).Should().BeTrue();

        (x, y).Should().Be((expectedX, expectedY));
    }

    [Theory]
    [InlineData("1 TOPLEFT OFFSET -1 0")]
    [InlineData("1 TOPRIGHT OFFSET 1 0")]
    [InlineData("1 BOTTOMLEFT OFFSET 0 1")]
    [InlineData("1 CENTER OFFSET 2147483647 -2147483648")]
    public void TryResolve_ShouldFailWithoutClampingToAnotherDisplay_WhenOffsetLeavesTarget(string args)
    {
        MacroDisplayMove.TryParse(args, out var move, out _).Should().BeTrue();

        move.TryResolve(new(-1920, -1080, 1920, 1080), out _, out _, out var error).Should().BeFalse();

        error.Should().Contain("範囲外");
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, -1)]
    public void TryResolve_ShouldRejectInvalidDisplayArea(int width, int height)
    {
        new MacroDisplayMove(1, "CENTER", 0, 0)
            .TryResolve(new(0, 0, width, height), out _, out _, out var error).Should().BeFalse();

        error.Should().Contain("領域");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0 CENTER")]
    [InlineData("-1 CENTER")]
    [InlineData("2147483648 CENTER")]
    [InlineData("1.5 CENTER")]
    [InlineData("1 DIAGONAL")]
    [InlineData("1 WIN_CENTER")]
    [InlineData("1 CENTER OFFSET 20")]
    [InlineData("1 CENTER OFFSET 20 20 extra")]
    [InlineData("1 CENTER DELTA 20 20")]
    [InlineData("1 CENTER OFFSET 2147483648 0")]
    [InlineData("1 CENTER OFFSET 0 x")]
    public void TryValidateScript_ShouldRejectMalformedDisplayCommand(string args)
    {
        var ok = KeyboardMacroService.TryValidateScript("MOUSEMOVEDISPLAY " + args,
            SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("行 1").And.Contain("MOUSEMOVEDISPLAY");
    }

    [Theory]
    [InlineData("MOUSEMOVEDISPLAY2 CENTER")]
    [InlineData("MOUSEMOVEDISPLAY 2 CENTER OFFSET {{Unknown}} 0")]
    public void TryValidateScript_ShouldRejectMissingDelimiterOrUndefinedVariable(string script)
    {
        KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error).Should().BeFalse();

        error.Should().Contain("行 1");
    }

    [Fact]
    public async Task RunMacro_ShouldNotSendFollowingClick_WhenOffsetLeavesDisplay()
    {
        using var service = new KeyboardMacroService(NullAppLogger.Instance,
            displayAdapter: new FakeDisplays { Bounds = new(0, 0, 100, 100) });
        int sends = 0;
        KeyboardMacroService.SetSendInputOverrideForTesting((count, _) => { sends++; return count; });
        try
        {
            var result = await service.RunMacroAsync("MOUSEMOVEDISPLAY 1 TOPLEFT OFFSET -1 0\nMOUSELEFTCLICK");

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("範囲外");
            sends.Should().Be(0);
        }
        finally
        {
            KeyboardMacroService.SetSendInputOverrideForTesting(null);
        }
    }

    [Fact]
    public void TryValidateScript_ShouldNotQueryDisplaysOrSendInput()
    {
        var displays = new FakeDisplays { ThrowOnQuery = true };
        using var service = new KeyboardMacroService(NullAppLogger.Instance, displayAdapter: displays);
        int sends = 0;
        KeyboardMacroService.SetSendInputOverrideForTesting((count, _) => { sends++; return count; });
        try
        {
            service.TryValidateScriptWithDependencies(
                "SET N 999\nSET Point TOPRIGHT\nMOUSEMOVEDISPLAY {{N}} {{Point}} OFFSET -20 20 # comment",
                SlotExecutionMode.MacroScript, out var error).Should().BeTrue("{0}", error);

            displays.Queries.Should().BeEmpty();
            sends.Should().Be(0);
        }
        finally
        {
            KeyboardMacroService.SetSendInputOverrideForTesting(null);
        }
    }

    [Fact]
    public async Task RunMacro_ShouldStopBeforeFollowingInput_WhenDisplayIsMissing()
    {
        var displays = new FakeDisplays();
        using var service = new KeyboardMacroService(NullAppLogger.Instance, displayAdapter: displays);
        int sends = 0;
        KeyboardMacroService.SetSendInputOverrideForTesting((count, _) => { sends++; return count; });
        try
        {
            var result = await service.RunMacroAsync("MOUSEMOVEDISPLAY 2 CENTER\nMOUSELEFTCLICK");

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("行 1").And.Contain("ディスプレイ 2");
            displays.Queries.Should().Equal(2);
            sends.Should().Be(0);
        }
        finally
        {
            KeyboardMacroService.SetSendInputOverrideForTesting(null);
        }
    }

    [Fact]
    public void DisplayMove_ShouldUseLatestBoundsAndExistingAbsoluteInputPath_ForEachCommand()
    {
        var displays = new FakeDisplays();
        using var service = new KeyboardMacroService(NullAppLogger.Instance, displayAdapter: displays);
        var inputType = typeof(KeyboardMacroService).GetNestedType("INPUT", BindingFlags.NonPublic)!;
        var bufferType = typeof(List<>).MakeGenericType(inputType);
        var displayMethod = typeof(KeyboardMacroService).GetMethod("TryHandleDisplayMove",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var absoluteMethod = typeof(KeyboardMacroService).GetMethod("TryHandleMouseCommand",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var sentInputs = new List<object>();
        using var capture = CaptureInputs(sentInputs);
        foreach (var bounds in new[] { new MacroDisplayBounds(0, 0, 101, 101), new(100, 200, 101, 101) })
        {
            sentInputs.Clear();
            displays.Bounds = bounds;
            var actual = (IList)Activator.CreateInstance(bufferType)!;
            var expected = (IList)Activator.CreateInstance(bufferType)!;
            object?[] args = { "MOUSEMOVEDISPLAY 2 CENTER", actual, false, null };
            object?[] absoluteArgs = { $"MOUSEMOVEABS {bounds.Left + 50} {bounds.Top + 50}", expected, null };

            ((bool)displayMethod.Invoke(service, args)!).Should().BeTrue("{0}", args[3]);
            var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                ((bool)absoluteMethod.Invoke(null, absoluteArgs)!).Should().BeTrue();
            }
            finally
            {
                SetThreadDpiAwarenessContext(previousDpi);
            }

            actual.Count.Should().Be(0);
            sentInputs.Should().ContainSingle().Which.Should().Be(expected[0]);
        }
        displays.Queries.Should().Equal(2, 2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisplayMove_ShouldSendInPhysicalContextAndRestoreCallerContext_EvenWhenSendFails(bool sendSuccess)
    {
        using var service = new KeyboardMacroService(NullAppLogger.Instance,
            displayAdapter: new FakeDisplays { Bounds = new(0, 0, 100, 100) });
        var inputType = typeof(KeyboardMacroService).GetNestedType("INPUT", BindingFlags.NonPublic)!;
        var buffer = Activator.CreateInstance(typeof(List<>).MakeGenericType(inputType));
        var method = typeof(KeyboardMacroService).GetMethod("TryHandleDisplayMove",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-1)); // DPI_UNAWARE
        bool physicalContextAtSend = false;
        KeyboardMacroService.SetSendInputOverrideForTesting((count, _) =>
        {
            physicalContextAtSend = AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new IntPtr(-4));
            return sendSuccess ? count : 0;
        });
        try
        {
            var callerDpi = GetThreadDpiAwarenessContext();
            object?[] args = { "MOUSEMOVEDISPLAY 2 CENTER", buffer, false, null };

            ((bool)method.Invoke(service, args)!).Should().Be(sendSuccess, "{0}", args[3]);

            physicalContextAtSend.Should().BeTrue();
            AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), callerDpi).Should().BeTrue();
        }
        finally
        {
            SetThreadDpiAwarenessContext(previousDpi);
            KeyboardMacroService.SetSendInputOverrideForTesting(null);
        }
    }

    private static IDisposable CaptureInputs(List<object> sentInputs)
    {
        var field = typeof(KeyboardMacroService).GetField("_sendInputOverride", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldValue = field.GetValue(null);
        var inputArrayType = field.FieldType.GenericTypeArguments[1];
        var count = Expression.Parameter(typeof(uint));
        var inputs = Expression.Parameter(inputArrayType);
        var size = Expression.Parameter(typeof(int));
        Func<uint, Array, int, uint> capture = (number, items, _) =>
        {
            foreach (var item in items) sentInputs.Add(item!);
            return number;
        };
        var body = Expression.Invoke(Expression.Constant(capture), count, Expression.Convert(inputs, typeof(Array)), size);
        field.SetValue(null, Expression.Lambda(field.FieldType, body, count, inputs, size).Compile());
        return new InputCaptureScope(() => field.SetValue(null, oldValue));
    }

    private sealed class InputCaptureScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    [Fact]
    public async Task RunMacro_ShouldExpandDisplayAndPositionVariables_AndSendMovement()
    {
        var displays = new FakeDisplays { Bounds = new(0, 0, 100, 100) };
        using var service = new KeyboardMacroService(NullAppLogger.Instance, displayAdapter: displays);
        uint sent = 0;
        KeyboardMacroService.SetSendInputOverrideForTesting((count, _) => { sent += count; return count; });
        try
        {
            var result = await service.RunMacroAsync(
                "SET N 2\nSET Point CENTER\nMOUSEMOVEDISPLAY {{N}} {{Point}} OFFSET 1 -1\nRETURN Done");

            result.Success.Should().BeTrue(result.Message);
            result.Message.Should().Be("Done");
            displays.Queries.Should().Equal(2);
            sent.Should().Be(1);
        }
        finally
        {
            KeyboardMacroService.SetSendInputOverrideForTesting(null);
        }
    }

    private sealed class FakeDisplays : IMacroDisplayAdapter
    {
        public MacroDisplayBounds? Bounds { get; set; }
        public bool ThrowOnQuery { get; init; }
        public List<int> Queries { get; } = new();

        public bool TryGetBounds(int displayNumber, out MacroDisplayBounds bounds, out string? error)
        {
            if (ThrowOnQuery) throw new InvalidOperationException("Validation must not query the desktop");
            Queries.Add(displayNumber);
            bounds = Bounds ?? default;
            error = Bounds == null ? $"ディスプレイ {displayNumber} が見つかりません。" : null;
            return Bounds != null;
        }
    }
}
