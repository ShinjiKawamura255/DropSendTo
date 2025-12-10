using System.Globalization;
using System.Windows;

namespace DropSendTo;

internal partial class SlotLayoutDialog : Window
{
    public int Rows { get; private set; }
    public int Columns { get; private set; }

    public SlotLayoutDialog(int currentRows, int currentColumns)
    {
        InitializeComponent();
        Rows = currentRows;
        Columns = currentColumns;
        RowsBox.Text = currentRows.ToString(CultureInfo.CurrentCulture);
        ColumnsBox.Text = currentColumns.ToString(CultureInfo.CurrentCulture);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ErrorBlock.Visibility = Visibility.Collapsed;
        if (!TryParse(RowsBox.Text, out var rows) || rows is < 2 or > 8)
        {
            ShowError("行は 2 〜 8 の範囲で入力してください。");
            return;
        }

        if (!TryParse(ColumnsBox.Text, out var cols) || cols is < 2 or > 8)
        {
            ShowError("列は 2 〜 8 の範囲で入力してください。");
            return;
        }

        Rows = rows;
        Columns = cols;
        DialogResult = true;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorBlock.Text = message;
        ErrorBlock.Visibility = Visibility.Visible;
    }

    private static bool TryParse(string? text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
    }
}
