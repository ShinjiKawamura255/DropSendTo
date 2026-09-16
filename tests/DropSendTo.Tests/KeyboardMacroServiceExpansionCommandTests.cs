using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceExpansionCommandTests : IDisposable
{
    private readonly string _tempRoot;

    public KeyboardMacroServiceExpansionCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DropSendTo_ExpansionCommandTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void TryValidateScript_ShouldPass_ForDatePathAndTextLoops()
    {
        var script = string.Join('\n',
            "NOW Stamp FILENAME",
            "PATH_BASENAME Name \"C:\\Temp\\report.txt\"",
            "FOREACH_LINE Line IN \"a\\nb\" INDEX i",
            "    TEXT {{i}}:{{Line}}",
            "ENDFOREACH_LINE",
            "SPLIT Part \"a,b\" \",\" INDEX n",
            "    TEXT {{n}}:{{Part}}",
            "ENDSPLIT");

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public async Task DateTimeCommands_ShouldUseDefaultsAndInjectedLocalClock()
    {
        var local = new DateTimeOffset(2031, 7, 8, 9, 10, 11, TimeSpan.FromHours(9));
        using var service = CreateService(localNow: local);
        var script = string.Join('\n',
            "DATE DateValue",
            "TIME TimeValue",
            "NOW NowValue",
            "RETURN \"{{DateValue}}|{{TimeValue}}|{{NowValue}}\"");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("2031-07-08|09:10:11|2031-07-08T09:10:11");
    }

    [Fact]
    public async Task DateTimeCommands_ShouldHonorUtcLocalFormatAndFilenameOptions()
    {
        var utc = new DateTimeOffset(2031, 7, 8, 0, 10, 11, TimeSpan.Zero);
        var local = utc.ToOffset(TimeSpan.FromHours(9));
        using var service = CreateService(utc, local);
        var script = string.Join('\n',
            "NOW UtcValue UTC FORMAT \"yyyy-MM-dd HH:mm zzz\"",
            "NOW LocalValue LOCAL FORMAT \"yyyy-MM-dd HH:mm zzz\"",
            "NOW FileValue UTC FILENAME",
            "RETURN \"{{UtcValue}}|{{LocalValue}}|{{FileValue}}\"");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("2031-07-08 00:10 +00:00|2031-07-08 09:10 +09:00|20310708-001011");
    }

    [Fact]
    public async Task DateTimeCommand_ShouldFail_ForInvalidFormat()
    {
        using var service = CreateService();

        var result = await service.RunMacroAsync("NOW Stamp FORMAT %");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("日時書式が不正");
    }

    [Fact]
    public async Task PathCommands_ShouldHandleUncRootTrailingSeparatorAndExtensionlessPaths()
    {
        using var service = CreateService();
        const string uncPath = @"\\server\share\folder\report.txt";
        var root = Path.GetPathRoot(_tempRoot)!;
        var trailing = Path.Combine(_tempRoot, "folder") + Path.DirectorySeparatorChar;
        var extensionless = Path.Combine(_tempRoot, "README");
        var script = string.Join('\n',
            $"PATH_DIR UncDir \"{uncPath}\"",
            $"PATH_NAME UncName \"{uncPath}\"",
            $"PATH_BASENAME UncBase \"{uncPath}\"",
            $"PATH_EXT UncExt \"{uncPath}\"",
            $"PATH_DIR RootDir \"{root}\"",
            $"PATH_NAME TrailingName \"{trailing}\"",
            $"PATH_EXT NoExt \"{extensionless}\"",
            $"PATH_FULL Full \"{extensionless}\"",
            "RETURN \"{{UncDir}}|{{UncName}}|{{UncBase}}|{{UncExt}}|{{RootDir}}|{{TrailingName}}|{{NoExt}}|{{Full}}\"");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be(string.Join('|', @"\\server\share\folder", "report.txt", "report", ".txt",
            string.Empty, "folder", string.Empty, Path.GetFullPath(extensionless)));
    }

    [Fact]
    public async Task RunMacroAsync_ShouldRunCatchBlock_WhenRuntimeCommandFails()
    {
        using var service = CreateService();
        var missing = Path.Combine(_tempRoot, "missing.txt");
        var script = string.Join('\n', "TRY", $"    READFILE Body \"{missing}\"", "CATCH ErrorMessage",
            "    RETURN \"{{ErrorMessage}}\"", "ENDTRY");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Contain("READFILE");
    }

    [Fact]
    public void TryValidateScript_ShouldNotCatchSyntaxErrorsInsideTry()
    {
        var script = string.Join('\n', "TRY", "    UNKNOWN_COMMAND", "CATCH ErrorMessage",
            "    TEXT {{ErrorMessage}}", "ENDTRY");

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("未知のマクロ命令");
    }

    [Fact]
    public async Task RunMacroAsync_ShouldExposeTryCatchReservedVariables()
    {
        using var service = CreateService();
        var missing = Path.Combine(_tempRoot, "missing.txt");
        var script = string.Join('\n', "TRY", $"    READFILE Body \"{missing}\"", "CATCH ErrorMessage",
            "    RETURN \"{{error_command}}§{{error_line}}§{{error_message}}§{{ErrorMessage}}\"", "ENDTRY");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().StartWith("TRY§1§");
        result.Message.Should().Contain("READFILE");
        var parts = result.Message.Split('§', 4);
        parts[2].Should().Be(parts[3]);
    }

    [Fact]
    public async Task RunMacroAsync_ShouldNotCatchCancellation()
    {
        using var service = CreateService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var script = string.Join('\n', "TRY", "    WAIT 5000", "CATCH", "    RETURN caught", "ENDTRY");

        var result = await service.RunMacroAsync(script, cancellationToken: cancellation.Token);

        result.Success.Should().BeFalse();
        result.IsCanceled.Should().BeTrue();
        result.Message.Should().NotContain("caught");
    }

    [Fact]
    public async Task RunMacroAsync_ShouldExecuteNestedTryCatchBlocks()
    {
        using var service = CreateService();
        var missing = Path.Combine(_tempRoot, "nested-missing.txt");
        var script = string.Join('\n', "TRY", "    TRY", $"        READFILE Body \"{missing}\"",
            "    CATCH InnerError", "        SET Recovered inner", "    ENDTRY", "CATCH OuterError",
            "    SET Recovered outer", "ENDTRY", "RETURN {{Recovered}}");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("inner");
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForUnclosedTryBlock()
    {
        var ok = KeyboardMacroService.TryValidateScript("TRY\nSET Value ok", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("ENDTRY");
    }

    [Fact]
    public async Task ForeachLine_ShouldHandleCrLfLfCrDropEmptyAndIndex()
    {
        var inputPath = WriteFile("mixed-lines.txt", "a\r\nb\nc\rd\r\n\r\ne");
        using var service = CreateService();
        var script = BuildLineLoopScript(inputPath, keepEmpty: false);

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("[1=a][2=b][3=c][4=d][5=e]");
    }

    [Fact]
    public async Task ForeachLine_ShouldKeepEmptyLines_WhenRequested()
    {
        var inputPath = WriteFile("empty-lines.txt", "a\r\n\r\nb");
        using var service = CreateService();

        var result = await service.RunMacroAsync(BuildLineLoopScript(inputPath, keepEmpty: true));

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("[1=a][2=][3=b]");
    }

    [Fact]
    public async Task Split_ShouldDropOrKeepEmptyItemsAndExposeIndex()
    {
        using var service = CreateService();
        var script = string.Join('\n', "SET Input a,,b,", "SET Dropped",
            "SPLIT Part {{Input}} \",\" INDEX Number", "    SET Dropped {{Dropped}}[{{Number}}={{Part}}]", "ENDSPLIT",
            "SET Kept", "SPLIT Part {{Input}} \",\" INDEX Number KEEP_EMPTY",
            "    SET Kept {{Kept}}[{{Number}}={{Part}}]", "ENDSPLIT", "RETURN \"{{Dropped}}|{{Kept}}\"");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("[1=a][2=b]|[1=a][2=][3=b][4=]");
    }

    [Fact]
    public async Task TextLoops_ShouldExecuteMixedNesting()
    {
        var inputPath = WriteFile("nested-lines.txt", "a,b\r\nc,d");
        using var service = CreateService();
        var script = string.Join('\n', $"READFILE Body \"{inputPath}\"", "SET Result",
            "FOREACH_LINE Line IN {{Body}} INDEX Row", "    SPLIT Part {{Line}} \",\" INDEX Column",
            "        SET Result {{Result}}[{{Row}}.{{Column}}={{Part}}]", "    ENDSPLIT", "ENDFOREACH_LINE",
            "RETURN {{Result}}");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("[1.1=a][1.2=b][2.1=c][2.2=d]");
    }

    [Fact]
    public async Task TextLoop_ShouldFail_WhenItemLimitIsExceeded()
    {
        var inputPath = WriteFile("too-many-lines.txt", string.Join('\n', Enumerable.Repeat("x", 1001)));
        using var service = CreateService();
        var script = string.Join('\n', $"READFILE Body \"{inputPath}\" MAX 8192", "FOREACH_LINE Line IN {{Body}}",
            "    SET Last {{Line}}", "ENDFOREACH_LINE");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("1000");
    }

    [Theory]
    [InlineData("FOREACH_LINE Line IN value\nSET Last {{Line}}")]
    [InlineData("SPLIT Part a,b \",\"\nSET Last {{Part}}")]
    public void TryValidateScript_ShouldFail_ForUnclosedTextLoop(string script)
    {
        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("閉じられていません");
    }

    [Fact]
    public void TryValidateScript_ShouldPass_ForFileProcessAndWindowCommands_InValidationMode()
    {
        var script = string.Join('\n', "MKDIR \"C:\\Temp\\DropSendToValidation\"",
            "COPY \"C:\\Temp\\missing-source.txt\" \"C:\\Temp\\missing-target.txt\"",
            "MOVE \"C:\\Temp\\missing-source.txt\" \"C:\\Temp\\missing-target.txt\"",
            "COMMAND_WAIT ExitCode TIMEOUT 1000 \"cmd.exe\" \"/c\" \"exit 0\"",
            "RUN_CAPTURE ExitCode Out Err TIMEOUT 1000 MAX 1024 \"cmd.exe\" \"/c\" \"echo ok\"",
            "WINDOW_FIND Hwnd TITLE \"Notepad\" INDEX 1", "WINDOW_ACTIVATE {{Hwnd}}");

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public async Task FileCommands_ShouldCreateAndReuseDirectory()
    {
        var directory = Path.Combine(_tempRoot, "created");
        using var service = CreateService();

        var result = await service.RunMacroAsync(string.Join('\n', $"MKDIR \"{directory}\"", $"MKDIR \"{directory}\""));

        result.Success.Should().BeTrue(result.Message);
        Directory.Exists(directory).Should().BeTrue();
    }

    [Fact]
    public async Task Mkdir_ShouldFail_WhenFileHasSameName()
    {
        var path = WriteFile("same-name", "content");
        using var service = CreateService();

        var result = await service.RunMacroAsync($"MKDIR \"{path}\"");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("同名ファイル");
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData("MOVE")]
    public async Task FileCommand_ShouldHandleFiles(string command)
    {
        var source = WriteFile($"{command}-source.txt", "payload");
        var destination = Path.Combine(_tempRoot, $"{command}-destination.txt");
        using var service = CreateService();

        var result = await service.RunMacroAsync($"{command} \"{source}\" \"{destination}\"");

        result.Success.Should().BeTrue(result.Message);
        File.ReadAllText(destination).Should().Be("payload");
        File.Exists(source).Should().Be(command == "COPY");
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData("MOVE")]
    public async Task FileCommand_ShouldHandleDirectoriesRecursively(string command)
    {
        var source = Path.Combine(_tempRoot, $"{command}-source-dir");
        var destination = Path.Combine(_tempRoot, $"{command}-destination-dir");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "nested", "payload.txt"), "payload");
        using var service = CreateService();

        var result = await service.RunMacroAsync($"{command} \"{source}\" \"{destination}\"");

        result.Success.Should().BeTrue(result.Message);
        File.ReadAllText(Path.Combine(destination, "nested", "payload.txt")).Should().Be("payload");
        Directory.Exists(source).Should().Be(command == "COPY");
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData("MOVE")]
    public async Task FileCommand_ShouldFail_WhenDestinationExists(string command)
    {
        var source = WriteFile($"{command}-exists-source.txt", "source");
        var destination = WriteFile($"{command}-exists-destination.txt", "destination");
        using var service = CreateService();

        var result = await service.RunMacroAsync($"{command} \"{source}\" \"{destination}\"");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("既に存在");
        File.ReadAllText(destination).Should().Be("destination");
    }

    [Fact]
    public async Task Copy_ShouldFail_WhenDestinationParentIsMissing()
    {
        var source = WriteFile("missing-parent-source.txt", "source");
        var destination = Path.Combine(_tempRoot, "missing-parent", "destination.txt");
        using var service = CreateService();

        var result = await service.RunMacroAsync($"COPY \"{source}\" \"{destination}\"");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("親ディレクトリ");
    }

    [Fact]
    public async Task FileCommand_ShouldRejectWildcards()
    {
        var source = Path.Combine(_tempRoot, "*.txt");
        var destination = Path.Combine(_tempRoot, "destination.txt");
        using var service = CreateService();

        var result = await service.RunMacroAsync($"COPY \"{source}\" \"{destination}\"");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("パスが不正");
    }

    [Fact]
    public void FileOperation_ShouldSkipSideEffects_WhenValidateOnly()
    {
        var dir = Path.Combine(_tempRoot, "validation-created");
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var args = new object?[] { $"MKDIR \"{dir}\"", variables, null, true, null };

        var ok = (bool)GetFileOperationMethod().Invoke(null, args)!;

        ok.Should().BeTrue();
        Directory.Exists(dir).Should().BeFalse();
    }

    [Fact]
    public async Task RunCapture_ShouldCaptureWorkingDirectoryExitCodeStdoutAndStderr()
    {
        var logger = new CaptureLogger();
        using var service = CreateService(logger: logger);
        var script = string.Join('\n',
            $"RUN_CAPTURE Exit Out Err TIMEOUT 5000 CWD \"{_tempRoot}\" MAX 1024 \"cmd.exe\" \"/d\" \"/c\" \"(cd)&(echo stdout-marker)&(echo stderr-marker 1>&2)&exit /b 7\"",
            "RETURN \"{{Exit}}|{{Out}}|{{Err}}\"");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().StartWith("7|");
        result.Message.Should().Contain(_tempRoot);
        result.Message.Should().Contain("stdout-marker");
        result.Message.Should().Contain("stderr-marker");
        logger.Messages.Should().NotContain(message => message.Contains("stdout-marker", StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message => message.Contains("stderr-marker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommandWait_ShouldReturnExitCode()
    {
        using var service = CreateService();
        var script = string.Join('\n', "COMMAND_WAIT Exit TIMEOUT 5000 \"cmd.exe\" \"/d\" \"/c\" \"exit /b 23\"",
            "RETURN {{Exit}}");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("23");
    }

    [Fact]
    public void ProcessCommand_ShouldConfigureShellDisabled()
    {
        var startInfo = KeyboardMacroService.CreateProcessStartInfoForTesting(
            "tool.exe", new[] { "first", "second" }, _tempRoot, capture: true);

        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.RedirectStandardOutput.Should().BeTrue();
        startInfo.RedirectStandardError.Should().BeTrue();
        startInfo.WorkingDirectory.Should().Be(_tempRoot);
        startInfo.ArgumentList.Should().Equal("first", "second");
    }

    [Fact]
    public async Task RunCapture_ShouldFailAndKillProcess_WhenTimeoutExpires()
    {
        using var service = CreateService();
        var result = await service.RunMacroAsync(
            "RUN_CAPTURE Exit Out Err TIMEOUT 50 MAX 1024 \"cmd.exe\" \"/d\" \"/c\" \"ping -n 6 127.0.0.1 >nul\"");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("タイムアウト");
    }

    [Fact]
    public async Task RunCapture_ShouldCapCapturedOutput()
    {
        using var service = CreateService();
        var script = string.Join('\n',
            "RUN_CAPTURE Exit Out Err TIMEOUT 5000 MAX 4 \"cmd.exe\" \"/d\" \"/c\" \"echo ABCDEFGHIJK\"",
            "RETURN {{Out}}");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("ABCD");
    }

    [Fact]
    public void ProcessCommand_ShouldNotLaunch_WhenValidateOnly()
    {
        var marker = Path.Combine(_tempRoot, "validate-process-marker");
        var script = $"COMMAND_WAIT Exit TIMEOUT 5000 \"cmd.exe\" \"/d\" \"/c\" \"mkdir {marker}\"";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue(error);
        Directory.Exists(marker).Should().BeFalse();
    }

    [Theory]
    [InlineData("TITLE", "partial title")]
    [InlineData("TITLE_EXACT", "exact title")]
    [InlineData("CLASS", "WindowClass")]
    [InlineData("PROCESS", "notepad")]
    [InlineData("PID", "1234")]
    public async Task WindowFind_ShouldPassEachModeToWindowAdapter(string mode, string query)
    {
        var windows = new FakeWindowAdapter();
        windows.SetMatches(mode, query, new IntPtr(42));
        using var service = CreateService(windowAdapter: windows);

        var result = await service.RunMacroAsync(string.Join('\n', $"WINDOW_FIND Hwnd {mode} \"{query}\"", "RETURN {{Hwnd}}"));

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("42");
        windows.Queries.Should().ContainSingle().Which.Should().Be((mode, query));
    }

    [Fact]
    public async Task WindowFind_ShouldFailForMultipleMatchesUnlessIndexIsSpecified()
    {
        var windows = new FakeWindowAdapter();
        windows.SetMatches("TITLE", "Editor", new IntPtr(10), new IntPtr(20));
        using var service = CreateService(windowAdapter: windows);

        var ambiguous = await service.RunMacroAsync("WINDOW_FIND Hwnd TITLE \"Editor\"");
        var selected = await service.RunMacroAsync("WINDOW_FIND Hwnd TITLE \"Editor\" INDEX 2\nRETURN {{Hwnd}}");

        ambiguous.Success.Should().BeFalse();
        ambiguous.Message.Should().Contain("複数");
        selected.Success.Should().BeTrue(selected.Message);
        selected.Message.Should().Be("20");
    }

    [Fact]
    public async Task WindowActivate_ShouldFail_WhenAdapterCannotVerifyActivation()
    {
        var windows = new FakeWindowAdapter { ActivationResult = false };
        windows.SetMatches("TITLE_EXACT", "Target", new IntPtr(42));
        using var service = CreateService(windowAdapter: windows);
        var script = string.Join('\n', "WINDOW_FIND Hwnd TITLE_EXACT \"Target\"", "WINDOW_ACTIVATE {{Hwnd}}");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("WINDOW_ACTIVATE");
        windows.ActivationRequests.Should().ContainSingle().Which.Should().Be(new IntPtr(42));
    }

    [Fact]
    public void WindowCommands_ShouldNotQueryOrActivate_WhenValidateOnly()
    {
        var windows = new FakeWindowAdapter();
        using var service = CreateService(windowAdapter: windows);
        var script = string.Join('\n', "WINDOW_FIND Hwnd TITLE \"Anything\"", "WINDOW_ACTIVATE {{Hwnd}}");

        var ok = service.TryValidateScriptWithDependencies(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue(error);
        windows.Queries.Should().BeEmpty();
        windows.ActivationRequests.Should().BeEmpty();
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForUnknownWindowFindMode()
    {
        var ok = KeyboardMacroService.TryValidateScript("WINDOW_FIND Hwnd UNKNOWN \"value\"",
            SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("WINDOW_FIND");
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForInvalidRunCaptureWorkingDirectory()
    {
        var ok = KeyboardMacroService.TryValidateScript(
            "RUN_CAPTURE ExitCode Out Err CWD \"Z:\\DefinitelyMissing\" \"cmd.exe\"",
            SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("CWD");
    }

    [Fact]
    public void FileOperation_ShouldNotOverwriteExistingDestination()
    {
        var source = WriteFile("source.txt", "source");
        var target = WriteFile("target.txt", "target");
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var args = new object?[] { $"COPY \"{source}\" \"{target}\"", variables, null, false, null };

        var ok = (bool)GetFileOperationMethod().Invoke(null, args)!;

        ok.Should().BeFalse();
        args[4].Should().BeOfType<string>().Subject.Should().Contain("既に存在");
        File.ReadAllText(target).Should().Be("target");
    }

    private KeyboardMacroService CreateService(
        DateTimeOffset? utcNow = null,
        DateTimeOffset? localNow = null,
        IAppLogger? logger = null,
        IMacroWindowAdapter? windowAdapter = null) =>
        new(logger ?? NullAppLogger.Instance,
            () => utcNow ?? new DateTimeOffset(2031, 7, 8, 0, 10, 11, TimeSpan.Zero),
            () => localNow ?? new DateTimeOffset(2031, 7, 8, 9, 10, 11, TimeSpan.FromHours(9)),
            windowAdapter);

    private string BuildLineLoopScript(string inputPath, bool keepEmpty) =>
        string.Join('\n', $"READFILE Body \"{inputPath}\"", "SET Result",
            $"FOREACH_LINE Line IN {{{{Body}}}} INDEX Number{(keepEmpty ? " KEEP_EMPTY" : string.Empty)}",
            "    SET Result {{Result}}[{{Number}}={{Line}}]", "ENDFOREACH_LINE", "RETURN {{Result}}");

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempRoot, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static MethodInfo GetFileOperationMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod("TryApplyFileOperationDirective",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("ファイル操作ヘルパーが存在すること");
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

    private sealed class FakeWindowAdapter : IMacroWindowAdapter
    {
        private readonly Dictionary<(string Mode, string Query), IReadOnlyList<IntPtr>> _matches = new();

        public List<(string Mode, string Query)> Queries { get; } = new();
        public List<IntPtr> ActivationRequests { get; } = new();
        public bool ActivationResult { get; set; } = true;

        public void SetMatches(string mode, string query, params IntPtr[] matches) => _matches[(mode, query)] = matches;

        public IReadOnlyList<IntPtr> FindMatchingWindows(string mode, string query)
        {
            Queries.Add((mode, query));
            return _matches.TryGetValue((mode, query), out var matches) ? matches : Array.Empty<IntPtr>();
        }

        public bool TryActivate(IntPtr hwnd)
        {
            ActivationRequests.Add(hwnd);
            return ActivationResult;
        }
    }

    private sealed class CaptureLogger : IAppLogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }
}
