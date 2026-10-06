namespace DropSendTo.Models;

/// <summary>Maps an application and optional window title/path to an existing layer.</summary>
public sealed class AppLayerRule
{
    public bool Enabled { get; set; } = true;
    public string ExecutableName { get; set; } = string.Empty;
    public string TitleContains { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public int LayerIndex { get; set; }

    public AppLayerRule Clone() => (AppLayerRule)MemberwiseClone();
}
