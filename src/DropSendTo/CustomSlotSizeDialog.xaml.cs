using System;
using System.Globalization;
using System.Windows;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

internal partial class CustomSlotSizeDialog : Window
{
    public CustomSlotSizeOptions ResultOptions { get; private set; } = CustomSlotSizeOptions.CreateDefault();
    public event EventHandler<CustomSlotSizeOptions>? SlotSizeApplied;

    private CustomSlotSizeOptions _current;

    public CustomSlotSizeDialog(CustomSlotSizeOptions options)
    {
        _current = options?.Clone() ?? CustomSlotSizeOptions.CreateDefault();
        InitializeComponent();
        PopulateFields();
    }

    private void PopulateFields()
    {
        SlotHeightBox.Text = _current.SlotHeight.ToString(CultureInfo.CurrentCulture);
        TitleFontBox.Text = _current.TitleFontSize.ToString(CultureInfo.CurrentCulture);
        StatusFontBox.Text = _current.StatusFontSize.ToString(CultureInfo.CurrentCulture);
        RowStepBox.Text = _current.RowStep.ToString(CultureInfo.CurrentCulture);
        ColumnStepBox.Text = _current.ColumnStep.ToString(CultureInfo.CurrentCulture);
        SlotMarginBox.Text = _current.SlotMargin.ToString(CultureInfo.CurrentCulture);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (!TryGetValidatedOptions(out var normalized, out var error))
        {
            ErrorBlock.Text = error;
            ErrorBlock.Visibility = Visibility.Visible;
            return;
        }

        ErrorBlock.Visibility = Visibility.Collapsed;
        ResultOptions = normalized;
        _current = normalized.Clone();
        PopulateFields();
        SlotSizeApplied?.Invoke(this, ResultOptions);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!TryGetValidatedOptions(out var normalized, out var error))
        {
            ErrorBlock.Text = error;
            ErrorBlock.Visibility = Visibility.Visible;
            return;
        }

        ErrorBlock.Visibility = Visibility.Collapsed;
        ResultOptions = normalized;
        DialogResult = true;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool TryGetValidatedOptions(out CustomSlotSizeOptions normalized, out string error)
    {
        var options = _current.Clone();
        normalized = options;
        error = string.Empty;
        if (!TryParseDouble(SlotHeightBox.Text, out var height))
        {
            error = "スロット高さを数値で入力してください。";
            return false;
        }

        if (!TryParseDouble(TitleFontBox.Text, out var titleFont))
        {
            error = "タイトルフォントサイズを数値で入力してください。";
            return false;
        }

        if (!TryParseDouble(StatusFontBox.Text, out var statusFont))
        {
            error = "ステータスフォントサイズを数値で入力してください。";
            return false;
        }

        if (!TryParseDouble(RowStepBox.Text, out var rowStep))
        {
            error = "縦方向ステップを数値で入力してください。";
            return false;
        }

        if (!TryParseDouble(ColumnStepBox.Text, out var columnStep))
        {
            error = "横方向ステップを数値で入力してください。";
            return false;
        }

        if (!TryParseDouble(SlotMarginBox.Text, out var margin))
        {
            error = "スロット間隔を数値で入力してください。";
            return false;
        }

        options.SlotHeight = height;
        options.TitleFontSize = titleFont;
        options.StatusFontSize = statusFont;
        options.RowStep = rowStep;
        options.ColumnStep = columnStep;
        options.SlotMargin = margin;

        normalized = CustomSlotSizeNormalizer.Normalize(options.Clone());
        if (normalized.SlotHeight != options.SlotHeight && options.SlotHeight < normalized.SlotHeight)
        {
            error = $"スロット高さはフォントと余白に合わせて最低 {normalized.SlotHeight:0.#} 以上にする必要があります。";
            return false;
        }

        return true;
    }

    private static bool TryParseDouble(string? input, out double value)
    {
        return double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}
