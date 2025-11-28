using System.Collections.Generic;
using System.Drawing;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class MouseGestureDetectorTests
{
    [Fact]
    public void HandleMove_ShouldTriggerShow_OnClockwiseTurns()
    {
        var detector = new MouseGestureDetector();
        var options = MouseGestureOptions.Default;

        var result = Run(detector, options, CreateCirclePoints(3, clockwise: true), ctrlPressed: false, blocked: false);
        var counts = detector.GetTurnCountsForDebug();

        result.Should().Be(MouseGestureAction.ShowWindow, $"cw={counts.Clockwise}, ccw={counts.CounterClockwise}, angle={counts.RemainderAngle}");
    }

    [Fact]
    public void HandleMove_ShouldTriggerHide_OnCounterClockwiseTurns()
    {
        var detector = new MouseGestureDetector();
        var options = MouseGestureOptions.Default;

        var result = Run(detector, options, CreateCirclePoints(2, clockwise: false), ctrlPressed: false, blocked: false);
        var counts = detector.GetTurnCountsForDebug();

        result.Should().Be(MouseGestureAction.HideWindow, $"cw={counts.Clockwise}, ccw={counts.CounterClockwise}, angle={counts.RemainderAngle}");
    }

    [Fact]
    public void HandleMove_ShouldInvertDirections_WhenConfigured()
    {
        var detector = new MouseGestureDetector();
        var options = MouseGestureOptions.Default with { InvertDirections = true };

        var result = Run(detector, options, CreateCirclePoints(2, clockwise: true), ctrlPressed: false, blocked: false);
        var counts = detector.GetTurnCountsForDebug();

        result.Should().Be(MouseGestureAction.HideWindow, $"cw={counts.Clockwise}, ccw={counts.CounterClockwise}, angle={counts.RemainderAngle}");
    }

    [Fact]
    public void HandleMove_ShouldRequireCtrl_WhenEnabled()
    {
        var detector = new MouseGestureDetector();
        var options = MouseGestureOptions.Default with { RequireCtrl = true };

        var noCtrl = Run(detector, options, CreateCirclePoints(3, clockwise: true), ctrlPressed: false, blocked: false);
        noCtrl.Should().Be(MouseGestureAction.None);

        var withCtrl = Run(detector, options, CreateCirclePoints(3, clockwise: true), ctrlPressed: true, blocked: false);
        var counts = detector.GetTurnCountsForDebug();
        withCtrl.Should().Be(MouseGestureAction.ShowWindow, $"cw={counts.Clockwise}, ccw={counts.CounterClockwise}, angle={counts.RemainderAngle}");
    }

    [Fact]
    public void HandleMove_ShouldSuppressDuringPresentation_WhenBlocked()
    {
        var detector = new MouseGestureDetector();
        var options = MouseGestureOptions.Default with { SuppressDuringPresentation = true };

        var result = Run(detector, options, CreateCirclePoints(3, clockwise: true), ctrlPressed: false, blocked: true);

        result.Should().Be(MouseGestureAction.None);
    }

    private static MouseGestureAction Run(
        MouseGestureDetector detector,
        MouseGestureOptions options,
        IEnumerable<Point> points,
        bool ctrlPressed,
        bool blocked)
    {
        detector.UpdateOptions(options);
        MouseGestureAction last = MouseGestureAction.None;
        foreach (var point in points)
        {
            last = detector.HandleMove(point, ctrlPressed, blocked);
            if (last != MouseGestureAction.None)
            {
                break;
            }
        }

        return last;
    }

    private static IEnumerable<Point> CreateCirclePoints(int turns, bool clockwise)
    {
        const double radius = 120;
        const int segmentsPerTurn = 18;
        int direction = clockwise ? 1 : -1;
        for (int i = 0; i <= turns * segmentsPerTurn; i++)
        {
            double progress = (double)i / segmentsPerTurn;
            double angle = direction * progress * 2 * System.Math.PI;
            int x = (int)(radius * System.Math.Cos(angle));
            int y = (int)(radius * System.Math.Sin(angle));
            yield return new Point(x, y);
        }
    }
}
