using System;

namespace DropSendTo.Models;

internal sealed class SlotShortcutInfo
{
    public SlotShortcutInfo(
        string slotId,
        string title,
        string shortcutKey,
        string normalizedKey,
        string[] normalizedSegments,
        bool hasParseError,
        int layerIndex,
        int slotIndex)
    {
        SlotId = slotId;
        Title = title;
        ShortcutKey = shortcutKey;
        NormalizedKey = normalizedKey;
        NormalizedSegments = normalizedSegments ?? Array.Empty<string>();
        HasParseError = hasParseError;
        LayerIndex = layerIndex;
        SlotIndex = slotIndex;
    }

    public string SlotId { get; }
    public int LayerIndex { get; }
    public int SlotIndex { get; }
    public long SlotSortKey => ((long)LayerIndex << 32) | (uint)SlotIndex;
    public string Title { get; }
    public string ShortcutKey { get; }
    public string NormalizedKey { get; }
    public string[] NormalizedSegments { get; }
    public bool HasParseError { get; }
    public bool HasConflict { get; set; }
    public bool IsShadowed { get; set; }
    public string Status => HasParseError
        ? "解析エラー"
        : HasConflict ? "競合"
        : IsShadowed ? "被覆" : string.Empty;
}
