using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace DropSendTo;

public sealed record ShowLayerPreferenceOptions(
    int MouseGestureVisibleLayer,
    int MouseGestureHiddenLayer,
    int PrefixVisibleLayer,
    int PrefixHiddenLayer);

public sealed partial class ShowLayerPreferenceDialog : Window, IConfirmableDialog
{
    private sealed record LayerChoice(int Value, string Label);

    private readonly List<LayerChoice> _choices = new()
    {
        new LayerChoice(-1, "変更しない"),
        new LayerChoice(0, "Layer 1"),
        new LayerChoice(1, "Layer 2"),
        new LayerChoice(2, "Layer 3"),
        new LayerChoice(3, "Layer 4")
    };

    public bool IsConfirmed { get; private set; }
    internal ShowLayerPreferenceOptions ResultOptions { get; private set; }

    public ShowLayerPreferenceDialog(ShowLayerPreferenceOptions options)
    {
        ResultOptions = Normalize(options);
        InitializeComponent();
        InitializeCombos();
        ApplyOptions(ResultOptions);
    }

    private void InitializeCombos()
    {
        InitializeCombo(MouseVisibleCombo);
        InitializeCombo(MouseHiddenCombo);
        InitializeCombo(PrefixVisibleCombo);
        InitializeCombo(PrefixHiddenCombo);
    }

    private void InitializeCombo(WpfComboBox combo)
    {
        combo.ItemsSource = _choices;
        combo.DisplayMemberPath = nameof(LayerChoice.Label);
        combo.SelectedValuePath = nameof(LayerChoice.Value);
    }

    private void ApplyOptions(ShowLayerPreferenceOptions options)
    {
        MouseVisibleCombo.SelectedValue = options.MouseGestureVisibleLayer;
        MouseHiddenCombo.SelectedValue = options.MouseGestureHiddenLayer;
        PrefixVisibleCombo.SelectedValue = options.PrefixVisibleLayer;
        PrefixHiddenCombo.SelectedValue = options.PrefixHiddenLayer;
    }

    private ShowLayerPreferenceOptions BuildOptions()
    {
        return Normalize(new ShowLayerPreferenceOptions(
            GetSelected(MouseVisibleCombo),
            GetSelected(MouseHiddenCombo),
            GetSelected(PrefixVisibleCombo),
            GetSelected(PrefixHiddenCombo)));
    }

    private static int GetSelected(Selector combo)
    {
        if (combo.SelectedValue is int v)
        {
            return v;
        }
        return -1;
    }

    private static ShowLayerPreferenceOptions Normalize(ShowLayerPreferenceOptions options)
    {
        static int NormalizeLayer(int value) => value < 0 ? -1 : Math.Clamp(value, 0, 3);
        return new ShowLayerPreferenceOptions(
            NormalizeLayer(options.MouseGestureVisibleLayer),
            NormalizeLayer(options.MouseGestureHiddenLayer),
            NormalizeLayer(options.PrefixVisibleLayer),
            NormalizeLayer(options.PrefixHiddenLayer));
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ResultOptions = BuildOptions();
        IsConfirmed = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}
