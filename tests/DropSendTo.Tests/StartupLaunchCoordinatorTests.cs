using System.IO;
using DropSendTo.Models;
using DropSendTo.Services;
using Xunit;

namespace DropSendTo.Tests;

public class StartupLaunchCoordinatorTests
{
    [Fact]
    public void Start_ShouldLaunchSavedCurrentLayer_WhenResidentAlreadyExists()
    {
        var config = new AppConfig { CurrentLayer = 2 };
        config.Layers[0].Slots[0].Command = "fallback.exe";
        config.Layers[2].Slots[1].Command = "current.exe";
        string[] args = ["C:\\日本語 folder\\first.txt", "second.txt"];
        SlotModel? launched = null;
        var result = StartupLaunchCoordinator.Start(false, args, () => config, (slot, paths) =>
        {
            launched = slot;
            Assert.Same(args, paths);
            return LaunchResult.Ok();
        });
        Assert.Equal(StartupLaunchStatus.CliLaunched, result.Status);
        Assert.Same(config.Layers[2].Slots[1], launched);
    }

    [Fact]
    public void Start_ShouldRejectSecondWindow_WithoutLoadingSettings()
    {
        var result = StartupLaunchCoordinator.Start(false, [], () => throw new Exception("Must not load"),
            (_, _) => throw new Exception("Must not launch"));
        Assert.Equal(StartupLaunchStatus.AlreadyRunning, result.Status);
    }

    [Fact]
    public void Start_ShouldReturnErrorWithoutSecondWindow_WhenResidentCliFails()
    {
        var config = new AppConfig();
        config.Layers[1].Slots[0].Command = "fallback.exe";
        var result = StartupLaunchCoordinator.Start(false, ["file"], () => config,
            (slot, _) => { Assert.Same(config.Layers[1].Slots[0], slot); return LaunchResult.Fail("failed"); });
        Assert.Equal(StartupLaunchStatus.CliFailed, result.Status);
        Assert.Equal("failed", result.Error);
    }

    [Theory]
    [InlineData(true, (int)StartupLaunchStatus.ShowWindow)]
    [InlineData(false, (int)StartupLaunchStatus.CliFailed)]
    public void Start_ShouldKeepWindowOwnership_WhenNoSlotExists(bool ownsInstance, int expected)
    {
        var result = StartupLaunchCoordinator.Start(ownsInstance, ["file"], () => new AppConfig(),
            (_, _) => throw new Exception("Must not launch"));
        Assert.Equal((StartupLaunchStatus)expected, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Start_ShouldAvoidASecondWindow_WhenResidentSettingsCannotBeRead()
    {
        var result = StartupLaunchCoordinator.Start(false, ["file"], () => throw new IOException("read failure"),
            (_, _) => throw new Exception("Must not launch"));
        Assert.Equal(StartupLaunchStatus.CliFailed, result.Status);
        Assert.Equal("read failure", result.Error);
    }
}
