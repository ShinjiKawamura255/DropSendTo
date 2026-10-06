using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

internal partial class AboutWindow : Window
{
    private readonly UpdateCheckService _service;
    private readonly string _currentVersion;
    private readonly CancellationTokenSource _lifetime = new();
    private AppLanguage _language;
    private UpdateCheckResult? _result;
    private bool _checking;
    private bool _closed;

    internal AboutWindow(AppLanguage language, UpdateCheckService? service = null, string? currentVersion = null)
    {
        InitializeComponent();
        _service = service ?? new UpdateCheckService();
        _currentVersion = currentVersion ?? UpdateCheckService.CurrentVersion;
        SetLanguage(language);
        Loaded += async (_, _) => await CheckForUpdatesAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            if (!_checking) _lifetime.Dispose();
        };
    }

    internal void SetLanguage(AppLanguage language)
    {
        _language = language;
        RefreshText();
    }

    private string Text(string japanese, string english) => _language == AppLanguage.English ? english : japanese;

    internal async Task CheckForUpdatesAsync()
    {
        if (_checking || _closed) return;
        _checking = true;
        _result = null;
        RefreshText();
        try { _result = await _service.CheckAsync(_currentVersion, _lifetime.Token); }
        catch (OperationCanceledException) when (_closed) { }
        finally
        {
            _checking = false;
            if (_closed) _lifetime.Dispose();
            else RefreshText();
        }
    }

    private void RefreshText()
    {
        Title = Text("DropSendTo について", "About DropSendTo");
        CurrentVersionText.Text = Text("現在のバージョン: ", "Current version: ") + _currentVersion;
        LatestVersionText.Text = Text("最新の正式版: ", "Latest stable release: ") + _result?.LatestVersion;
        LatestVersionText.Visibility = _result?.LatestVersion != null ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _checking ? Text("更新を確認しています…", "Checking for updates…") : _result?.Status switch
        {
            UpdateCheckStatus.UpdateAvailable => Text("新しいバージョンが利用できます。", "A new version is available."),
            UpdateCheckStatus.UpToDate => Text("最新バージョンです（公開版以上）。", "You are up to date (at or above the published release)."),
            UpdateCheckStatus.DevelopmentBuild => Text("開発版・プレリリース版のため更新判定の対象外です。", "Development and prerelease builds are not compared with stable releases."),
            UpdateCheckStatus.NoRelease => Text("公開された正式リリースがありません。", "No stable release has been published."),
            UpdateCheckStatus.Failed => Text("更新を確認できませんでした。通信環境を確認して再確認してください。", "Unable to check for updates. Check your connection and try again."),
            _ => Text("この画面を開くと GitHub に更新を確認します。", "Opening this window checks GitHub for updates.")
        };
        DownloadButton.Content = Text("最新版をダウンロード", "Download latest version");
        DownloadButton.Visibility = _result?.Status == UpdateCheckStatus.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        DownloadHintText.Text = Text("GitHub のリリースページを開きます。ダウンロード後、アプリを終了してファイルを置き換えてください。",
            "Opens the GitHub release page. After downloading, close the app and replace its files.");
        DownloadHintText.Visibility = DownloadButton.Visibility;
        RetryButton.Content = Text("再確認", "Check again");
        RetryButton.IsEnabled = !_checking;
        ReleasesButton.Content = Text("GitHub Releases", "GitHub Releases");
        CloseButton.Content = Text("閉じる", "Close");
    }

    private void OpenPage(Uri page)
    {
        try { Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            StatusText.Text = Text("ブラウザを開けませんでした。", "Unable to open the browser.");
        }
    }

    private void OnDownload(object sender, RoutedEventArgs e) { if (_result?.DownloadPage is Uri page) OpenPage(page); }
    private void OnReleases(object sender, RoutedEventArgs e) => OpenPage(UpdateCheckService.ReleasesPage);
    private async void OnRetry(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
