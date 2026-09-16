using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class MacroConcurrencyCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldCancelCurrentAndSkipExecution_WhenSameSlotIsTriggered()
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events) { IsMacroRunning = true };
        var coordinator = new MacroConcurrencyCoordinator(runtime, new CaptureLogger(events));

        var result = await coordinator.ExecuteAsync(
            CreateRequest(MacroConcurrencyMode.Exclusive, events, isSameSlotRunning: true),
            _ =>
            {
                events.Add("execute");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        result.Should().Be(MacroConcurrencyExecutionResult.SameSlotCancellationRequested);
        events.Should().Equal("cancel-current", "info", "cancel-ui");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectAndWarn_WhenExclusiveModeHasAnotherRunningMacro()
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events) { IsMacroRunning = true };
        var coordinator = new MacroConcurrencyCoordinator(runtime, new CaptureLogger(events));

        var result = await coordinator.ExecuteAsync(
            CreateRequest(MacroConcurrencyMode.Exclusive, events),
            _ =>
            {
                events.Add("execute");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        result.Should().Be(MacroConcurrencyExecutionResult.ExclusiveRejected);
        events.Should().Equal("warn", "reject:Exclusive");
    }

    [Theory]
    [InlineData(MacroConcurrencyMode.Exclusive)]
    [InlineData(MacroConcurrencyMode.Interrupt)]
    [InlineData(MacroConcurrencyMode.SuspendAndResume)]
    public async Task ExecuteAsync_ShouldAllowCommandOnlySlot_InEveryMode(MacroConcurrencyMode mode)
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events) { IsMacroRunning = true };
        var coordinator = new MacroConcurrencyCoordinator(runtime, new CaptureLogger(events));

        var result = await coordinator.ExecuteAsync(
            CreateRequest(mode, events, shouldRunMacro: false),
            _ =>
            {
                events.Add("execute");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        result.Should().Be(MacroConcurrencyExecutionResult.Executed);
        events.Should().Equal("info", "execute");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAwaitCancelAllBeforeExecuting_WhenInterruptModeIsSelected()
    {
        var events = new List<string>();
        var cancelStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCancelToFinish = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime(events)
        {
            IsMacroRunning = true,
            CancelAllAsync = async cancellationToken =>
            {
                events.Add("cancel-all-start");
                cancelStarted.TrySetResult(null);
                await allowCancelToFinish.Task.WaitAsync(cancellationToken);
                events.Add("cancel-all-end");
                return true;
            }
        };
        var coordinator = new MacroConcurrencyCoordinator(runtime, new CaptureLogger(events));

        var execution = coordinator.ExecuteAsync(
            CreateRequest(MacroConcurrencyMode.Interrupt, events),
            _ =>
            {
                events.Add("execute");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await cancelStarted.Task;
        events.Should().NotContain("execute");
        allowCancelToFinish.TrySetResult(null);

        (await execution).Should().Be(MacroConcurrencyExecutionResult.Executed);
        events.Should().Equal("info", "cancel-ui", "cancel-all-start", "cancel-all-end", "execute");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ShouldRejectAndRestoreUi_WhenSuspendFails(bool throwException)
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events)
        {
            IsMacroRunning = true,
            SuspendAsync = (_, _) => throwException
                ? Task.FromException<IAsyncDisposable?>(new InvalidOperationException("busy"))
                : Task.FromResult<IAsyncDisposable?>(null)
        };
        var coordinator = new MacroConcurrencyCoordinator(runtime, new CaptureLogger(events));

        var result = await coordinator.ExecuteAsync(
            CreateRequest(MacroConcurrencyMode.SuspendAndResume, events),
            _ =>
            {
                events.Add("execute");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        result.Should().Be(MacroConcurrencyExecutionResult.SuspendRejected);
        events.Should().Equal("pause:True", "suspend", "warn", "pause:False", "reject:SuspendFailed");
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("exception")]
    public async Task ExecuteAsync_ShouldAlwaysDisposeSuspendLease(string completion)
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events)
        {
            IsMacroRunning = true,
            SuspendAsync = (_, _) => Task.FromResult<IAsyncDisposable?>(new CaptureLease("resume", events))
        };
        var coordinator = new MacroConcurrencyCoordinator(runtime, new CaptureLogger(events));
        var request = CreateRequest(MacroConcurrencyMode.SuspendAndResume, events);

        Func<Task> act = async () => await coordinator.ExecuteAsync(
            request,
            _ => completion switch
            {
                "cancel" => Task.FromCanceled(new CancellationToken(canceled: true)),
                "exception" => Task.FromException(new InvalidOperationException("boom")),
                _ => Task.CompletedTask
            },
            CancellationToken.None);

        if (completion == "cancel")
        {
            await act.Should().ThrowAsync<TaskCanceledException>();
        }
        else if (completion == "exception")
        {
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            await act();
        }

        events.Should().ContainInOrder("pause:True", "suspend", "resume", "pause:False");
        events.Should().ContainSingle(item => item == "resume");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldResumeNestedSuspensions_InLifoOrder()
    {
        var events = new List<string>();
        var leaseNumber = 0;
        var runtime = new FakeRuntime(events)
        {
            IsMacroRunning = true,
            SuspendAsync = (_, _) =>
            {
                int current = ++leaseNumber;
                events.Add($"suspend:{current}");
                return Task.FromResult<IAsyncDisposable?>(new CaptureLease($"resume:{current}", events));
            }
        };
        var coordinator = new MacroConcurrencyCoordinator(runtime, new CaptureLogger(events));
        var request = CreateRequest(MacroConcurrencyMode.SuspendAndResume, events);

        await coordinator.ExecuteAsync(
            request,
            async _ =>
            {
                await coordinator.ExecuteAsync(request, _ => Task.CompletedTask, CancellationToken.None);
            },
            CancellationToken.None);

        events.IndexOf("resume:2").Should().BeLessThan(events.IndexOf("resume:1"));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWarnRecoverAndRestoreUi_WhenResumeFails()
    {
        var events = new List<string>();
        var runtime = new FakeRuntime(events)
        {
            IsMacroRunning = true,
            SuspendAsync = (_, _) => Task.FromResult<IAsyncDisposable?>(new ThrowingLease(events))
        };
        var coordinator = new MacroConcurrencyCoordinator(runtime, new CaptureLogger(events));
        var request = CreateRequest(MacroConcurrencyMode.SuspendAndResume, events) with
        {
            OnResumeFailedAsync = _ =>
            {
                events.Add("resume-recovery");
                return Task.CompletedTask;
            }
        };

        await coordinator.ExecuteAsync(
            request,
            _ =>
            {
                events.Add("execute");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        events.Should().Equal(
            "pause:True",
            "suspend",
            "execute",
            "resume-throw",
            "warn",
            "resume-recovery",
            "pause:False");
    }

    private static MacroConcurrencyRequest CreateRequest(
        MacroConcurrencyMode mode,
        List<string> events,
        bool shouldRunMacro = true,
        bool isSameSlotRunning = false) =>
        new(
            mode,
            shouldRunMacro,
            isSameSlotRunning,
            "layer=1, slot=2, source=Test",
            () => events.Add("cancel-ui"),
            paused => events.Add($"pause:{paused}"),
            reason => events.Add($"reject:{reason}"),
            _ => Task.CompletedTask);

    private sealed class FakeRuntime : IMacroConcurrencyRuntime
    {
        private readonly List<string> _events;

        public FakeRuntime(List<string> events)
        {
            _events = events;
        }

        public bool IsMacroRunning { get; set; }
        public Func<CancellationToken, Task<bool>>? CancelAllAsync { get; set; }
        public Func<TimeSpan, CancellationToken, Task<IAsyncDisposable?>>? SuspendAsync { get; set; }

        public bool CancelCurrentMacro()
        {
            _events.Add("cancel-current");
            return true;
        }

        public Task<bool> CancelAllRunningMacrosAsync(CancellationToken cancellationToken) =>
            CancelAllAsync?.Invoke(cancellationToken) ?? Task.FromResult(true);

        public Task<IAsyncDisposable?> SuspendCurrentMacroAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            _events.Add("suspend");
            if (SuspendAsync != null)
            {
                return SuspendAsync(timeout, cancellationToken);
            }
            return Task.FromResult<IAsyncDisposable?>(new CaptureLease("resume", _events));
        }
    }

    private sealed class CaptureLease : IAsyncDisposable
    {
        private readonly string _event;
        private readonly List<string> _events;

        public CaptureLease(string @event, List<string> events)
        {
            _event = @event;
            _events = events;
        }

        public ValueTask DisposeAsync()
        {
            _events.Add(_event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingLease : IAsyncDisposable
    {
        private readonly List<string> _events;

        public ThrowingLease(List<string> events)
        {
            _events = events;
        }

        public ValueTask DisposeAsync()
        {
            _events.Add("resume-throw");
            return ValueTask.FromException(new InvalidOperationException("resume failed"));
        }
    }

    private sealed class CaptureLogger : IAppLogger
    {
        private readonly List<string> _events;

        public CaptureLogger(List<string> events)
        {
            _events = events;
        }

        public void Info(string message) => _events.Add("info");
        public void Warn(string message) => _events.Add("warn");
        public void Error(string message) => _events.Add("error");
    }
}
