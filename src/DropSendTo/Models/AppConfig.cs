using System;
using System.Collections.Generic;

namespace DropSendTo.Models;

public class AppConfig
{
    public int Version { get; set; } = 1;
    public int CurrentLayer { get; set; } = 0; // 0..3
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

