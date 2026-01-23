using System;
using System.Collections.Generic;

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
    public bool HasAppShortcutConflict { get; set; }
    public string AppShortcutConflictDetail { get; set; } = string.Empty;
    public bool IsShadowed { get; set; }
    public string Status => HasParseError
        ? "解析エラー"
        : BuildStatus();

    private string BuildStatus()
    {
        var parts = new List<string>();
        if (HasAppShortcutConflict)
        {
            var detail = string.IsNullOrWhiteSpace(AppShortcutConflictDetail)
                ? "アプリ優先"
                : $"アプリ優先: {AppShortcutConflictDetail}";
            parts.Add(detail);
        }
        if (HasConflict)
        {
            parts.Add("競合");
        }
        if (IsShadowed)
        {
            parts.Add("被覆");
        }
        return parts.Count == 0 ? string.Empty : string.Join(" / ", parts);
    }
}
