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
}
