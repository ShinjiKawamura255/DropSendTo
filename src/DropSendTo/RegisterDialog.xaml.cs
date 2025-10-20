using System.Windows;
using Microsoft.Win32;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

public partial class RegisterDialog : Window
{
    public string AppTitle => TitleBox.Text.Trim();
    public string CommandPath => IsMacroMode ? string.Empty : CommandBox.Text.Trim();
    public string ArgumentsTemplate => IsMacroMode ? "{args}" : ArgsBox.Text;
    public string MacroScript => IsMacroMode ? MacroBox.Text : string.Empty;
    public string ShortcutChord { get; private set; } = string.Empty;

    private bool IsMacroMode => MacroModeToggle?.IsChecked == true;

    public RegisterDialog()
    {
        InitializeComponent();
        UpdateModeState();
    }

    public RegisterDialog(SlotModel slot) : this()
    {
        TitleBox.Text = slot.Title ?? string.Empty;
        CommandBox.Text = slot.Command ?? string.Empty;
        ArgsBox.Text = slot.ArgumentsTemplate ?? "{args}";
        MacroBox.Text = slot.KeyboardMacroScript ?? string.Empty;
        ShortcutBox.Text = slot.ShortcutKey ?? string.Empty;
        ShortcutChord = ShortcutBox.Text.Trim();
        MacroModeToggle.IsChecked = !string.IsNullOrWhiteSpace(slot.KeyboardMacroScript);
        UpdateModeState();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (IsMacroMode)
        {
            if (string.IsNullOrWhiteSpace(MacroBox.Text))
            {
                MessageBox.Show("Macro Script モードでは Macro Script を入力してください。", "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CommandBox.Text = string.Empty;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(CommandBox.Text))
            {
                MessageBox.Show("Command モードでは Command を入力してください。", "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MacroBox.Text = string.Empty;
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

    private void OnModeToggleChanged(object sender, RoutedEventArgs e)
    {
        UpdateModeState();
    }

    private void UpdateModeState()
    {
        bool macroMode = IsMacroMode;
        CommandBox.IsEnabled = !macroMode;
        ArgsBox.IsEnabled = !macroMode;
        CommandBrowseButton.IsEnabled = !macroMode;
        MacroBox.IsEnabled = macroMode;
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

    private void OnShowMacroTips(object sender, RoutedEventArgs e)
    {
        var tips = new MacroTipsWindow
        {
            Owner = this
        };
        tips.ShowDialog();
    }
}
