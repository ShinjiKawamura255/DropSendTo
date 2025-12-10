using DropSendTo.Models;

namespace DropSendTo.Services;

internal static class CustomSlotSizeNormalizer
{
    internal const double MinSlotHeight = 24;
    internal const double MaxSlotHeight = 120;
    internal const double MinFont = 8;
    internal const double MaxTitleFont = 18;
    internal const double MaxStatusFont = 16;
    internal const double MinRowStep = 24;
    internal const double MaxRowStep = 120;
    internal const double MinColumnStep = 50;
    internal const double MaxColumnStep = 120;
    internal const double MinMargin = 0;
    internal const double MaxMargin = 12;

    internal static CustomSlotSizeOptions Normalize(CustomSlotSizeOptions options)
    {
        options ??= CustomSlotSizeOptions.CreateDefault();
        options.SlotMargin = Clamp(options.SlotMargin, MinMargin, MaxMargin);
        options.TitleFontSize = Clamp(options.TitleFontSize, MinFont, MaxTitleFont);
        options.StatusFontSize = Clamp(options.StatusFontSize, MinFont, MaxStatusFont);
        double minHeight = System.Math.Max(MinSlotHeight, options.TitleFontSize + options.StatusFontSize + (options.SlotMargin * 2) + 4);
        options.SlotHeight = Clamp(options.SlotHeight, minHeight, MaxSlotHeight);
        options.RowStep = Clamp(options.RowStep, System.Math.Max(options.SlotHeight + options.SlotMargin * 2, MinRowStep), MaxRowStep);
        options.ColumnStep = Clamp(options.ColumnStep, MinColumnStep, MaxColumnStep);
        return options;
    }

    private static double Clamp(double value, double min, double max) => System.Math.Min(System.Math.Max(value, min), max);
}
