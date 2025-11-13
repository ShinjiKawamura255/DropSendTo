using System;
using System.Collections.Generic;

namespace DropSendTo.Models;

public class AppConfig
{
    public int Version { get; set; } = 15;
    public int CurrentLayer { get; set; } = 0; // 0..3
    public double? WindowLeft { get; set; } = 0;
    public double? WindowTop { get; set; } = 0;
    public bool AlwaysOnTop { get; set; } = true;
    public StartupWindowBehavior StartupBehavior { get; set; } = StartupWindowBehavior.AlwaysShow;
    public WindowVisibilityState LastWindowVisibility { get; set; } = WindowVisibilityState.Visible;
    public WindowPlacementMode WindowPlacementMode { get; set; } = WindowPlacementMode.Fixed;
    public string ShortcutPrefix { get; set; } = "CTRL+Q";
    public bool ShortcutPrefixDisabled { get; set; }
    public MacroConcurrencyMode MacroConcurrencyMode { get; set; } = MacroConcurrencyMode.Exclusive;
    public int SlotRows { get; set; } = 2;
    public int SlotColumns { get; set; } = 2;
    public SlotSize SlotSize { get; set; } = SlotSize.Medium;
    public List<Layer> Layers { get; set; } = new()
    {
        new Layer(), new Layer(), new Layer(), new Layer()
    };
}

public class Layer
{
    public List<SlotModel> Slots { get; set; } = new()
    {
        new SlotModel(), new SlotModel(), new SlotModel(), new SlotModel()
    };
}

public enum StartupWindowBehavior
{
    AlwaysShow = 0,
    RestoreLastState = 1
}

public enum WindowVisibilityState
{
    Visible = 0,
    Tray = 1
}

public enum WindowPlacementMode
{
    Fixed = 0,
    MouseFollow = 1
}

public enum MacroConcurrencyMode
{
    Exclusive = 0,
    Interrupt = 1,
    SuspendAndResume = 2
}
