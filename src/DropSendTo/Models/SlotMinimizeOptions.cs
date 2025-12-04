namespace DropSendTo.Models;

public enum SlotTriggerKind
{
    Click = 0,
    Shortcut = 1,
    Drop = 2,
    Keyboard = 3
}

public class SlotMinimizeOptions
{
    public bool EnableOnClick { get; set; }
    public bool EnableOnShortcut { get; set; }
    public bool EnableOnDrop { get; set; }
    public bool EnableOnKeyboard { get; set; }

    public static SlotMinimizeOptions CreateDefault() => new()
    {
        EnableOnClick = false,
        EnableOnShortcut = false,
        EnableOnDrop = false,
        EnableOnKeyboard = false
    };

    public bool ShouldMinimizeAfter(SlotTriggerKind trigger) =>
        trigger switch
        {
            SlotTriggerKind.Click => EnableOnClick,
            SlotTriggerKind.Shortcut => EnableOnShortcut,
            SlotTriggerKind.Drop => EnableOnDrop,
            SlotTriggerKind.Keyboard => EnableOnKeyboard,
            _ => false
        };
}
