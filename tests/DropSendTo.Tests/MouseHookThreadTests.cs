using System;
using System.Threading;
using System.Windows.Threading;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class MouseHookThreadTests
{
    [Fact]
    public void Hook_ShouldKeepProcessingAndCleanUp_WhenCallingThreadCannotPumpMessages()
    {
        int callerThread = Environment.CurrentManagedThreadId;
        int installThread = 0, callbackThread = 0, cleanupThread = 0, cleanupCount = 0;
        Dispatcher? dispatcher = null;
        MouseHookThread.Callback? installed = null;
        using var completed = new ManualResetEventSlim();
        var hook = new MouseHookThread((_, _, _) =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            completed.Set();
            return IntPtr.Zero;
        }, callback =>
        {
            installThread = Environment.CurrentManagedThreadId;
            dispatcher = Dispatcher.CurrentDispatcher;
            installed = callback;
            return new IntPtr(123);
        }, handle =>
        {
            handle.Should().Be(new IntPtr(123));
            cleanupThread = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref cleanupCount);
        });
        try
        {
            installThread.Should().NotBe(callerThread, "UI stalls must not stall the mouse hook");
            // This thread deliberately blocks without pumping any UI/Dispatcher messages.
            for (int i = 0; i < 3; i++)
            {
                completed.Reset();
                dispatcher!.BeginInvoke(new Action(() => installed!(0, IntPtr.Zero, IntPtr.Zero)));
                completed.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue("hook message pump must remain responsive");
                callbackThread.Should().Be(installThread);
            }
        }
        finally
        {
            hook.Dispose();
            hook.Dispose();
        }
        cleanupCount.Should().Be(1);
        cleanupThread.Should().Be(installThread);
    }
    [Fact]
    public void Constructor_ShouldReportInstallationFailure_WithoutOwningAHook()
    {
        int cleanups = 0;
        Action start = () => new MouseHookThread((_, _, _) => IntPtr.Zero,
            _ => throw new InvalidOperationException("installation failed"),
            _ => Interlocked.Increment(ref cleanups));
        start.Should().Throw<InvalidOperationException>().WithMessage("installation failed");
        cleanups.Should().Be(0);
    }

    [Fact]
    public async System.Threading.Tasks.Task Constructor_ShouldCleanUpLateInstallation_WhenStartupTimesOut()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cleaned = new ManualResetEventSlim();
        int cleanups = 0;
        var attempt = System.Threading.Tasks.Task.Run(() =>
        {
            Action start = () => new MouseHookThread((_, _, _) => IntPtr.Zero, _ =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("test gate");
                return new IntPtr(123);
            }, _ => { Interlocked.Increment(ref cleanups); cleaned.Set(); }, TimeSpan.FromMilliseconds(200));
            start.Should().Throw<TimeoutException>();
        });
        try
        {
            entered.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue("installer must reach the controlled gate");
            await attempt.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            release.Set();
            cleaned.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue("late successful installs must be unhooked");
        }
        cleanups.Should().Be(1);
    }

    [Fact]
    public void Hook_ShouldCleanUp_WhenItsMessagePumpStopsUnexpectedly()
    {
        Dispatcher? dispatcher = null;
        using var cleaned = new ManualResetEventSlim();
        using var hook = new MouseHookThread((_, _, _) => IntPtr.Zero, _ =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            return new IntPtr(123);
        }, _ => cleaned.Set());
        dispatcher!.BeginInvoke(new Action(() => throw new InvalidOperationException("worker failure")));
        cleaned.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue("worker failure must release the native hook");
    }

    [Fact]
    public async System.Threading.Tasks.Task Dispose_ShouldReleaseHookOnce_WhenSeveralCallersStopConcurrently()
    {
        int cleanups = 0;
        using var hook = new MouseHookThread((_, _, _) => IntPtr.Zero, _ => new IntPtr(123),
            _ => Interlocked.Increment(ref cleanups));
        var stops = new System.Threading.Tasks.Task[8];
        for (int i = 0; i < stops.Length; i++) stops[i] = System.Threading.Tasks.Task.Run(hook.Dispose);
        await System.Threading.Tasks.Task.WhenAll(stops).WaitAsync(TimeSpan.FromSeconds(6));
        cleanups.Should().Be(1);
    }

}
