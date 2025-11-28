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

    private static MethodInfo GetResolveKeyTokenMethod()
    {
        var parserType = typeof(KeyboardMacroService).Assembly.GetType("DropSendTo.Services.KeyChordParser");
        parserType.Should().NotBeNull("KeyChordParser が内部型として存在すること");
        var method = parserType!.GetMethod("TryResolveKeyToken", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull("キー名の解決メソッドが公開されていること");
        return method!;
    }

    [Theory]
    [InlineData("Alt")]
    [InlineData("ALT")]
    public void TryAppendCombination_ShouldAllowStandaloneModifierKey(string token)
    {
        var method = GetCombinationMethod();
        var buffer = CreateInputBuffer();
        var args = new object?[] { token, buffer, IntPtr.Zero, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue($"{token} の単体指定は許可されること");
        args[3].Should().BeNull("エラーは発生しないこと");
        buffer.Count.Should().Be(2, "単体キーは押下と解放の 2 イベントを生成すること");
    }

    [Theory]
    [InlineData("Alt", 0x12)]
    [InlineData("MENU", 0x12)]
    public void TryResolveKeyToken_ShouldSupportStandaloneModifierToken(string token, ushort expectedVirtualKey)
    {
        var method = GetResolveKeyTokenMethod();
        var args = new object?[] { token, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue($"{token} は KEYDOWN/KEYUP でも利用できること");
        args[1].Should().NotBeNull();
        ((ushort)args[1]!).Should().Be(expectedVirtualKey);
    }

    [Theory]
    [InlineData("`", 0xC0)]
    [InlineData("KANJI", 0x19)]
    [InlineData("OEM102", 0xE2)]
    [InlineData("OEM8", 0xDF)]
    [InlineData("NONCONVERT", 0x1D)]
    [InlineData("PROCESS", 0xE5)]
    public void TryResolveKeyToken_ShouldSupportAdditionalKeyboardInputs(string token, ushort expectedVirtualKey)
    {
        var method = GetResolveKeyTokenMethod();
        var args = new object?[] { token, null };

        var result = (bool)method.Invoke(null, args)!;

        result.Should().BeTrue($"{token} が KEYDOWN/KEYUP に利用できること");
        args[1].Should().NotBeNull();
        ((ushort)args[1]!).Should().Be(expectedVirtualKey);
    }

    [Fact]
    public void TryGetCanonicalToken_ShouldExposeBackquoteAndImeKeys()
    {
        KeyChordParser.TryGetCanonicalToken(0xC0, out var backquoteToken).Should().BeTrue();
        backquoteToken.Should().NotBeNullOrEmpty();

        KeyChordParser.TryGetCanonicalToken(0x19, out var kanjiToken).Should().BeTrue();
        kanjiToken.Should().Be("KANJI");
    }

    [Fact]
    public void TryGetCanonicalToken_ShouldFallbackToVkNotationForUnknownKeys()
    {
        const ushort unknownVirtualKey = 0xE8;

        KeyChordParser.TryGetCanonicalToken(unknownVirtualKey, out var token).Should().BeTrue();
        token.Should().Be("VK_E8");

        var method = GetResolveKeyTokenMethod();
        var args = new object?[] { token, null };

        var resolved = (bool)method.Invoke(null, args)!;

        resolved.Should().BeTrue("VK_XX 形式のキーは録音結果から再解決できること");
        args[1].Should().NotBeNull();
        ((ushort)args[1]!).Should().Be(unknownVirtualKey);
    }
}
