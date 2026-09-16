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

    private sealed class InMemoryStartupRegistrationStore : IStartupRegistrationStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string? GetValue(string valueName) => Values.TryGetValue(valueName, out var value) ? value : null;

        public void SetValue(string valueName, string value) => Values[valueName] = value;

        public void DeleteValue(string valueName) => Values.Remove(valueName);
    }
}
