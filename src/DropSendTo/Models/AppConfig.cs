using System;
using System.Collections.Generic;

namespace DropSendTo.Models;

public class AppConfig
{
    public int Version { get; set; } = 34;
    public int CurrentLayer { get; set; } = 0; // 0-based
    public double? WindowLeft { get; set; } = 0;
    public double? WindowTop { get; set; } = 0;
    public bool AlwaysOnTop { get; set; } = true;
    public StartupWindowBehavior StartupBehavior { get; set; } = StartupWindowBehavior.AlwaysShow;
    public WindowVisibilityState LastWindowVisibility { get; set; } = WindowVisibilityState.Visible;
    public WindowPlacementMode WindowPlacementMode { get; set; } = WindowPlacementMode.Fixed;
    public WindowPlacementMode KeyboardPlacementMode { get; set; } = WindowPlacementMode.Fixed;
    public WindowPlacementMode MousePlacementMode { get; set; } = WindowPlacementMode.Fixed;
    public bool MousePlacementFollowsKeyboard { get; set; } = true;
    public string ShortcutPrefix { get; set; } = "CTRL+Q";
    public bool ShortcutPrefixDisabled { get; set; }
    public bool EnablePrefixLayerShortcuts { get; set; }
    public bool EnableEmacsNavigation { get; set; }
    public bool EnableViNavigation { get; set; }
    public bool HideEmptySlotNames { get; set; }
    public MacroConcurrencyMode MacroConcurrencyMode { get; set; } = MacroConcurrencyMode.Exclusive;
    public bool EnableMouseGestures { get; set; } = true;
    public int MouseGestureClockwiseTurnsToShow { get; set; } = 3;
    public int MouseGestureCounterClockwiseTurnsToHide { get; set; } = 2;
    public bool MouseGestureInvertDirections { get; set; }
    public bool MouseGestureRequireCtrl { get; set; }
    public bool MouseGestureSuppressDuringPresentation { get; set; }
    public bool MouseGestureEnforceRadiusLimit { get; set; } = true;
    public int MouseGestureMinRadiusPixels { get; set; } = 0;
    public int MouseGestureMaxRadiusPixels { get; set; } = 140;
    public int MouseGestureShowLayerWhenVisible { get; set; } = -1;
    public int MouseGestureShowLayerWhenHidden { get; set; } = -1;
    public int PrefixShowLayerWhenVisible { get; set; } = -1;
    public int PrefixShowLayerWhenHidden { get; set; } = -1;
    public SlotMinimizeOptions DefaultMinimizeOptions { get; set; } = SlotMinimizeOptions.CreateDefault();
    public bool SearchHotkeyEnabled { get; set; }
    public string SearchHotkey { get; set; } = string.Empty;
    public int SlotRows { get; set; } = 2;
    public int SlotColumns { get; set; } = 2;
    public SlotSize SlotSize { get; set; } = SlotSize.Medium;
    public CustomSlotSizeOptions CustomSlotSize { get; set; } = CustomSlotSizeOptions.CreateDefault();
    public bool PreferRemoteSessions { get; set; } = true;
    public SearchOverlayPlacementMode SearchPlacementMode { get; set; } = SearchOverlayPlacementMode.Fixed;
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

public enum SearchOverlayPlacementMode
{
    Fixed = 0,
    MouseFollow = 1,
    CursorScreenCenter = 2
}

public sealed class CustomSlotSizeOptions
{
    public double SlotHeight { get; set; }
    public double TitleFontSize { get; set; }
    public double StatusFontSize { get; set; }
    public double RowStep { get; set; }
    public double ColumnStep { get; set; }
    public double SlotMargin { get; set; }

    public static CustomSlotSizeOptions CreateDefault() => new()
    {
        SlotHeight = 32,
        TitleFontSize = 10,
        StatusFontSize = 9,
        RowStep = 36,
        ColumnStep = 70,
        SlotMargin = 2
    };

    public CustomSlotSizeOptions Clone() => new()
    {
        SlotHeight = SlotHeight,
        TitleFontSize = TitleFontSize,
        StatusFontSize = StatusFontSize,
        RowStep = RowStep,
        ColumnStep = ColumnStep,
        SlotMargin = SlotMargin
    };
}

public enum MacroConcurrencyMode
{
    Exclusive = 0,
    Interrupt = 1,
    SuspendAndResume = 2
}
