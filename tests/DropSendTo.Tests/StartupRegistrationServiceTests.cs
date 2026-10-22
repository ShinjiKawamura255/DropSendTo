using System;
using System.Collections.Generic;
using DropSendTo.Services;
using Xunit;

namespace DropSendTo.Tests;

public sealed class StartupRegistrationServiceTests
{
    [Fact]
    public void Register_ShouldStoreLaunchCommand()
    {
        var store = new InMemoryStartupRegistrationStore();
        var sut = new StartupRegistrationService(store, () => @"""C:\Tools\DropSendTo.exe""");

        sut.Register();

        Assert.True(sut.IsRegistered());
        Assert.Equal(@"""C:\Tools\DropSendTo.exe""", store.Values[StartupRegistrationService.ValueName]);
    }

    [Fact]
    public void Unregister_ShouldRemoveLaunchCommand()
    {
        var store = new InMemoryStartupRegistrationStore();
        store.Values[StartupRegistrationService.ValueName] = @"""C:\Tools\DropSendTo.exe""";
        var sut = new StartupRegistrationService(store, () => throw new InvalidOperationException());

        sut.Unregister();

        Assert.False(sut.IsRegistered());
        Assert.DoesNotContain(StartupRegistrationService.ValueName, store.Values.Keys);
    }

    [Fact]
    public void Quote_ShouldQuoteWindowsPath()
    {
        var result = StartupCommandBuilder.Quote(@"C:\Program Files\DropSendTo\DropSendTo.exe");

        Assert.Equal(@"""C:\Program Files\DropSendTo\DropSendTo.exe""", result);
    }

    [Theory]
    [InlineData(@"C:\Tools\DropSendTo.exe")]
    [InlineData(@"C:\Program Files\日本語 folder\DropSendTo.exe")]
    public void BuildLaunchCommand_ShouldQuoteOnlyTheExecutable_WhenStartedDirectly(string executable)
    {
        var result = StartupCommandBuilder.BuildLaunchCommand(executable, @"C:\Unused\Different.dll", @"C:\Unused\");

        Assert.Equal($"\"{executable}\"", result);
    }

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", @"C:\日本語 folder\DropSendTo.dll")]
    [InlineData(@"C:\Tools\DOTNET.EXE", @"C:\日本語 folder\Renamed launcher.dll")]
    public void BuildLaunchCommand_ShouldPreserveTheActualDllPath_WhenStartedByDotnet(string host, string entryPoint)
    {
        var result = StartupCommandBuilder.BuildLaunchCommand(host, entryPoint, @"C:\Other directory\");

        Assert.Equal($"\"{host}\" \"{entryPoint}\"", result);
    }

    [Theory]
    [InlineData("Renamed launcher.dll")]
    [InlineData(@".\Renamed launcher.dll")]
    [InlineData(@"subdirectory\Renamed launcher.dll")]
    public void BuildLaunchCommand_ShouldUseTheEntryDirectory_WhenTheRuntimeEntryNameIsRelative(string entryPoint)
    {
        var result = StartupCommandBuilder.BuildLaunchCommand(@"C:\dotnet\dotnet.exe", entryPoint, @"C:\日本語 folder\");

        Assert.Equal(@"""C:\dotnet\dotnet.exe"" ""C:\日本語 folder\Renamed launcher.dll""", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void BuildLaunchCommand_ShouldRejectMissingProcessPath(string? processPath)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StartupCommandBuilder.BuildLaunchCommand(processPath, @"C:\App\DropSendTo.dll", @"C:\App\"));

        Assert.Equal("実行ファイルのパスを取得できませんでした。", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void BuildLaunchCommand_ShouldRejectMissingEntryPoint_WhenStartedByDotnet(string? entryPoint)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StartupCommandBuilder.BuildLaunchCommand(@"C:\dotnet\dotnet.exe", entryPoint, @"C:\App\"));

        Assert.Equal("アプリケーションの起動パスを取得できませんでした。", error.Message);
    }

    private sealed class InMemoryStartupRegistrationStore : IStartupRegistrationStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string? GetValue(string valueName) => Values.TryGetValue(valueName, out var value) ? value : null;

        public void SetValue(string valueName, string value) => Values[valueName] = value;

        public void DeleteValue(string valueName) => Values.Remove(valueName);
    }
}
