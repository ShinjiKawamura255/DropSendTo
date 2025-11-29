using System.Windows;

namespace DropSendTo;

internal partial class PasswordPromptDialog : Window, IConfirmableDialog
{
    private readonly bool _requireConfirmation;

    public PasswordPromptDialog(string title, string message, bool requireConfirmation)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        _requireConfirmation = requireConfirmation;
        ConfirmPanel.Visibility = requireConfirmation ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => PasswordBox.Focus();
        UpdateState();
    }

    public string Password { get; private set; } = string.Empty;
    public bool IsConfirmed { get; private set; }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        UpdateState();
    }

    private void UpdateState()
    {
        var password = PasswordBox.Password;
        var confirmation = _requireConfirmation ? ConfirmPasswordBox.Password : password;

        if (string.IsNullOrEmpty(password))
        {
            ErrorText.Text = string.Empty;
            OkButton.IsEnabled = false;
            return;
        }

        if (_requireConfirmation && !string.Equals(password, confirmation))
        {
            ErrorText.Text = "確認用パスワードが一致しません。";
            OkButton.IsEnabled = false;
            return;
        }

        ErrorText.Text = string.Empty;
        OkButton.IsEnabled = true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        UpdateState();
        if (!OkButton.IsEnabled)
        {
            if (string.IsNullOrEmpty(PasswordBox.Password))
            {
                ErrorText.Text = "パスワードを入力してください。";
            }
            return;
        }

        Password = PasswordBox.Password;
        IsConfirmed = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}
