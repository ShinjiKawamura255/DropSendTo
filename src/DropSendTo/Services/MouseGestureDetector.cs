using System;
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
    int MaxRadiusPixels)
{
    public static MouseGestureOptions Default { get; } =
        new(true, 3, 2, false, false, false, true, 0, 140);

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
    private readonly List<(double X, double Y, DateTime Timestamp)> _recentPoints = new();

    private const double FullTurn = Math.PI * 2;
    private const double TurnThreshold = FullTurn * 0.9;
    private const int IdleResetMilliseconds = 330;
    private const double MaxRadiusSlackFactor = 1.2; // allow slight overshoot before resetting
    private const double CenterWindowSeconds = 2.0;

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

    public MouseGestureAction HandleMove(Point point, bool isCtrlPressed, bool isPresentationBlocked)
    {
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

            if (_options.RequireCtrl && !isCtrlPressed)
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
                AddRecentPoint(point, nowUtc);
                return MouseGestureAction.None;
            }

            var deltaX = point.X - _lastPoint.X;
            var deltaY = point.Y - _lastPoint.Y;
            var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            _lastPoint = point;
            if (distanceSquared < _minMovementSquared)
            {
                AddRecentPoint(point, nowUtc);
                return MouseGestureAction.None;
            }

            AddRecentPoint(point, nowUtc);
            var center = GetRecentCenter();

            if (_enforceRadiusLimit && center.HasValue)
            {
                var anchorDx = point.X - center.Value.X;
                var anchorDy = point.Y - center.Value.Y;
                var anchorDistanceSquared = (anchorDx * anchorDx) + (anchorDy * anchorDy);
                var softMaxSquared = _maxRadiusSquared * (MaxRadiusSlackFactor * MaxRadiusSlackFactor);
                if (anchorDistanceSquared > softMaxSquared)
                {
                    ResetState();
                    _lastPoint = point;
                    _hasLastPoint = true;
                    AddRecentPoint(point, nowUtc);
                    return MouseGestureAction.None;
                }
                if (anchorDistanceSquared < _minRadiusSquared)
                {
                    ResetState();
                    _lastPoint = point;
                    _hasLastPoint = true;
                    AddRecentPoint(point, nowUtc);
                    return MouseGestureAction.None;
                }
            }

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

            return EvaluateCompletion();
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

    private MouseGestureAction EvaluateCompletion()
    {
        if (!_options.InvertDirections)
        {
            if (_clockwiseTurns >= _options.ClockwiseTurnsToShow)
            {
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

    private void AddRecentPoint(Point point, DateTime nowUtc)
    {
        _recentPoints.Add((point.X, point.Y, nowUtc));
        PruneRecent(nowUtc);
    }

    private void PruneRecent(DateTime nowUtc)
    {
        if (_recentPoints.Count == 0) return;
        var cutoff = nowUtc.AddSeconds(-CenterWindowSeconds);
        int idx = 0;
        while (idx < _recentPoints.Count && _recentPoints[idx].Timestamp < cutoff)
        {
            idx++;
        }
        if (idx > 0)
        {
            _recentPoints.RemoveRange(0, idx);
        }
    }

    private (double X, double Y)? GetRecentCenter()
    {
        if (_recentPoints.Count == 0) return null;
        double sumX = 0;
        double sumY = 0;
        foreach (var sample in _recentPoints)
        {
            sumX += sample.X;
            sumY += sample.Y;
        }
        double count = _recentPoints.Count;
        return (sumX / count, sumY / count);
    }
}
