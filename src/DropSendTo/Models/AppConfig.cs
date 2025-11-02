using System;
using System.Collections.Generic;

namespace DropSendTo.Models;

public class AppConfig
{
    public int Version { get; set; } = 10;
    public int CurrentLayer { get; set; } = 0; // 0..3
    public double? WindowLeft { get; set; } = 0;
    public double? WindowTop { get; set; } = 0;
    public bool AlwaysOnTop { get; set; } = true;
    public string ShortcutPrefix { get; set; } = "CTRL+Q";
    public bool ShortcutPrefixDisabled { get; set; }
    public int SlotRows { get; set; } = 2;
    public int SlotColumns { get; set; } = 2;
    public SlotSize SlotSize { get; set; } = SlotSize.Large;
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
