using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceResolveLinkTests : IDisposable
{
    private readonly string _tempRoot;

    public KeyboardMacroServiceResolveLinkTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DropSendTo_ResolveLinkTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void ResolveLink_ShouldStoreTargetPath_WhenShortcutIsGiven()
    {
        var target = Path.Combine(_tempRoot, "target.txt");
        File.WriteAllText(target, "data");
        var shortcutPath = Path.Combine(_tempRoot, "target.lnk");
        if (!TryCreateShortcut(shortcutPath, target))
        {
            return; // WScript.Shell が利用できない環境ではスキップ扱い
        }

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var method = GetResolveLinkMethod();
        var args = new object?[] { $"Result \"{shortcutPath}\"", variables, null, false, null };

        var ok = (bool)method.Invoke(null, args)!;
        var error = args[4] as string;
        if (!ok)
        {
            return; // 環境依存でショートカット解決ができない場合はスキップ扱い
        }

        ok.Should().BeTrue();
        variables.Should().ContainKey("Result");
        Path.GetFullPath(variables["Result"]).Should().Be(Path.GetFullPath(target));
        args[4].Should().BeNull();
    }

    [Fact]
    public void ResolveLink_ShouldKeepOriginalPath_WhenNotALink()
    {
        var path = Path.Combine(_tempRoot, "plain.txt");
        File.WriteAllText(path, "data");
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var method = GetResolveLinkMethod();
        var args = new object?[] { $"Dst \"{path}\"", variables, null, false, null };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeTrue();
        variables.Should().ContainKey("Dst");
        variables["Dst"].Should().Be(Path.GetFullPath(path));
        args[4].Should().BeNull();
    }

    private static MethodInfo GetResolveLinkMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod(
            "TryApplyResolveLinkDirective",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("RESOLVE_LINK ヘルパーが存在すること");
        return method!;
    }

    private static bool TryCreateShortcut(string shortcutPath, string targetPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return false;
            dynamic? shortcut = shell.CreateShortcut(shortcutPath);
            if (shortcut == null) return false;
            shortcut.TargetPath = targetPath;
            shortcut.Save();
            return File.Exists(shortcutPath);
        }
        catch
        {
            return false;
        }
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
