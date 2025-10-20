using System;
using System.Collections;
using System.Reflection;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceKeyParsingTests
{
    private static MethodInfo GetCombinationMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod("TryAppendCombination", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("KEY コマンドの解析メソッドが存在すること");
        return method!;
    }

    private static IList CreateInputBuffer()
    {
        var inputType = typeof(KeyboardMacroService).GetNestedType("INPUT", BindingFlags.NonPublic);
        inputType.Should().NotBeNull("SendInput 用の構造体が存在すること");
        var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(inputType!);
        return (IList)Activator.CreateInstance(listType)!;
    }

    [Fact]
    public void TryAppendCombination_ShouldRejectModifierWithoutMainKey()
    {
        var method = GetCombinationMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { "ALT", buffer, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeFalse();
        args[2].Should().BeOfType<string>().Which.Should().NotBeNullOrEmpty();
        buffer.Count.Should().Be(0, "修飾キーのみの KEY 指示は拒否されること");
    }
}
