using System;
using System.Globalization;

namespace DropSendTo.Services;

internal readonly record struct MacroDisplayBounds(int Left, int Top, int Width, int Height);

internal interface IMacroDisplayAdapter
{
    bool TryGetBounds(int displayNumber, out MacroDisplayBounds bounds, out string? error);
}

internal readonly record struct MacroDisplayMove(int DisplayNumber, string Position, int OffsetX, int OffsetY)
{
    public static bool TryParse(string args, out MacroDisplayMove move, out string? error)
    {
        move = default;
        error = null;
        var tokens = args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2 && tokens.Length != 5)
        {
            error = "MOUSEMOVEDISPLAY の書式は MOUSEMOVEDISPLAY <番号> <位置> [OFFSET <X> <Y>] です。";
            return false;
        }
        if (!int.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            error = "MOUSEMOVEDISPLAY のディスプレイ番号は 1 以上の整数で指定してください。";
            return false;
        }
        var position = tokens[1].ToUpperInvariant();
        if (position is not ("TOPLEFT" or "TOPCENTER" or "TOPRIGHT" or "LEFTCENTER" or "CENTER" or
            "RIGHTCENTER" or "BOTTOMLEFT" or "BOTTOMCENTER" or "BOTTOMRIGHT"))
        {
            error = $"MOUSEMOVEDISPLAY の位置が不正です: \"{tokens[1]}\"";
            return false;
        }
        int offsetX = 0;
        int offsetY = 0;
        if (tokens.Length == 5 && (!tokens[2].Equals("OFFSET", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out offsetX) ||
            !int.TryParse(tokens[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out offsetY)))
        {
            error = "MOUSEMOVEDISPLAY の OFFSET には 2 個の整数を指定してください。";
            return false;
        }
        move = new MacroDisplayMove(number, position, offsetX, offsetY);
        return true;
    }

    public bool TryResolve(MacroDisplayBounds bounds, out int x, out int y, out string? error)
    {
        x = y = 0;
        error = null;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            error = "ディスプレイの領域が不正です。";
            return false;
        }
        long left = bounds.Left;
        long top = bounds.Top;
        long right = left + bounds.Width - 1L;
        long bottom = top + bounds.Height - 1L;
        long middleX = left + (bounds.Width - 1L) / 2;
        long middleY = top + (bounds.Height - 1L) / 2;
        var point = Position switch
        {
            "TOPLEFT" => (left, top),
            "TOPCENTER" => (middleX, top),
            "TOPRIGHT" => (right, top),
            "LEFTCENTER" => (left, middleY),
            "CENTER" => (middleX, middleY),
            "RIGHTCENTER" => (right, middleY),
            "BOTTOMLEFT" => (left, bottom),
            "BOTTOMCENTER" => (middleX, bottom),
            "BOTTOMRIGHT" => (right, bottom),
            _ => (long.MinValue, long.MinValue)
        };
        if (point.Item1 == long.MinValue)
        {
            error = "MOUSEMOVEDISPLAY の位置が不正です。";
            return false;
        }
        long targetX = point.Item1 + OffsetX;
        long targetY = point.Item2 + OffsetY;
        if (targetX < left || targetX > right || targetY < top || targetY > bottom ||
            targetX < int.MinValue || targetX > int.MaxValue || targetY < int.MinValue || targetY > int.MaxValue)
        {
            error = "MOUSEMOVEDISPLAY の移動先が指定ディスプレイの範囲外です。";
            return false;
        }
        x = (int)targetX;
        y = (int)targetY;
        return true;
    }
}
