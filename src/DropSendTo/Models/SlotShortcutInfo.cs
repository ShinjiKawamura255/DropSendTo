using System;

namespace DropSendTo.Models;

internal sealed class SlotShortcutInfo
{
    public SlotShortcutInfo(string slotId, string title, string shortcutKey, string normalizedKey, string[] normalizedSegments, bool hasParseError)
    {
        SlotId = slotId;
        Title = title;
        ShortcutKey = shortcutKey;
        NormalizedKey = normalizedKey;
        NormalizedSegments = normalizedSegments ?? Array.Empty<string>();
        HasParseError = hasParseError;
    }

    public string SlotId { get; }
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
