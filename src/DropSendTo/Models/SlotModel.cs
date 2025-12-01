using System.Text.Json.Serialization;
using DropSendTo.Serialization;

namespace DropSendTo.Models;

public class SlotModel
{
    public string? Title { get; set; }
    public string? Command { get; set; }
    public string? ArgumentsTemplate { get; set; } = "{args}"; // placeholder will be replaced by joined paths
    public string? IconPath { get; set; }
    public bool ClickEnabled { get; set; } = true;
    public string? ShortcutKey { get; set; } = string.Empty;
    [JsonConverter(typeof(KeyboardMacroScriptJsonConverter))]
    public string? KeyboardMacroScript { get; set; } = string.Empty;
    public SlotExecutionMode ExecutionMode { get; set; } = SlotExecutionMode.Command;
    public SlotAccentColor AccentColor { get; set; } = SlotAccentColor.Default;
    public SlotMinimizeOptions MinimizeOptions { get; set; } = SlotMinimizeOptions.CreateDefault();
}

public enum SlotExecutionMode
{
    Command = 0,
    MacroScript = 1,
    MacroScriptExtended = 2
}

public enum SlotAccentColor
{
    Default = 0,
    Teal = 1,
    Indigo = 2,
    Amber = 3,
    Olive = 4,
    Crimson = 5
}
