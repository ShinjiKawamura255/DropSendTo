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
    int RadiusPixels)
{
    public static MouseGestureOptions Default { get; } =
        new(true, 3, 2, false, false, false, 120);

    public MouseGestureOptions Normalize()
    {
        int Clamp(int value) => Math.Clamp(value, 1, 50);
        int ClampRadius(int value) => Math.Clamp(value, 40, 320);

        return this with
        {
            ClockwiseTurnsToShow = Clamp(ClockwiseTurnsToShow),
            CounterClockwiseTurnsToHide = Clamp(CounterClockwiseTurnsToHide),
            RadiusPixels = ClampRadius(RadiusPixels)
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
    private double _minMovementSquared = CalculateMinMovementSquared(MouseGestureOptions.Default.RadiusPixels);

    private const double FullTurn = Math.PI * 2;
    private const double TurnThreshold = FullTurn * 0.9;
    private const int IdleResetMilliseconds = 330;

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
            _minMovementSquared = CalculateMinMovementSquared(_options.RadiusPixels);
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
                return MouseGestureAction.None;
            }

            var deltaX = point.X - _lastPoint.X;
            var deltaY = point.Y - _lastPoint.Y;
            var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            _lastPoint = point;
            if (distanceSquared < _minMovementSquared)
            {
                return MouseGestureAction.None;
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
    }

    private static double CalculateMinMovementSquared(int radiusPixels)
    {
        // Use a small fraction of the configured radius as the minimum vector length to suppress jitter.
        double minDistance = Math.Max(6, radiusPixels * 0.12);
        return minDistance * minDistance;
    }
}
