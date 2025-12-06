using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using DropSendTo.Models;

namespace DropSendTo;

public partial class SlotMinimizeSettingsWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public List<SlotMinimizeRow> Rows { get; }

    public bool DefaultOnClick
    {
        get => _defaultOnClick;
        set
        {
            if (_defaultOnClick == value) return;
            _defaultOnClick = value;
            OnPropertyChanged(nameof(DefaultOnClick));
        }
    }

    public bool DefaultOnShortcut
    {
        get => _defaultOnShortcut;
        set
        {
            if (_defaultOnShortcut == value) return;
            _defaultOnShortcut = value;
            OnPropertyChanged(nameof(DefaultOnShortcut));
        }
    }

    public bool DefaultOnDrop
    {
        get => _defaultOnDrop;
        set
        {
            if (_defaultOnDrop == value) return;
            _defaultOnDrop = value;
            OnPropertyChanged(nameof(DefaultOnDrop));
        }
    }

    public bool DefaultOnKeyboard
    {
        get => _defaultOnKeyboard;
        set
        {
            if (_defaultOnKeyboard == value) return;
            _defaultOnKeyboard = value;
            OnPropertyChanged(nameof(DefaultOnKeyboard));
        }
    }

    private bool _defaultOnClick;
    private bool _defaultOnShortcut;
    private bool _defaultOnDrop;
    private bool _defaultOnKeyboard;

    public SlotMinimizeSettingsWindow(IReadOnlyList<Layer> layers, SlotMinimizeOptions defaultOptions)
    {
        InitializeComponent();
        DataContext = this;
        Rows = BuildRows(layers);
        ApplyDefaultOptions(defaultOptions ?? SlotMinimizeOptions.CreateDefault());
    }

    private static List<SlotMinimizeRow> BuildRows(IReadOnlyList<Layer> layers)
    {
        var rows = new List<SlotMinimizeRow>();
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
                var options = slot.MinimizeOptions ?? SlotMinimizeOptions.CreateDefault();
                string title = string.IsNullOrWhiteSpace(slot.Title) ? $"Slot {slotIndex + 1}" : slot.Title.Trim();
                rows.Add(new SlotMinimizeRow(layerIndex, slotIndex, $"L{layerIndex + 1}", $"S{slotIndex + 1}", title)
                {
                    EnableOnClick = options.EnableOnClick,
                    EnableOnShortcut = options.EnableOnShortcut,
                    EnableOnDrop = options.EnableOnDrop,
                    EnableOnKeyboard = options.EnableOnKeyboard
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

    private void ApplyDefaultOptions(SlotMinimizeOptions options)
    {
        DefaultOnClick = options.EnableOnClick;
        DefaultOnShortcut = options.EnableOnShortcut;
        DefaultOnDrop = options.EnableOnDrop;
        DefaultOnKeyboard = options.EnableOnKeyboard;
    }

    public SlotMinimizeOptions GetDefaultOptions() => new()
    {
        EnableOnClick = DefaultOnClick,
        EnableOnShortcut = DefaultOnShortcut,
        EnableOnDrop = DefaultOnDrop,
        EnableOnKeyboard = DefaultOnKeyboard
    };

    public IEnumerable<SlotUpdate> GetSlotUpdates() =>
        Rows.Select(r => new SlotUpdate(r.LayerIndex, r.SlotIndex, r.ToOptions()));

    private void OnSave(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class SlotMinimizeRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int LayerIndex { get; }
    public int SlotIndex { get; }
    public string LayerLabel { get; }
    public string SlotLabel { get; }

    private bool _enableOnClick;
    private bool _enableOnShortcut;
    private bool _enableOnDrop;
    private bool _enableOnKeyboard;

    public string Title { get; }

    public bool EnableOnClick
    {
        get => _enableOnClick;
        set
        {
            if (_enableOnClick == value) return;
            _enableOnClick = value;
            OnPropertyChanged(nameof(EnableOnClick));
        }
    }

    public bool EnableOnShortcut
    {
        get => _enableOnShortcut;
        set
        {
            if (_enableOnShortcut == value) return;
            _enableOnShortcut = value;
            OnPropertyChanged(nameof(EnableOnShortcut));
        }
    }

    public bool EnableOnDrop
    {
        get => _enableOnDrop;
        set
        {
            if (_enableOnDrop == value) return;
            _enableOnDrop = value;
            OnPropertyChanged(nameof(EnableOnDrop));
        }
    }

    public bool EnableOnKeyboard
    {
        get => _enableOnKeyboard;
        set
        {
            if (_enableOnKeyboard == value) return;
            _enableOnKeyboard = value;
            OnPropertyChanged(nameof(EnableOnKeyboard));
        }
    }

    public SlotMinimizeRow(int layerIndex, int slotIndex, string layerLabel, string slotLabel, string title)
    {
        LayerIndex = layerIndex;
        SlotIndex = slotIndex;
        LayerLabel = layerLabel;
        SlotLabel = slotLabel;
        Title = title;
    }

    public SlotMinimizeOptions ToOptions() => new()
    {
        EnableOnClick = EnableOnClick,
        EnableOnShortcut = EnableOnShortcut,
        EnableOnDrop = EnableOnDrop,
        EnableOnKeyboard = EnableOnKeyboard
    };

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public readonly record struct SlotUpdate(int LayerIndex, int SlotIndex, SlotMinimizeOptions Options);
