using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DropSendTo.Models;

namespace DropSendTo;

public partial class SlotStartupSettingsWindow : Window
{
    public event EventHandler<SlotStartupSaveEventArgs>? SaveRequested;

    public ObservableCollection<SlotStartupRow> Rows { get; } = new();

    public SlotStartupSettingsWindow(IReadOnlyList<Layer> layers)
    {
        InitializeComponent();
        DataContext = this;
        SetEntries(layers);
        Loaded += (_, _) => ClampToWorkAreaHeight();
    }

    public void SetEntries(IReadOnlyList<Layer> layers)
    {
        Rows.Clear();
        foreach (var row in BuildRows(layers))
        {
            Rows.Add(row);
        }
    }

    public IEnumerable<SlotStartupUpdate> GetSlotUpdates() =>
        Rows.Select(r => new SlotStartupUpdate(r.LayerIndex, r.SlotIndex, r.RunOnStartup));

    private static List<SlotStartupRow> BuildRows(IReadOnlyList<Layer> layers)
    {
        var rows = new List<SlotStartupRow>();
        if (layers == null)
        {
            return rows;
        }

        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            var slots = layer?.Slots ?? new List<SlotModel>();
            for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                var slot = slots[slotIndex] ?? new SlotModel();
                if (IsSlotEmpty(slot))
                {
                    continue;
                }
                string title = string.IsNullOrWhiteSpace(slot.Title) ? $"Slot {slotIndex + 1}" : slot.Title.Trim();
                rows.Add(new SlotStartupRow(layerIndex, slotIndex, $"L{layerIndex + 1}", $"S{slotIndex + 1}", title)
                {
                    RunOnStartup = slot.RunOnStartup
                });
            }
        }

        return rows;
    }

    private static bool IsSlotEmpty(SlotModel slot)
    {
        if (slot == null) return true;
        bool baseTemplate = string.Equals(slot.ArgumentsTemplate ?? string.Empty, "{args}", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(slot.Title) &&
               string.IsNullOrWhiteSpace(slot.Command) &&
               string.IsNullOrWhiteSpace(slot.KeyboardMacroScript) &&
               string.IsNullOrWhiteSpace(slot.ShortcutKey) &&
               baseTemplate &&
               slot.ClickEnabled &&
               string.IsNullOrWhiteSpace(slot.IconPath);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(this, new SlotStartupSaveEventArgs(GetSlotUpdates().ToList()));
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveElement(e.OriginalSource)) return;
        DragMove();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ClampToWorkAreaHeight()
    {
        var workAreaHeight = SystemParameters.WorkArea.Height;
        if (workAreaHeight <= 0)
        {
            return;
        }
        MaxHeight = workAreaHeight;
        if (ActualHeight > MaxHeight)
        {
            Height = MaxHeight;
        }
    }

    private static bool IsInteractiveElement(object source)
    {
        if (source is not DependencyObject d) return false;
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase
                || d is System.Windows.Controls.Primitives.TextBoxBase
                || d is System.Windows.Controls.PasswordBox)
            {
                return true;
            }
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }
}

public sealed class SlotStartupRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int LayerIndex { get; }
    public int SlotIndex { get; }
    public string LayerLabel { get; }
    public string SlotLabel { get; }
    public string Title { get; }

    private bool _runOnStartup;

    public bool RunOnStartup
    {
        get => _runOnStartup;
        set
        {
            if (_runOnStartup == value) return;
            _runOnStartup = value;
            OnPropertyChanged(nameof(RunOnStartup));
        }
    }

    public SlotStartupRow(int layerIndex, int slotIndex, string layerLabel, string slotLabel, string title)
    {
        LayerIndex = layerIndex;
        SlotIndex = slotIndex;
        LayerLabel = layerLabel;
        SlotLabel = slotLabel;
        Title = title;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public readonly record struct SlotStartupUpdate(int LayerIndex, int SlotIndex, bool RunOnStartup);

public sealed class SlotStartupSaveEventArgs : EventArgs
{
    public SlotStartupSaveEventArgs(IReadOnlyList<SlotStartupUpdate> updates)
    {
        Updates = updates;
    }

    public IReadOnlyList<SlotStartupUpdate> Updates { get; }
}
