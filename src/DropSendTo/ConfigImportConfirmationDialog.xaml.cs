using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

internal partial class ConfigImportConfirmationDialog : Window, IConfirmableDialog
{
    public ConfigImportConfirmationDialog(AppLanguage language, ConfigImportRiskSummary risk)
    {
        InitializeComponent();
        ApplyText(language, risk);
    }

    public bool IsConfirmed { get; private set; }

    private void ApplyText(AppLanguage language, ConfigImportRiskSummary risk)
    {
        if (language == AppLanguage.English)
        {
            Title = "Confirm Config Import";
            WarningTitleText.Text = "Import only configurations from a source you trust.";
            WarningBodyText.Text =
                "The imported configuration can launch applications, run keyboard macros, access files, and automatically run enabled slots the next time DropSendTo starts.";
            RiskSummaryText.Text =
                $"Command slots: {risk.CommandSlotCount}\nMacro slots: {risk.MacroSlotCount}\nRun-on-startup slots: {risk.RunOnStartupSlotCount}";
            TrustCheckBox.Content = "I trust the source and understand that these settings can execute actions.";
            ImportButton.Content = "Import";
            CancelButton.Content = "Cancel";
            return;
        }

        Title = "設定インポートの確認";
        WarningTitleText.Text = "信頼できる入手元の設定だけをインポートしてください。";
        WarningBodyText.Text =
            "インポートした設定は、アプリの起動、キーボードマクロ、ファイル操作を実行できます。また、起動時実行が有効なスロットは DropSendTo の次回起動時に自動実行されます。";
        RiskSummaryText.Text =
            $"コマンドを含むスロット: {risk.CommandSlotCount}\nマクロを含むスロット: {risk.MacroSlotCount}\n起動時実行スロット: {risk.RunOnStartupSlotCount}";
        TrustCheckBox.Content = "入手元を信頼し、設定が操作を実行できることを理解しました。";
        ImportButton.Content = "インポート";
        CancelButton.Content = "キャンセル";
    }

    private void OnTrustChanged(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = TrustCheckBox.IsChecked == true;
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (TrustCheckBox.IsChecked != true) return;
        IsConfirmed = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveElement(e.OriginalSource)) return;
        DragMove();
    }

    private static bool IsInteractiveElement(object source)
    {
        if (source is not DependencyObject dependencyObject) return false;
        while (dependencyObject != null)
        {
            if (dependencyObject is ButtonBase
                || dependencyObject is System.Windows.Controls.Primitives.TextBoxBase
                || dependencyObject is System.Windows.Controls.PasswordBox)
            {
                return true;
            }
            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }
        return false;
    }
}
