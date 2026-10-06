using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ActiveWindowMonitorTests
{
    [Fact]
    public void Monitor_ShouldDiscardStaleReadAndCoalesceToLatestWindow()
    {
        RunSta(() =>
        {
            var source = new ControlledSource();
            using var monitor = new ActiveWindowMonitor(Dispatcher.CurrentDispatcher, source, TimeSpan.FromMilliseconds(1));
            var published = new List<ActiveWindowInfo?>();
            monitor.Changed += (info, _) => published.Add(info);
            monitor.Start();
            source.Signal(1);
            PumpUntil(() => source.Reads.Count == 1);
            source.Reads[0].Handle.Should().Be(new IntPtr(1));
            source.Signal(2);
            source.Signal(3);
            source.Reads.Count.Should().Be(1);
            source.Reads[0].Completion.SetResult(Window(1));
            PumpUntil(() => source.Reads.Count == 2);
            published.Should().BeEmpty();
            source.Reads[1].Handle.Should().Be(new IntPtr(3));
            source.Reads[1].Completion.SetResult(Window(3));
            PumpUntil(() => published.Count == 1);
            published[0]!.Handle.Should().Be(new IntPtr(3));
            source.Reads.Count.Should().Be(2);
        });
    }

    [Fact]
    public void Monitor_ShouldIgnoreStaleFailureAndRecoverFromLatestReadFailure()
    {
        RunSta(() =>
        {
            var source = new ControlledSource();
            using var monitor = new ActiveWindowMonitor(Dispatcher.CurrentDispatcher, source, TimeSpan.FromMilliseconds(1));
            int failures = 0;
            ActiveWindowInfo? published = null;
            monitor.Failed += _ => failures++;
            monitor.Changed += (info, _) => published = info;
            monitor.Start();
            source.Signal(1);
            PumpUntil(() => source.Reads.Count == 1);
            source.Signal(2);
            source.Reads[0].Completion.SetException(new InvalidOperationException("stale failure"));
            PumpUntil(() => source.Reads.Count == 2);
            failures.Should().Be(0);
            source.Reads[1].Completion.SetException(new InvalidOperationException("latest failure"));
            PumpUntil(() => failures == 1);
            source.Signal(3);
            PumpUntil(() => source.Reads.Count == 3);
            source.Reads[2].Completion.SetResult(Window(3));
            PumpUntil(() => published != null);
            published!.Handle.Should().Be(new IntPtr(3));
            failures.Should().Be(1);
        });
    }

    [Fact]
    public void Dispose_ShouldCancelInFlightReadAndReleaseSourceExactlyOnce()
    {
        RunSta(() =>
        {
            var source = new ControlledSource();
            var monitor = new ActiveWindowMonitor(Dispatcher.CurrentDispatcher, source, TimeSpan.FromMilliseconds(1));
            int published = 0;
            monitor.Changed += (_, _) => published++;
            monitor.Start();
            source.Signal(1);
            PumpUntil(() => source.Reads.Count == 1);
            source.FinishedReads.Should().Be(0);
            var watch = Stopwatch.StartNew();
            monitor.Dispose();
            monitor.Dispose();
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
            source.DisposeCount.Should().Be(1);
            PumpUntil(() => source.FinishedReads == 1);
            source.Reads[0].Token.IsCancellationRequested.Should().BeTrue();
            source.Signal(2);
            published.Should().Be(0);
            source.Reads.Count.Should().Be(1);
        });
    }

    [Fact]
    public void Start_ShouldReleaseSourceWhenHookSetupFails()
    {
        RunSta(() =>
        {
            var source = new ControlledSource { FailStart = true };
            using var monitor = new ActiveWindowMonitor(Dispatcher.CurrentDispatcher, source);
            var action = () => monitor.Start();
            action.Should().Throw<InvalidOperationException>();
            source.DisposeCount.Should().Be(1);
        });
    }

    [Fact]
    public void Monitor_ShouldRetainExternalHandleWhileOwnWindowsAreForeground()
    {
        RunSta(() =>
        {
            var source = new ControlledSource();
            using var monitor = new ActiveWindowMonitor(Dispatcher.CurrentDispatcher, source, TimeSpan.FromMilliseconds(1));
            bool? own = null;
            int externalChanges = 0;
            monitor.ExternalTargetChanged += () => externalChanges++;
            monitor.Changed += (_, value) => own = value;
            monitor.Start();
            source.Signal(7);
            source.Signal(8, isOwn: true);
            externalChanges.Should().Be(1, "the pending external activation must survive coalescing into an own-window event");
            PumpUntil(() => source.Reads.Count == 1);
            source.Reads[0].Handle.Should().Be(new IntPtr(7));
            source.Reads[0].Completion.SetResult(Window(7));
            PumpUntil(() => own.HasValue);
            own.Should().BeTrue();
        });
    }

    private static ActiveWindowInfo Window(int handle) => new(new IntPtr(handle), "test.exe", "Test", "C:\\test.exe");

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15)).Should().BeTrue("the dispatcher test must terminate");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) => { if (condition() || watch.Elapsed > TimeSpan.FromSeconds(5)) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        condition().Should().BeTrue($"the expected dispatcher state must be reached (elapsed={watch.Elapsed})");
    }

    private sealed class ControlledSource : IActiveWindowSource
    {
        public event Action<IntPtr, bool>? WindowChanged;
        public List<ReadRequest> Reads { get; } = new();
        public int FinishedReads { get; private set; }
        public int DisposeCount { get; private set; }
        public bool FailStart { get; init; }
        public void Start() { if (FailStart) throw new InvalidOperationException("setup failed"); }
        public void Signal(int hwnd, bool isOwn = false) => WindowChanged?.Invoke(new IntPtr(hwnd), isOwn);
        public async Task<ActiveWindowInfo?> ReadAsync(IntPtr handle, CancellationToken token)
        {
            var request = new ReadRequest(handle, token);
            Reads.Add(request);
            try { return await request.Completion.Task.WaitAsync(token); }
            finally { FinishedReads++; }
        }
        public void Dispose() => DisposeCount++;
    }

    private sealed record ReadRequest(IntPtr Handle, CancellationToken Token)
    {
        public TaskCompletionSource<ActiveWindowInfo?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
