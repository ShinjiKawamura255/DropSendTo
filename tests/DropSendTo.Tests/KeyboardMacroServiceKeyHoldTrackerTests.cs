using System;
using System.Collections;
using System.Reflection;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceKeyHoldTrackerTests
{
    private static Type GetKeyHoldTrackerType()
    {
        var trackerType = typeof(KeyboardMacroService).GetNestedType("KeyHoldTracker", BindingFlags.NonPublic);
        trackerType.Should().NotBeNull("キー押下管理用のトラッカーが存在すること");
        return trackerType!;
    }

    private static object CreateTrackerInstance()
    {
        var trackerType = GetKeyHoldTrackerType();
        return Activator.CreateInstance(trackerType)!;
    }

    private static IList CreateInputBuffer()
    {
        var inputType = typeof(KeyboardMacroService).GetNestedType("INPUT", BindingFlags.NonPublic);
        inputType.Should().NotBeNull("SendInput 用の構造体が存在すること");
        var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(inputType!);
        return (IList)Activator.CreateInstance(listType)!;
    }

    private static ushort GetScanCode(ushort virtualKey)
    {
        var mapMethod = typeof(KeyboardMacroService).GetMethod("MapVirtualKey", BindingFlags.NonPublic | BindingFlags.Static);
        mapMethod.Should().NotBeNull("MapVirtualKey API が存在すること");
        var mapModeField = typeof(KeyboardMacroService).GetField("MAPVK_VK_TO_VSC", BindingFlags.NonPublic | BindingFlags.Static);
        mapModeField.Should().NotBeNull("MapVirtualKey の変換モード定数が存在すること");
        var mode = (uint)mapModeField!.GetValue(null)!;
        return (ushort)(uint)mapMethod!.Invoke(null, new object?[] { (uint)virtualKey, mode })!;
    }

    private static uint GetKeyEventFlags(object input)
    {
        var inputType = input.GetType();
        var unionField = inputType.GetField("u", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        unionField.Should().NotBeNull();
        var unionValue = unionField!.GetValue(input);
        var unionType = unionValue!.GetType();
        var keyField = unionType.GetField("ki", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        keyField.Should().NotBeNull();
        var keyValue = keyField!.GetValue(unionValue);
        var dwFlagsField = keyValue!.GetType().GetField("dwFlags", BindingFlags.Public | BindingFlags.Instance);
        dwFlagsField.Should().NotBeNull();
        return (uint)dwFlagsField!.GetValue(keyValue)!;
    }

    private static ushort GetKeyScan(object input)
    {
        var inputType = input.GetType();
        var unionField = inputType.GetField("u", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var unionValue = unionField!.GetValue(input);
        var keyField = unionValue!.GetType().GetField("ki", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var keyValue = keyField!.GetValue(unionValue);
        var wScanField = keyValue!.GetType().GetField("wScan", BindingFlags.Public | BindingFlags.Instance);
        return (ushort)wScanField!.GetValue(keyValue)!;
    }

    private static bool HasHeldKeys(object tracker)
    {
        var property = tracker.GetType().GetProperty("HasHeldKeys", BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull();
        return (bool)property!.GetValue(tracker)!;
    }

    private static void InvokeTracker(object tracker, string methodName, params object[] args)
    {
        var method = tracker.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull($"{methodName} メソッドが存在すること");
        method!.Invoke(tracker, args);
    }

    [Fact]
    public void ReleaseAll_ShouldAppendKeyUpsInReverseOrder()
    {
        var tracker = CreateTrackerInstance();
        var buffer = CreateInputBuffer();

        ushort alt = 0x12;
        ushort ctrl = 0x11;

        InvokeTracker(tracker, "TrackKeyDown", ctrl);
        InvokeTracker(tracker, "TrackKeyDown", alt);
        HasHeldKeys(tracker).Should().BeTrue("キーを押下状態で保持しているため");

        InvokeTracker(tracker, "ReleaseAll", buffer);

        buffer.Count.Should().Be(2, "保持していた各キーに対して KEYUP が 1 件ずつ生成されること");
        HasHeldKeys(tracker).Should().BeFalse("ReleaseAll 後は状態がクリアされること");

        var keyUpFlagField = typeof(KeyboardMacroService).GetField("KEYEVENTF_KEYUP", BindingFlags.NonPublic | BindingFlags.Static);
        keyUpFlagField.Should().NotBeNull();
        uint keyUpBit = (uint)keyUpFlagField!.GetValue(null)!;

        var firstInput = buffer[0]!;
        var secondInput = buffer[1]!;

        (GetKeyEventFlags(firstInput) & keyUpBit).Should().NotBe(0, "最初の入力は KEYUP フラグを含むこと");
        (GetKeyEventFlags(secondInput) & keyUpBit).Should().NotBe(0, "2 番目の入力も KEYUP フラグを含むこと");

        GetKeyScan(firstInput).Should().Be(GetScanCode(alt), "ReleaseAll は後から押したキーを先に解放すること");
        GetKeyScan(secondInput).Should().Be(GetScanCode(ctrl), "最初に押したキーは最後に解放されること");
    }

    [Fact]
    public void TrackKeyUp_ShouldRemoveKeyFromHeldState()
    {
        var tracker = CreateTrackerInstance();
        var buffer = CreateInputBuffer();
        ushort shift = 0x10;

        InvokeTracker(tracker, "TrackKeyDown", shift);
        InvokeTracker(tracker, "TrackKeyUp", shift);

        HasHeldKeys(tracker).Should().BeFalse("KEYUP 済みのキーは保持されないこと");

        InvokeTracker(tracker, "ReleaseAll", buffer);

        buffer.Count.Should().Be(0, "保持中のキーが無ければ KEYUP は生成されないこと");
    }
}
