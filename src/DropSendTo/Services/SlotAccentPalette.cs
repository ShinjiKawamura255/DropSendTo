using System.Collections.Generic;
using DropSendTo.Models;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace DropSendTo.Services;

internal readonly record struct SlotAccentPaletteEntry(MediaColor Background, MediaColor Border, MediaColor Foreground);

internal static class SlotAccentPalette
{
    private static readonly IReadOnlyDictionary<SlotAccentColor, SlotAccentPaletteEntry> DarkSchemes =
        new Dictionary<SlotAccentColor, SlotAccentPaletteEntry>
        {
            [SlotAccentColor.Default] = new(MediaColor.FromRgb(0x11, 0x11, 0x11), MediaColor.FromRgb(0x33, 0x33, 0x33), MediaColors.White),
            [SlotAccentColor.Teal] = new(MediaColor.FromRgb(0x10, 0x2A, 0x30), MediaColor.FromRgb(0x1F, 0x76, 0x7D), MediaColor.FromRgb(0xE4, 0xFD, 0xFF)),
            [SlotAccentColor.Indigo] = new(MediaColor.FromRgb(0x16, 0x15, 0x2E), MediaColor.FromRgb(0x4E, 0x52, 0xA6), MediaColor.FromRgb(0xF4, 0xF2, 0xFF)),
            [SlotAccentColor.Azure] = new(MediaColor.FromRgb(0x0F, 0x1B, 0x33), MediaColor.FromRgb(0x2B, 0x78, 0xC4), MediaColor.FromRgb(0xE2, 0xF1, 0xFF)),
            [SlotAccentColor.Amber] = new(MediaColor.FromRgb(0x2D, 0x1F, 0x0F), MediaColor.FromRgb(0xB5, 0x6B, 0x17), MediaColor.FromRgb(0xFF, 0xE8, 0xC2)),
            [SlotAccentColor.Olive] = new(MediaColor.FromRgb(0x20, 0x27, 0x12), MediaColor.FromRgb(0x6E, 0x8C, 0x23), MediaColor.FromRgb(0xF0, 0xFF, 0xD8)),
            [SlotAccentColor.Emerald] = new(MediaColor.FromRgb(0x0F, 0x28, 0x1D), MediaColor.FromRgb(0x1E, 0x8B, 0x5B), MediaColor.FromRgb(0xE3, 0xFF, 0xF2)),
            [SlotAccentColor.Crimson] = new(MediaColor.FromRgb(0x2B, 0x11, 0x16), MediaColor.FromRgb(0xB5, 0x45, 0x4F), MediaColor.FromRgb(0xFF, 0xE3, 0xE7)),
            [SlotAccentColor.Magenta] = new(MediaColor.FromRgb(0x2A, 0x0F, 0x2B), MediaColor.FromRgb(0x9B, 0x3E, 0xA8), MediaColor.FromRgb(0xFF, 0xE6, 0xFF))
        };

    private static readonly IReadOnlyDictionary<SlotAccentColor, SlotAccentPaletteEntry> LightSchemes =
        new Dictionary<SlotAccentColor, SlotAccentPaletteEntry>
        {
            [SlotAccentColor.Default] = new(MediaColor.FromRgb(0xF5, 0xF5, 0xF5), MediaColor.FromRgb(0xCC, 0xCC, 0xCC), MediaColor.FromRgb(0x11, 0x11, 0x11)),
            [SlotAccentColor.Teal] = new(MediaColor.FromRgb(0xE3, 0xF4, 0xF6), MediaColor.FromRgb(0x74, 0xB9, 0xC0), MediaColor.FromRgb(0x1F, 0x5A, 0x62)),
            [SlotAccentColor.Indigo] = new(MediaColor.FromRgb(0xEC, 0xEB, 0xFA), MediaColor.FromRgb(0x8E, 0x87, 0xD6), MediaColor.FromRgb(0x2F, 0x2F, 0x6A)),
            [SlotAccentColor.Azure] = new(MediaColor.FromRgb(0xE6, 0xF0, 0xFB), MediaColor.FromRgb(0x7A, 0xAD, 0xE3), MediaColor.FromRgb(0x1F, 0x3E, 0x69)),
            [SlotAccentColor.Amber] = new(MediaColor.FromRgb(0xFF, 0xF3, 0xDD), MediaColor.FromRgb(0xE1, 0xA4, 0x4B), MediaColor.FromRgb(0x6B, 0x4A, 0x16)),
            [SlotAccentColor.Olive] = new(MediaColor.FromRgb(0xEE, 0xF5, 0xDA), MediaColor.FromRgb(0xA2, 0xB8, 0x5F), MediaColor.FromRgb(0x45, 0x55, 0x1F)),
            [SlotAccentColor.Emerald] = new(MediaColor.FromRgb(0xE5, 0xF6, 0xEE), MediaColor.FromRgb(0x6F, 0xB9, 0x8A), MediaColor.FromRgb(0x1F, 0x5A, 0x3D)),
            [SlotAccentColor.Crimson] = new(MediaColor.FromRgb(0xFB, 0xE7, 0xEA), MediaColor.FromRgb(0xE1, 0x89, 0x95), MediaColor.FromRgb(0x6A, 0x1E, 0x2A)),
            [SlotAccentColor.Magenta] = new(MediaColor.FromRgb(0xF7, 0xE8, 0xF8), MediaColor.FromRgb(0xD0, 0x8F, 0xD8), MediaColor.FromRgb(0x5A, 0x1F, 0x60))
        };

    public static SlotAccentPaletteEntry GetScheme(SlotAccentColor accent, AppTheme theme)
    {
        var schemes = theme == AppTheme.Light ? LightSchemes : DarkSchemes;
        return schemes.TryGetValue(accent, out var scheme) ? scheme : schemes[SlotAccentColor.Default];
    }
}
