namespace DropSendTo.Models;

public class SlotModel
{
    public string? Title { get; set; }
    public string? Command { get; set; }
    public string? ArgumentsTemplate { get; set; } = "{args}"; // placeholder will be replaced by joined paths
    public string? IconPath { get; set; }
    public bool ClickEnabled { get; set; } = true;
}
