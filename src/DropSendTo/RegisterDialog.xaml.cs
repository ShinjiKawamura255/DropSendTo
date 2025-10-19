using System.Windows;
using Microsoft.Win32;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

public partial class RegisterDialog : Window
{
    public string AppTitle => TitleBox.Text.Trim();
    public string CommandPath => CommandBox.Text.Trim();
    public string ArgumentsTemplate => ArgsBox.Text;
    public string MacroScript => MacroBox.Text;
    public string ShortcutChord { get; private set; } = string.Empty;

    public RegisterDialog()
    {
        InitializeComponent();
    }

    public RegisterDialog(SlotModel slot) : this()
    {
        TitleBox.Text = slot.Title ?? string.Empty;
        CommandBox.Text = slot.Command ?? string.Empty;
        ArgsBox.Text = slot.ArgumentsTemplate ?? "{args}";
        MacroBox.Text = slot.KeyboardMacroScript ?? string.Empty;
        ShortcutBox.Text = slot.ShortcutKey ?? string.Empty;
        ShortcutChord = ShortcutBox.Text.Trim();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        bool hasCommand = !string.IsNullOrWhiteSpace(CommandBox.Text);
        bool hasMacro = !string.IsNullOrWhiteSpace(MacroBox.Text);
        if (!hasCommand && !hasMacro)
        {
            MessageBox.Show("Command か Macro Script のどちらかを設定してください。", "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var shortcutText = ShortcutBox.Text.Trim();
        if (shortcutText.Length > 0)
        {
            if (!KeyChordParser.TryParse(shortcutText, out var chord, out var error))
            {
                MessageBox.Show(error ?? "ショートカットの書式が正しくありません。", "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ShortcutChord = chord.NormalizedString;
            ShortcutBox.Text = chord.NormalizedString;
        }
        else
        {
            ShortcutChord = string.Empty;
        }

        DialogResult = true;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            CommandBox.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                TitleBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    private void OnInsertNewline(object sender, RoutedEventArgs e)
    {
        var idx = TitleBox.CaretIndex;
        TitleBox.Text = TitleBox.Text.Insert(idx, System.Environment.NewLine);
        TitleBox.CaretIndex = idx + System.Environment.NewLine.Length;
        TitleBox.Focus();
    }
}
