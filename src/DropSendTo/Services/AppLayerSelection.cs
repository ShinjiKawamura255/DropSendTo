using System;
using DropSendTo.Models;

namespace DropSendTo.Services;

internal sealed record ActiveWindowInfo(IntPtr Handle, string ExecutableName, string Title, string ExecutablePath)
{
    public override string ToString() => $"{ExecutableName} — {Title}";
}

internal static class AppLayerRuleMatcher
{
    public static int? FindMatch(AppConfig config, ActiveWindowInfo? window)
    {
        if (window == null || string.IsNullOrEmpty(window.ExecutableName)) return null;
        for (int i = 0; i < config.AppLayerRules.Count; i++)
        {
            var rule = config.AppLayerRules[i];
            if (!rule.Enabled || rule.LayerIndex < 0 || rule.LayerIndex >= config.Layers.Count) continue;
            if (!string.Equals(rule.ExecutableName, window.ExecutableName, StringComparison.OrdinalIgnoreCase)) continue;
            if (rule.TitleContains.Length > 0 &&
                !window.Title.Contains(rule.TitleContains, StringComparison.OrdinalIgnoreCase)) continue;
            if (rule.ExecutablePath.Length > 0 &&
                !string.Equals(rule.ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase)) continue;
            return i;
        }
        return null;
    }
}

/// <summary>Dispatcher-owned selection state; automatic changes never overwrite the manual fallback.</summary>
internal sealed class AppLayerSelection
{
    private bool _pending;
    public int ManualLayer { get; private set; }
    public bool IsPinned { get; private set; }
    public bool HasPendingEvaluation => _pending;
    public ActiveWindowInfo? LastExternalWindow { get; private set; }

    public AppLayerSelection(int manualLayer) => ManualLayer = manualLayer;

    public void Observe(ActiveWindowInfo? externalWindow, bool isOwnForeground)
    {
        LastExternalWindow = externalWindow;
        if (!isOwnForeground) _pending = true;
    }

    public void SelectManually(int layer)
    {
        ManualLayer = layer;
        _pending = false;
    }

    public void SetPinned(bool pinned)
    {
        IsPinned = pinned;
        if (!pinned) RequestEvaluation();
    }

    public void RequestEvaluation() => _pending = true;

    public int? TakePendingLayer(AppConfig config, bool blocked)
    {
        if (!_pending || blocked || IsPinned || !config.AppLayerSwitchingEnabled) return null;
        _pending = false;
        // Unknown/inaccessible foreground windows leave the display unchanged.
        if (LastExternalWindow == null) return null;
        var ruleIndex = AppLayerRuleMatcher.FindMatch(config, LastExternalWindow);
        int layer = ruleIndex.HasValue ? config.AppLayerRules[ruleIndex.Value].LayerIndex :
            config.AppLayerFallbackLayer >= 0 ? config.AppLayerFallbackLayer : ManualLayer;
        return Math.Clamp(layer, 0, config.Layers.Count - 1);
    }

    public static bool ShouldSwitchForShortcut(AppConfig config) => !config.AppLayerSwitchingEnabled;
}
