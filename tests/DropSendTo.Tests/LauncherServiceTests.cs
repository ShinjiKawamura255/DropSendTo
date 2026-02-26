using System.Diagnostics;
using DropSendTo.Models;
using DropSendTo.Services;
using Xunit;

namespace DropSendTo.Tests;

public class LauncherServiceTests
{
    [Fact]
    public void Launch_WhenProcessIdIsAvailable_SchedulesForegroundPromotion()
    {
        var sut = new TestableLauncherService
        {
            ProcessToReturn = new Process(),
            ProcessIdToReturn = 4321
        };
        var slot = CreateSlot();

        var result = sut.Launch(slot, []);

        Assert.True(result.Success);
        Assert.Single(sut.ScheduledProcessIds);
        Assert.Equal(4321, sut.ScheduledProcessIds[0]);
    }

    [Fact]
    public void Launch_WhenProcessIdIsUnavailable_DoesNotScheduleForegroundPromotion()
    {
        var sut = new TestableLauncherService
        {
            ProcessToReturn = new Process(),
            ProcessIdToReturn = 0
        };
        var slot = CreateSlot();

        var result = sut.Launch(slot, []);

        Assert.True(result.Success);
        Assert.Empty(sut.ScheduledProcessIds);
    }

    private static SlotModel CreateSlot()
    {
        return new SlotModel
        {
            Title = "test",
            Command = @"C:\Tools\SomeApp.exe",
            ArgumentsTemplate = "{args}"
        };
    }

    private sealed class TestableLauncherService : LauncherService
    {
        public Process? ProcessToReturn { get; set; }
        public int ProcessIdToReturn { get; set; }
        public List<int> ScheduledProcessIds { get; } = [];

        internal override Process? StartProcess(ProcessStartInfo startInfo)
        {
            return ProcessToReturn;
        }

        internal override int GetProcessId(Process? process)
        {
            return ProcessIdToReturn;
        }

        internal override void ScheduleLaunchedProcessForegroundPromotion(int processId, string slotTitle)
        {
            ScheduledProcessIds.Add(processId);
        }
    }
}
