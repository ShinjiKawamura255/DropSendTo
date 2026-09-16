using System;
using System.Collections.Generic;
using System.Globalization;

namespace DropSendTo.Services;

internal sealed record LayerButtonModel(
    string Content,
    object Tag,
    bool IsLayer,
    int LayerIndex,
    string? ToolTip,
    bool Visible)
{
    public static LayerButtonModel Layer(int layerIndex) =>
        new(
            (layerIndex + 1).ToString(CultureInfo.InvariantCulture),
            layerIndex,
            true,
            layerIndex,
            $"Layer {layerIndex + 1}",
            true);

    public static LayerButtonModel Arrow(string content, string tag, string toolTip) =>
        new(content, tag, false, -1, toolTip, true);

    public static LayerButtonModel Hidden { get; } =
        new(string.Empty, string.Empty, false, -1, null, false);
}

internal static class LayerButtonModelFactory
{
    public static IReadOnlyList<LayerButtonModel> Create(
        int totalLayers,
        int currentLayer,
        int buttonCount,
        int navigationDirection)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(buttonCount);

        var models = new List<LayerButtonModel>(buttonCount);
        if (totalLayers <= 0)
        {
            AddHiddenModels(models, buttonCount);
            return models;
        }

        currentLayer = Math.Clamp(currentLayer, 0, totalLayers - 1);
        if (totalLayers <= buttonCount)
        {
            for (int i = 0; i < buttonCount; i++)
            {
                models.Add(i < totalLayers ? LayerButtonModel.Layer(i) : LayerButtonModel.Hidden);
            }
            return models;
        }

        int numericCount = Math.Min(3, buttonCount);
        int start = Math.Clamp(currentLayer - 1, 0, Math.Max(0, totalLayers - numericCount));
        int end = start + numericCount - 1;
        bool leftHidden = start > 0;
        bool rightHidden = end < totalLayers - 1;

        if (leftHidden && rightHidden)
        {
            numericCount = 2;
            start = navigationDirection switch
            {
                > 0 => Math.Clamp(currentLayer - 1, 1, totalLayers - numericCount - 1),
                < 0 => Math.Clamp(currentLayer, 1, totalLayers - numericCount - 1),
                _ => Math.Clamp(currentLayer, 1, totalLayers - numericCount - 1)
            };
            end = start + numericCount - 1;
            leftHidden = start > 0;
            rightHidden = end < totalLayers - 1;
        }

        if (leftHidden && models.Count < buttonCount)
        {
            models.Add(LayerButtonModel.Arrow("◀", "prev", "前のレイヤーへ"));
        }

        for (int i = 0; i < numericCount && models.Count < buttonCount; i++)
        {
            int layerIndex = start + i;
            if (layerIndex >= totalLayers)
            {
                break;
            }
            models.Add(LayerButtonModel.Layer(layerIndex));
        }

        if (rightHidden && models.Count < buttonCount)
        {
            models.Add(LayerButtonModel.Arrow("▶", "next", "次のレイヤーへ"));
        }

        AddHiddenModels(models, buttonCount);
        return models;
    }

    private static void AddHiddenModels(List<LayerButtonModel> models, int buttonCount)
    {
        while (models.Count < buttonCount)
        {
            models.Add(LayerButtonModel.Hidden);
        }
    }
}
