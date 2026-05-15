using System;
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
        PopulateCombos(currentRows, currentColumns);
    }

    private void PopulateCombos(int currentRows, int currentColumns)
    {
        for (int i = 2; i <= 8; i++)
        {
            RowsBox.Items.Add(i);
            ColumnsBox.Items.Add(i);
        }

        RowsBox.SelectedItem = ClampSelection(currentRows);
        ColumnsBox.SelectedItem = ClampSelection(currentColumns);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ErrorBlock.Visibility = Visibility.Collapsed;
        if (RowsBox.SelectedItem is not int rows)
        {
            ShowError("行を選択してください (2〜8)。");
            return;
        }

        if (ColumnsBox.SelectedItem is not int cols)
        {
            ShowError("列を選択してください (2〜8)。");
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

    private static int ClampSelection(int value) => Math.Clamp(value, 2, 8);
}
