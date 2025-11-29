using System;
using System.Collections.Generic;

namespace DropSendTo.Models;

public class AppConfig
{
    public int Version { get; set; } = 23;
    public int CurrentLayer { get; set; } = 0; // 0..3
    public double? WindowLeft { get; set; } = 0;
    public double? WindowTop { get; set; } = 0;
    public bool AlwaysOnTop { get; set; } = true;
    public StartupWindowBehavior StartupBehavior { get; set; } = StartupWindowBehavior.AlwaysShow;
    public WindowVisibilityState LastWindowVisibility { get; set; } = WindowVisibilityState.Visible;
    public WindowPlacementMode WindowPlacementMode { get; set; } = WindowPlacementMode.Fixed;
    public string ShortcutPrefix { get; set; } = "CTRL+Q";
    public bool ShortcutPrefixDisabled { get; set; }
    public bool EnablePrefixLayerShortcuts { get; set; }
    public bool EnableEmacsNavigation { get; set; } = true;
    public bool EnableViNavigation { get; set; } = true;
    public MacroConcurrencyMode MacroConcurrencyMode { get; set; } = MacroConcurrencyMode.Exclusive;
    public bool EnableMouseGestures { get; set; } = true;
    public int MouseGestureClockwiseTurnsToShow { get; set; } = 3;
    public int MouseGestureCounterClockwiseTurnsToHide { get; set; } = 2;
    public bool MouseGestureInvertDirections { get; set; }
    public bool MouseGestureRequireCtrl { get; set; }
    public bool MouseGestureSuppressDuringPresentation { get; set; }
    public bool MouseGestureEnforceRadiusLimit { get; set; } = true;
    public int MouseGestureMinRadiusPixels { get; set; } = 40;
    public int MouseGestureMaxRadiusPixels { get; set; } = 140;
    public int SlotRows { get; set; } = 2;
    public int SlotColumns { get; set; } = 2;
    public SlotSize SlotSize { get; set; } = SlotSize.Medium;
    public bool PreferRemoteSessions { get; set; } = true;
    public List<Layer> Layers { get; set; } = new()
    {
        new Layer(), new Layer(), new Layer(), new Layer()
    };
}

public class Layer
{
    public string Name { get; set; } = string.Empty;
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
