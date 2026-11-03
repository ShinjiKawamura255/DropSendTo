using System;
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

        var hideWithoutCtrl = Run(detector, options, CreateCirclePoints(2, clockwise: false), ctrlPressed: false, blocked: false);
        hideWithoutCtrl.Should().Be(MouseGestureAction.HideWindow);

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

    [Fact]
    public void HandleIdleTimeout_ShouldResetCounts_AfterOneSecondOfInactivity()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var detector = new MouseGestureDetector(clock.UtcNow);
        var options = MouseGestureOptions.Default;

        detector.UpdateOptions(options);
        foreach (var point in CreateCirclePoints(2, clockwise: true))
        {
            detector.HandleMove(point, false, false);
            clock.Advance(TimeSpan.FromMilliseconds(50));
        }

        var before = detector.GetTurnCountsForDebug();
        before.Clockwise.Should().BeGreaterThan(0);

        clock.Advance(TimeSpan.FromMilliseconds(1_100));
        detector.HandleIdleTimeout();

        var after = detector.GetTurnCountsForDebug();
        after.Clockwise.Should().Be(0);
        after.CounterClockwise.Should().Be(0);
    }

    [Fact]
    public void HandleMove_ShouldIgnoreGesturesOutsideRadius()
    {
        var detector = new MouseGestureDetector();
        var options = MouseGestureOptions.Default with { MaxRadiusPixels = 120, EnforceRadiusLimit = true };

        var result = Run(detector, options, CreateCirclePoints(2, clockwise: true, radius: 180), ctrlPressed: false, blocked: false);
        result.Should().Be(MouseGestureAction.None);

        var counts = detector.GetTurnCountsForDebug();
        counts.Clockwise.Should().Be(0);
        counts.CounterClockwise.Should().Be(0);
    }

    [Fact]
    public void HandleMove_ShouldAllowLargeRadius_WhenLimitDisabled()
    {
        var detector = new MouseGestureDetector();
        var options = MouseGestureOptions.Default with { MaxRadiusPixels = 120, EnforceRadiusLimit = false };

        var result = Run(detector, options, CreateCirclePoints(3, clockwise: true, radius: 200), ctrlPressed: false, blocked: false);
        result.Should().Be(MouseGestureAction.ShowWindow);
    }

    [Fact]
    public void HandleMove_ShouldIgnoreFlattenedGestures()
    {
        var detector = new MouseGestureDetector();
        var options = MouseGestureOptions.Default with { MaxRadiusPixels = 200, EnforceRadiusLimit = true };

        var flatPoints = CreateFlattenedHorizontal(3, width: 200, height: 10);
        var result = Run(detector, options, flatPoints, ctrlPressed: false, blocked: false);
        result.Should().Be(MouseGestureAction.None);

        var counts = detector.GetTurnCountsForDebug();
        counts.Clockwise.Should().Be(0);
        counts.CounterClockwise.Should().Be(0);
    }

    [Theory]
    [InlineData(125, true)]
    [InlineData(500, true)]
    [InlineData(1000, true)]
    [InlineData(1000, false)]
    public void HandleMove_ShouldDetectRepeatedGestures_WhenMovesAreSmallerThanJitterThreshold(
        int samplesPerTurn, bool clockwise)
    {
        var clock = new FakeClock(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        var detector = new MouseGestureDetector(clock.UtcNow);
        var expected = clockwise ? MouseGestureAction.ShowWindow : MouseGestureAction.HideWindow;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            detector.HandleIdleTimeout();
            var actions = new List<MouseGestureAction>();
            for (int i = 0; i <= 4 * samplesPerTurn; i++)
            {
                double angle = (clockwise ? 1 : -1) * 2 * Math.PI * i / samplesPerTurn;
                var point = new Point(-2400 + (int)(120 * Math.Cos(angle)),
                    -600 + (int)(120 * Math.Sin(angle)));
                clock.Advance(TimeSpan.FromSeconds(1.0 / samplesPerTurn));
                var action = detector.HandleMove(point, false, false);
                if (action != MouseGestureAction.None) actions.Add(action);
            }
            actions.Should().Contain(expected, $"repeat={repeat}, samples={samplesPerTurn}");
            actions.Should().OnlyContain(action => action == expected);
        }
    }

    [Fact]
    public void HandleMove_ShouldNotComplete_WhenInputIsStationaryJitter()
    {
        var detector = new MouseGestureDetector();
        for (int i = 0; i < 4000; i++)
        {
            detector.HandleMove(new Point(i % 3, i % 2), false, false)
                .Should().Be(MouseGestureAction.None);
        }
    }

    [Theory]
    [InlineData(-2400, -600, 18, 0.7)]
    [InlineData(4000, 900, 1000, 0.7)]
    [InlineData(4000, 900, 1000, 2.0)]
    public void HandleMove_ShouldReturnGestureCenter_InPhysicalDesktopCoordinates(int x, int y, int samples, double secondsPerTurn)
    {
        var clock = new FakeClock(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        var detector = new MouseGestureDetector(clock.UtcNow);
        MouseGestureAction action = MouseGestureAction.None;
        Point center = default;
        Point cursor = default;
        for (int i = 0; i <= 4 * samples && action == MouseGestureAction.None; i++)
        {
            double angle = 2 * Math.PI * i / samples;
            cursor = new Point(x + (int)(100 * Math.Cos(angle)), y + (int)(100 * Math.Sin(angle)));
            clock.Advance(TimeSpan.FromSeconds(secondsPerTurn / samples));
            action = detector.HandleMove(cursor, false, false, out center);
        }
        action.Should().Be(MouseGestureAction.ShowWindow);
        center.X.Should().BeInRange(x - 3, x + 3);
        center.Y.Should().BeInRange(y - 3, y + 3);
        center.Should().NotBe(cursor, "the display anchor is the circle center, not the final cursor");
    }

    [Fact]
    public void HandleMove_ShouldShowHideAndShowAtAnotherCenter_WithoutRestartOrOptionReset()
    {
        var clock = new FakeClock(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        var detector = new MouseGestureDetector(clock.UtcNow);
        for (int gesture = 0; gesture < 9; gesture++)
        {
            bool clockwise = gesture % 2 == 0;
            var expected = clockwise ? MouseGestureAction.ShowWindow : MouseGestureAction.HideWindow;
            int x = gesture % 3 == 0 ? -2400 : 4000;
            clock.Advance(TimeSpan.FromMilliseconds(400));
            MouseGestureAction action = MouseGestureAction.None;
            for (int i = 0; i <= 4000 && action == MouseGestureAction.None; i++)
            {
                double angle = (clockwise ? 1 : -1) * 2 * Math.PI * i / 1000;
                clock.Advance(TimeSpan.FromMilliseconds(1));
                action = detector.HandleMove(new Point(x + (int)(120 * Math.Cos(angle)),
                    600 + (int)(120 * Math.Sin(angle))), false, false);
            }
            action.Should().Be(expected, $"gesture={gesture}");
        }
    }

    [Fact]
    public void HandleMove_ShouldDetectConfiguredTurns_WhenStraightApproachPrecedesTheCircle()
    {
        var clock = new FakeClock(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        var detector = new MouseGestureDetector(clock.UtcNow);
        for (int x = -2000; x <= 0; x += 10)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            detector.HandleMove(new Point(x, 0), false, false).Should().Be(MouseGestureAction.None);
        }
        MouseGestureAction action = MouseGestureAction.None;
        for (int i = 0; i <= 3000 && action == MouseGestureAction.None; i++)
        {
            double angle = 2 * Math.PI * i / 1000;
            clock.Advance(TimeSpan.FromMilliseconds(1));
            action = detector.HandleMove(new Point((int)(120 * Math.Cos(angle)),
                (int)(120 * Math.Sin(angle))), false, false);
        }
        action.Should().Be(MouseGestureAction.ShowWindow);
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
        return CreateCirclePoints(turns, clockwise, radius);
    }

    private static IEnumerable<Point> CreateCirclePoints(int turns, bool clockwise, double radius)
    {
        // Start at the anchor/center point so that the allowed radius is measured from the initial position.
        yield return new Point(0, 0);

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

    private sealed class FakeClock
    {
        private DateTime _now;

        public FakeClock(DateTime start)
        {
            _now = start;
        }

        public DateTime UtcNow() => _now;

        public void Advance(TimeSpan delta)
        {
            _now += delta;
        }
    }

    private static IEnumerable<Point> CreateFlattenedHorizontal(int turns, double width, double height)
    {
        int segmentsPerTurn = 18;
        for (int i = 0; i <= turns * segmentsPerTurn; i++)
        {
            double progress = (double)i / segmentsPerTurn;
            double angle = progress * 2 * System.Math.PI;
            int x = (int)(width * System.Math.Cos(angle));
            int y = (int)(height * System.Math.Sin(angle));
            yield return new Point(x, y);
        }
    }
}
