using System;
using System.Collections;
using System.Reflection;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceMouseCommandTests
{
    private static MethodInfo GetMouseCommandMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod("TryHandleMouseCommand", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("マウスコマンドの解析メソッドが存在すること");
        return method!;
    }

    private static object CreateInputBuffer()
    {
        var inputType = typeof(KeyboardMacroService).GetNestedType("INPUT", BindingFlags.NonPublic);
        inputType.Should().NotBeNull("SendInput 用の構造体が存在すること");
        var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(inputType!);
        return Activator.CreateInstance(listType)!;
    }

    private static uint GetFlag(string name)
    {
        var field = typeof(KeyboardMacroService).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull($"{name} 定数が存在すること");
        return (uint)field!.GetValue(null)!;
    }

    private static int GetIntConstant(string name)
    {
        var field = typeof(KeyboardMacroService).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull($"{name} 定数が存在すること");
        return (int)field!.GetValue(null)!;
    }

    private static object GetFirstInput(object buffer)
    {
        var list = (IList)buffer;
        list.Count.Should().BeGreaterThan(0, "少なくとも 1 件の入力が追加されること");
        return list[0]!;
    }

    private static uint ReadMouseFlags(object input)
    {
        var unionValue = input.GetType().GetField("u", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(input)!;
        var mouseValue = unionValue.GetType().GetField("mi", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(unionValue)!;
        return (uint)mouseValue.GetType().GetField("dwFlags", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(mouseValue)!;
    }

    private static int ReadMouseDx(object input)
    {
        var unionValue = input.GetType().GetField("u", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(input)!;
        var mouseValue = unionValue.GetType().GetField("mi", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(unionValue)!;
        return (int)mouseValue.GetType().GetField("dx", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(mouseValue)!;
    }

    private static int ReadMouseDy(object input)
    {
        var unionValue = input.GetType().GetField("u", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(input)!;
        var mouseValue = unionValue.GetType().GetField("mi", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(unionValue)!;
        return (int)mouseValue.GetType().GetField("dy", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(mouseValue)!;
    }

    private static uint ReadMouseData(object input)
    {
        var unionValue = input.GetType().GetField("u", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(input)!;
        var mouseValue = unionValue.GetType().GetField("mi", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(unionValue)!;
        return (uint)mouseValue.GetType().GetField("mouseData", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(mouseValue)!;
    }

    private static IDisposable UseActiveWindowBounds(int left, int top, int right, int bottom)
    {
        KeyboardMacroService.SetActiveWindowBoundsForTesting(left, top, right, bottom);
        return new DelegateDisposable(KeyboardMacroService.ClearActiveWindowBoundsForTesting);
    }

    private static IDisposable UseMacroCursor(int x, int y)
    {
        KeyboardMacroService.SetMacroCursorStartForTesting(x, y);
        return new DelegateDisposable(KeyboardMacroService.ClearMacroCursorForTesting);
    }

    private static MethodInfo GetResolvePointMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod("TryResolveWindowCoordinatePoint", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("座標予約語の解決メソッドが存在すること");
        return method!;
    }

    private static MethodInfo GetParseInt64OrWindowTokenMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod("TryParseInt64OrWindowToken", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("座標部品の解決メソッドが存在すること");
        return method!;
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
    public void TryHandleMouseCommand_ShouldAppendRelativeMove()
    {
        var method = GetMouseCommandMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "MOUSEMOVEREL 12 -34", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        args[2].Should().BeNull();
        ((IList)buffer).Count.Should().Be(1);
        var input = GetFirstInput(buffer);
        ReadMouseFlags(input).Should().Be(GetFlag("MOUSEEVENTF_MOVE"));
        ReadMouseDx(input).Should().Be(12);
        ReadMouseDy(input).Should().Be(-34);
    }

    [Fact]
    public void TryHandleMouseCommand_ShouldAppendAbsoluteMoveFlags()
    {
        var method = GetMouseCommandMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "MOUSEMOVEABS 100 200", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue("画面座標が取得できれば正しく処理できること");
        args[2].Should().BeNull();
        ((IList)buffer).Count.Should().Be(1);
        var input = GetFirstInput(buffer);
        ReadMouseFlags(input).Should().Be(
            GetFlag("MOUSEEVENTF_MOVE") |
            GetFlag("MOUSEEVENTF_ABSOLUTE") |
            GetFlag("MOUSEEVENTF_VIRTUALDESK"));
    }

    [Fact]
    public void TryHandleMouseCommand_ShouldAcceptWindowToken()
    {
        using var _ = UseActiveWindowBounds(100, 200, 300, 400);
        var method = GetMouseCommandMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "MOUSEMOVEABS WIN_TOPCENTER", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        args[2].Should().BeNull();
        ((IList)buffer).Count.Should().Be(1);
        var input = GetFirstInput(buffer);
        ReadMouseFlags(input).Should().Be(
            GetFlag("MOUSEEVENTF_MOVE") |
            GetFlag("MOUSEEVENTF_ABSOLUTE") |
            GetFlag("MOUSEEVENTF_VIRTUALDESK"));
    }

    [Fact]
    public void TryHandleMouseCommand_ShouldAcceptWindowCenterToken()
    {
        using var _ = UseActiveWindowBounds(10, 10, 110, 210);
        var method = GetMouseCommandMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "MOUSEMOVEABS WIN_CENTER", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        args[2].Should().BeNull();
        ((IList)buffer).Count.Should().Be(1);
        var input = GetFirstInput(buffer);
        ReadMouseFlags(input).Should().Be(
            GetFlag("MOUSEEVENTF_MOVE") |
            GetFlag("MOUSEEVENTF_ABSOLUTE") |
            GetFlag("MOUSEEVENTF_VIRTUALDESK"));
    }

    [Fact]
    public void TryHandleMouseCommand_ShouldMoveRelativeToWindow()
    {
        using var _ = UseActiveWindowBounds(50, 60, 250, 260);
        var method = GetMouseCommandMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "MOUSEMOVEWIN 20 30", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        args[2].Should().BeNull();
        ((IList)buffer).Count.Should().Be(1);
        var input = GetFirstInput(buffer);
        ReadMouseFlags(input).Should().Be(
            GetFlag("MOUSEEVENTF_MOVE") |
            GetFlag("MOUSEEVENTF_ABSOLUTE") |
            GetFlag("MOUSEEVENTF_VIRTUALDESK"));
    }

    [Fact]
    public void TryHandleMouseCommand_ShouldAddWheelDownWithDefaultSteps()
    {
        var method = GetMouseCommandMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "MOUSESCROLLDOWN", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        args[2].Should().BeNull();
        ((IList)buffer).Count.Should().Be(1);
        var input = GetFirstInput(buffer);
        ReadMouseFlags(input).Should().Be(GetFlag("MOUSEEVENTF_WHEEL"));
        var wheelDelta = GetIntConstant("WHEEL_DELTA");
        ReadMouseData(input).Should().Be(unchecked((uint)(-wheelDelta)));
    }

    [Fact]
    public void TryHandleMouseCommand_ShouldAppendDoubleClickSequence()
    {
        var method = GetMouseCommandMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "MOUSELEFTDOUBLECLICK", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        args[2].Should().BeNull();
        ((IList)buffer).Count.Should().Be(4);
        var expected = new[]
        {
            GetFlag("MOUSEEVENTF_LEFTDOWN"),
            GetFlag("MOUSEEVENTF_LEFTUP"),
            GetFlag("MOUSEEVENTF_LEFTDOWN"),
            GetFlag("MOUSEEVENTF_LEFTUP")
        };

        var list = (IList)buffer;
        for (int i = 0; i < expected.Length; i++)
        {
            ReadMouseFlags(list[i]!).Should().Be(expected[i]);
        }
    }

    [Fact]
    public void TryHandleMouseCommand_ShouldRejectInvalidArgumentCount()
    {
        var method = GetMouseCommandMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "MOUSEMOVEABS 10", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeFalse();
        args[2].Should().BeOfType<string>().Which.Should().NotBeNullOrEmpty();
        ((IList)buffer).Count.Should().Be(0);
    }

    [Fact]
    public void TryHandleMouseCommand_ShouldRejectUnknownWindowToken()
    {
        using var _ = UseActiveWindowBounds(0, 0, 100, 100);
        var method = GetMouseCommandMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "MOUSEMOVEABS WIN_DIAGONAL", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeFalse();
        args[2].Should().BeOfType<string>().Which.Should().Contain("予約語");
        ((IList)buffer).Count.Should().Be(0);
    }

    [Fact]
    public void TryResolveWindowCoordinatePoint_ShouldReturnMacroCursorOrigin()
    {
        using var _ = UseMacroCursor(640, 480);
        var method = GetResolvePointMethod();
        var args = new object?[] { "CURSOR_START", 0L, 0L, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        args[3].Should().BeNull();
        ((long)args[1]!).Should().Be(640);
        ((long)args[2]!).Should().Be(480);
    }

    [Fact]
    public void TryParseInt64OrWindowToken_ShouldResolveCursorStartComponent()
    {
        using var _ = UseMacroCursor(123, 456);
        var method = GetParseInt64OrWindowTokenMethod();
        var args = new object?[] { "CURSOR_START_Y", 0L, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue();
        args[2].Should().BeNull();
        ((long)args[1]!).Should().Be(456);
    }

    [Fact]
    public void TryResolveWindowCoordinatePoint_ShouldFailWithoutCursorContext()
    {
        KeyboardMacroService.ClearMacroCursorForTesting();
        var method = GetResolvePointMethod();
        var args = new object?[] { "CURSOR_START", 0L, 0L, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeFalse();
        args[3].Should().BeOfType<string>().Which.Should().Contain("マクロ開始時のマウス座標");
    }

    [Theory]
    [InlineData("WIN_TOPLEFT")]
    [InlineData("WIN_TOPCENTER")]
    [InlineData("WIN_TOPMIDDLE")]
    [InlineData("WIN_TOPRIGHT")]
    [InlineData("WIN_LEFTCENTER")]
    [InlineData("WIN_LEFTMIDDLE")]
    [InlineData("WIN_RIGHTCENTER")]
    [InlineData("WIN_RIGHTMIDDLE")]
    [InlineData("WIN_BOTTOMLEFT")]
    [InlineData("WIN_BOTTOMCENTER")]
    [InlineData("WIN_BOTTOMMIDDLE")]
    [InlineData("WIN_BOTTOMRIGHT")]
    [InlineData("WIN_CENTER")]
    [InlineData("WIN_MIDDLE")]
    [InlineData("WIN_MID")]
    public void TryResolveWindowCoordinatePoint_ShouldAcceptDocumentedWindowTokens(string token)
    {
        using var _ = UseActiveWindowBounds(10, 10, 210, 410);
        var method = GetResolvePointMethod();
        var args = new object?[] { token, 0L, 0L, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue($"{token} はドキュメントで許容される座標予約語であること");
        args[3].Should().BeNull();
    }

    [Theory]
    [InlineData("WIN_RIGHTBOTTOM")]
    [InlineData("WIN_BOTTOMRIGHT_XY")]
    [InlineData("WIN_CENTERLEFT")]
    [InlineData("WIN_TOPRIGHT_XY")]
    public void TryResolveWindowCoordinatePoint_ShouldRejectUnknownWindowTokenVariants(string token)
    {
        using var _ = UseActiveWindowBounds(0, 0, 100, 100);
        var method = GetResolvePointMethod();
        var args = new object?[] { token, 0L, 0L, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeFalse();
        args[3].Should().BeOfType<string>().Which.Should().Contain("予約語");
    }
}
