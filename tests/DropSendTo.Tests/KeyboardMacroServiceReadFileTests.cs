using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceReadFileTests : IDisposable
{
    private readonly string _tempRoot;

    public KeyboardMacroServiceReadFileTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DropSendTo_ReadFileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void TryApplyReadFileDirective_ShouldLoadContent_WhenWithinDefaultLimit()
    {
        var path = Path.Combine(_tempRoot, "small.txt");
        File.WriteAllText(path, "hello");

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var method = GetReadFileMethod();
        var args = new object?[] { $"Body \"{path}\"", variables, null, false, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeTrue();
        variables.Should().ContainKey("Body");
        variables["Body"].Should().Be("hello");
        args[4].Should().BeNull();
    }

    [Fact]
    public void TryApplyReadFileDirective_ShouldFail_WhenExceedsDefaultLimitWithoutMax()
    {
        var path = Path.Combine(_tempRoot, "big.txt");
        var content = new string('a', 5000);
        File.WriteAllText(path, content);

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var method = GetReadFileMethod();
        var args = new object?[] { $"Body \"{path}\"", variables, null, false, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeFalse();
        args[4].Should().BeOfType<string>()
            .Subject.Should().Contain("MAX");
    }

    [Fact]
    public void TryApplyReadFileDirective_ShouldRespectMaxOption()
    {
        var path = Path.Combine(_tempRoot, "medium.txt");
        var content = new string('b', 6000);
        File.WriteAllText(path, content);

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var method = GetReadFileMethod();
        var args = new object?[] { $"Body \"{path}\" MAX 6000", variables, null, false, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeTrue();
        variables.Should().ContainKey("Body");
        variables["Body"].Length.Should().Be(6000);
        args[4].Should().BeNull();
    }

    private static MethodInfo GetReadFileMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod(
            "TryApplyReadFileDirective",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("READFILE ヘルパーが存在すること");
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
            // ignore cleanup failures
        }
    }
}
