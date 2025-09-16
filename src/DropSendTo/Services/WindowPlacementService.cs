namespace DropSendTo.Services;

public readonly record struct ScreenBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public class WindowPlacementService
{
    public (double left, double top) Clamp(double left, double top, ScreenBounds bounds, double windowWidth, double windowHeight)
    {
        var cl = left;
        var ct = top;
        if (double.IsNaN(cl) || double.IsInfinity(cl)) cl = bounds.Left;
        if (double.IsNaN(ct) || double.IsInfinity(ct)) ct = bounds.Top;

        var maxLeft = bounds.Right - windowWidth;
        var maxTop = bounds.Bottom - windowHeight;
        if (cl < bounds.Left) cl = bounds.Left;
        if (ct < bounds.Top) ct = bounds.Top;
        if (cl > maxLeft) cl = maxLeft;
        if (ct > maxTop) ct = maxTop;
        return (cl, ct);
    }
}

