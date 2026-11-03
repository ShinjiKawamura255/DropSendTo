using System;
using System.Collections.Generic;
using System.Drawing;

namespace DropSendTo.Services;

internal enum MouseGestureAction
{
    None,
    ShowWindow,
    HideWindow
}

internal sealed record MouseGestureOptions(
    bool Enabled,
    int ClockwiseTurnsToShow,
    int CounterClockwiseTurnsToHide,
    bool InvertDirections,
    bool RequireCtrl,
    bool SuppressDuringPresentation,
    bool EnforceRadiusLimit,
    int MinRadiusPixels,
    int MaxRadiusPixels,
    bool EnableDragMiddleClickShow)
{
    public static MouseGestureOptions Default { get; } =
        new(true, 3, 2, false, false, false, true, 0, 140, false);

    public MouseGestureOptions Normalize()
    {
        int Clamp(int value) => Math.Clamp(value, 1, 50);
        int ClampRadius(int value) => Math.Clamp(value, 0, 320);

        var min = ClampRadius(MinRadiusPixels);
        var max = ClampRadius(MaxRadiusPixels);
        if (min > max)
        {
            min = max;
        }

        return this with
        {
            ClockwiseTurnsToShow = Clamp(ClockwiseTurnsToShow),
            CounterClockwiseTurnsToHide = Clamp(CounterClockwiseTurnsToHide),
            MinRadiusPixels = min,
            MaxRadiusPixels = max
        };
    }
}

internal sealed class MouseGestureDetector
{
    private readonly Func<DateTime> _utcNowProvider;
    private readonly object _lock = new();
    private MouseGestureOptions _options = MouseGestureOptions.Default;
    private bool _hasLastPoint;
    private Point _lastPoint;
    private bool _hasLastVector;
    private (double X, double Y) _lastVector;
    private double _accumulatedAngle;
    private int _clockwiseTurns;
    private int _counterClockwiseTurns;
    private DateTime _lastMoveUtc;
    private double _minMovementSquared = CalculateMinMovementSquared(MouseGestureOptions.Default.MaxRadiusPixels);
    private double _minRadiusSquared = CalculateMaxRadiusSquared(MouseGestureOptions.Default.MinRadiusPixels);
    private double _maxRadiusSquared = CalculateMaxRadiusSquared(MouseGestureOptions.Default.MaxRadiusPixels);
    private bool _enforceRadiusLimit = MouseGestureOptions.Default.EnforceRadiusLimit;
    private readonly List<Point> _recentPoints = new();

    private const double StraightAngleTolerance = 0.02;
    private const double FullTurn = Math.PI * 2;
    private const double TurnThreshold = FullTurn * 0.9;
    private const int IdleResetMilliseconds = 330;
    private const double MaxRadiusSlackFactor = 1.2; // allow slight overshoot before resetting
    private const double MinAxisSpreadPixels = 32;
    private const double MinAspectRatio = 0.35;

    public MouseGestureDetector()
        : this(() => DateTime.UtcNow)
    {
    }

    internal MouseGestureDetector(Func<DateTime> utcNowProvider)
    {
        _utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
    }

    public void UpdateOptions(MouseGestureOptions options)
    {
        lock (_lock)
        {
            _options = options.Normalize();
            _minMovementSquared = CalculateMinMovementSquared(_options.MaxRadiusPixels);
            _minRadiusSquared = CalculateMaxRadiusSquared(_options.MinRadiusPixels);
            _maxRadiusSquared = CalculateMaxRadiusSquared(_options.MaxRadiusPixels);
            _enforceRadiusLimit = _options.EnforceRadiusLimit;
            ResetState();
        }
    }

    public MouseGestureAction HandleMove(Point point, bool isCtrlPressed, bool isPresentationBlocked) =>
        HandleMove(point, isCtrlPressed, isPresentationBlocked, out _);

    public MouseGestureAction HandleMove(Point point, bool isCtrlPressed, bool isPresentationBlocked,
        out Point centerPoint)
    {
        centerPoint = point;
        lock (_lock)
        {
            if (!_options.Enabled)
            {
                return MouseGestureAction.None;
            }

            if (_options.SuppressDuringPresentation && isPresentationBlocked)
            {
                ResetState();
                return MouseGestureAction.None;
            }

            var nowUtc = _utcNowProvider();
            TryResetForIdle(nowUtc);
            _lastMoveUtc = nowUtc;

            if (!_hasLastPoint)
            {
                _lastPoint = point;
                _hasLastPoint = true;
                _hasLastVector = false;
                AddRecentPoint(point);
                return MouseGestureAction.None;
            }

            var deltaX = point.X - _lastPoint.X;
            var deltaY = point.Y - _lastPoint.Y;
            var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            if (distanceSquared < _minMovementSquared)
            {
                return MouseGestureAction.None;
            }

            // Measure from the last accepted sample so sub-threshold moves accumulate.
            _lastPoint = point;
            AddRecentPoint(point);
            var center = GetRecentCenter();

            // Invert Y to use mathematical CCW sign convention.
            var adjustedDelta = (X: (double)deltaX, Y: -(double)deltaY);

            if (!_hasLastVector)
            {
                _lastVector = adjustedDelta;
                _hasLastVector = true;
                return MouseGestureAction.None;
            }

            var cross = (_lastVector.X * adjustedDelta.Y) - (_lastVector.Y * adjustedDelta.X);
            var dot = (_lastVector.X * adjustedDelta.X) + (_lastVector.Y * adjustedDelta.Y);
            var angle = Math.Atan2(cross, dot);
            _lastVector = adjustedDelta;

            // Leading straight travel is not part of the circle's bounds. Keep its
            // latest point while direction is unchanged, without discarding rotation.
            if (_clockwiseTurns == 0 && _counterClockwiseTurns == 0 &&
                Math.Abs(_accumulatedAngle) < StraightAngleTolerance && Math.Abs(angle) < StraightAngleTolerance)
            {
                _recentPoints.Clear();
                AddRecentPoint(point);
            }
            _accumulatedAngle += angle;
            while (_accumulatedAngle >= TurnThreshold)
            {
                _counterClockwiseTurns++;
                _accumulatedAngle -= FullTurn;
            }
            while (_accumulatedAngle <= -TurnThreshold)
            {
                _clockwiseTurns++;
                _accumulatedAngle += FullTurn;
            }

            // A short arc can look like a line. Validate shape/radius only once a turn
            // is complete, rather than resetting high-rate input after twelve samples.
            if ((_clockwiseTurns > 0 || _counterClockwiseTurns > 0) && center.HasValue)
            {
                var dx = point.X - center.Value.X;
                var dy = point.Y - center.Value.Y;
                if (IsDegenerate(center.Value) || (_enforceRadiusLimit &&
                    dx * dx + dy * dy > _maxRadiusSquared * MaxRadiusSlackFactor * MaxRadiusSlackFactor))
                {
                    ResetState();
                    return MouseGestureAction.None;
                }
                centerPoint = new Point((int)Math.Round(center.Value.X), (int)Math.Round(center.Value.Y));
            }
            return EvaluateCompletion(isCtrlPressed);
        }
    }

    public void HandleIdleTimeout()
    {
        lock (_lock)
        {
            TryResetForIdle(_utcNowProvider());
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            ResetState();
        }
    }

    internal (int Clockwise, int CounterClockwise, double RemainderAngle) GetTurnCountsForDebug()
    {
        lock (_lock)
        {
            return (_clockwiseTurns, _counterClockwiseTurns, _accumulatedAngle);
        }
    }

    private MouseGestureAction EvaluateCompletion(bool isCtrlPressed)
    {
        if (!_options.InvertDirections)
        {
            if (_clockwiseTurns >= _options.ClockwiseTurnsToShow)
            {
                if (_options.RequireCtrl && !isCtrlPressed)
                {
                    return MouseGestureAction.None;
                }

                ResetState();
                return MouseGestureAction.ShowWindow;
            }

            if (_counterClockwiseTurns >= _options.CounterClockwiseTurnsToHide)
            {
                ResetState();
                return MouseGestureAction.HideWindow;
            }
        }
        else
        {
            if (_counterClockwiseTurns >= _options.ClockwiseTurnsToShow)
            {
                if (_options.RequireCtrl && !isCtrlPressed)
                {
                    return MouseGestureAction.None;
                }

                ResetState();
                return MouseGestureAction.ShowWindow;
            }

            if (_clockwiseTurns >= _options.CounterClockwiseTurnsToHide)
            {
                ResetState();
                return MouseGestureAction.HideWindow;
            }
        }

        return MouseGestureAction.None;
    }

    private void TryResetForIdle(DateTime nowUtc)
    {
        if (!_hasLastPoint)
        {
            return;
        }

        if ((nowUtc - _lastMoveUtc).TotalMilliseconds > IdleResetMilliseconds)
        {
            ResetState();
        }
    }

    private void ResetState()
    {
        _hasLastPoint = false;
        _hasLastVector = false;
        _lastVector = default;
        _accumulatedAngle = 0;
        _clockwiseTurns = 0;
        _counterClockwiseTurns = 0;
        _lastMoveUtc = DateTime.MinValue;
        _recentPoints.Clear();
    }

    private static double CalculateMinMovementSquared(int radiusPixels)
    {
        // Use a small fraction of the configured outer radius as the minimum vector length to suppress jitter.
        double minDistance = Math.Max(3, radiusPixels * 0.05);
        return minDistance * minDistance;
    }

    private static double CalculateMaxRadiusSquared(int radiusPixels)
    {
        return Math.Max(1, radiusPixels) * (double)Math.Max(1, radiusPixels);
    }

    private void AddRecentPoint(Point point)
    {
        // Keep complete spatial history for slow circles too. Decimate at the bound
        // instead of discarding all samples older than one second (a partial arc).
        if (_recentPoints.Count == 512)
        {
            for (int i = 1; i < _recentPoints.Count; i += 2) _recentPoints[i / 2] = _recentPoints[i];
            _recentPoints.RemoveRange(256, 256);
        }
        _recentPoints.Add(point);
    }

    private (double X, double Y)? GetRecentCenter()
    {
        if (_recentPoints.Count == 0) return null;
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var sample in _recentPoints)
        {
            minX = Math.Min(minX, sample.X);
            minY = Math.Min(minY, sample.Y);
            maxX = Math.Max(maxX, sample.X);
            maxY = Math.Max(maxY, sample.Y);
        }
        // The bounding-box center is independent of speed and sample density.
        return ((minX + maxX) / 2, (minY + maxY) / 2);
    }

    private bool IsDegenerate((double X, double Y) center)
    {
        double maxDx = 0;
        double maxDy = 0;
        foreach (var sample in _recentPoints)
        {
            maxDx = Math.Max(maxDx, Math.Abs(sample.X - center.X));
            maxDy = Math.Max(maxDy, Math.Abs(sample.Y - center.Y));
        }

        double maxAxis = Math.Max(maxDx, maxDy);
        double minAxis = Math.Min(maxDx, maxDy);

        if (maxAxis < MinAxisSpreadPixels)
        {
            return true;
        }

        if (minAxis < MinAxisSpreadPixels && minAxis < maxAxis * MinAspectRatio)
        {
            return true;
        }

        return false;
    }
}
