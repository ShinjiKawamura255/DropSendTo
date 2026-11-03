using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutServiceMouseGestureDispatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispatch_ShouldRetainLatestCenterAndIgnoreStoppedService_WhenUiResumes(bool stopped)
    {
        Exception? failure = null;
        var ui = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var service = (ShortcutService)RuntimeHelpers.GetUninitializedObject(typeof(ShortcutService));
                typeof(ShortcutService).GetField("_dispatcher", flags)!.SetValue(service, dispatcher);
                typeof(ShortcutService).GetField("_mouseGestureOptions", flags)!
                    .SetValue(service, MouseGestureOptions.Default);
                var requestType = typeof(ShortcutService).GetNestedType("MouseGestureRequest", BindingFlags.NonPublic)!;
                var pending = typeof(ShortcutService).GetField("_pendingMouseGesture", flags)!;
                var queue = typeof(ShortcutService).GetMethod("QueueMouseGestureDispatch", flags)!;
                int posted = 0, shows = 0, hides = 0;
                Point received = default;
                service.MouseGestureShowRequested += (_, e) => { shows++; received = e.Center; };
                service.MouseGestureHideRequested += (_, _) => hides++;
                dispatcher.Hooks.OperationPosted += (_, _) => Interlocked.Increment(ref posted);
                Exception? workerFailure = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        foreach (var action in new[] { MouseGestureAction.ShowWindow,
                            MouseGestureAction.HideWindow, MouseGestureAction.ShowWindow })
                        {
                            pending.SetValue(service, Activator.CreateInstance(requestType, action, new Point(-2400, 900)));
                            queue.Invoke(service, null);
                        }
                    }
                    catch (Exception ex) { workerFailure = ex; }
                })
                { IsBackground = true };
                worker.Start();
                worker.Join(TimeSpan.FromSeconds(3)).Should().BeTrue("UI is deliberately not pumping");
                workerFailure.Should().BeNull();
                posted.Should().Be(1, "a busy UI must retain at most one pending gesture operation");
                shows.Should().Be(0);
                typeof(ShortcutService).GetField("_disposed", flags)!.SetValue(service, stopped);
                dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                shows.Should().Be(stopped ? 0 : 1);
                hides.Should().Be(0, "intermediate requests are superseded by the latest request");
                if (!stopped) received.Should().Be(new Point(-2400, 900));
            }
            catch (Exception ex) { failure = ex; }
            finally { dispatcher.InvokeShutdown(); }
        })
        { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join(TimeSpan.FromSeconds(5)).Should().BeTrue("UI fixture must stop within its deadline");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
