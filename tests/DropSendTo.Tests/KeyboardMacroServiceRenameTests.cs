using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceRenameTests : IDisposable
{
    private readonly string _tempRoot;

    public KeyboardMacroServiceRenameTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DropSendTo_RenameTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void TryApplyRenameDirective_ShouldRenameFile_WithVariableExpansion()
    {
        var source = Path.Combine(_tempRoot, "source.txt");
        var target = Path.Combine(_tempRoot, "renamed.txt");
        File.WriteAllText(source, "data");

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Src"] = source,
            ["Dst"] = target
        };

        var method = GetRenameMethod();
        var args = new object?[] { "\"{{Src}}\" \"{{Dst}}\"", variables, null, false, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeTrue();
        File.Exists(source).Should().BeFalse();
        File.Exists(target).Should().BeTrue();
        args[4].Should().BeNull();
    }

    [Fact]
    public void TryApplyRenameDirective_ShouldFail_WhenSourceMissing()
    {
        var source = Path.Combine(_tempRoot, "missing.txt");
        var target = Path.Combine(_tempRoot, "target.txt");
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var method = GetRenameMethod();
        var args = new object?[] { $"\"{source}\" \"{target}\"", variables, null, false, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeFalse();
        args[4].Should().BeOfType<string>()
            .Subject.Should().Contain("存在しません");
    }

    [Fact]
    public void TryApplyRenameDirective_ShouldValidateOnly_WhenRequested()
    {
        var source = Path.Combine(_tempRoot, "src.txt");
        var target = Path.Combine(_tempRoot, "dst.txt");
        File.WriteAllText(source, "data");

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Src"] = source,
            ["Dst"] = target
        };

        var method = GetRenameMethod();
        var args = new object?[] { "\"{{Src}}\" \"{{Dst}}\"", variables, null, true, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeTrue();
        File.Exists(source).Should().BeTrue();
        File.Exists(target).Should().BeFalse();
        args[4].Should().BeNull();
    }

    private static MethodInfo GetRenameMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod(
            "TryApplyRenameDirective",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("RENAME ヘルパーが存在すること");
        return method!;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
