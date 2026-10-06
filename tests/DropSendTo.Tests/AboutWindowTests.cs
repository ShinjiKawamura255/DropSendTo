using System.Net;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DropSendTo.Models;
using DropSendTo.Services;
using Xunit;

namespace DropSendTo.Tests;

public class AboutWindowTests
{
    [Theory]
    [InlineData(AppLanguage.Japanese, "最新版をダウンロード", "再確認")]
    [InlineData(AppLanguage.English, "Download latest version", "Check again")]
    public void Check_ShouldShowDownloadOnlyForNewerRelease_AndLocalize(AppLanguage language, string download, string retry)
    {
        RunSta(() =>
        {
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Release())));
            var window = new AboutWindow(language, new UpdateCheckService(client), "v1.3.0");
            try
            {
                window.CheckForUpdatesAsync().GetAwaiter().GetResult();
                var button = (Button)window.FindName("DownloadButton");
                Assert.Equal(Visibility.Visible, button.Visibility);
                Assert.Equal(download, button.Content);
                Assert.Equal(retry, ((Button)window.FindName("RetryButton")).Content);
                window.SetLanguage(language == AppLanguage.English ? AppLanguage.Japanese : AppLanguage.English);
                Assert.Equal(language == AppLanguage.English ? "最新版をダウンロード" : "Download latest version", button.Content);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("v1.4.0", HttpStatusCode.OK, "最新バージョン")]
    [InlineData("v2.0.0", HttpStatusCode.OK, "最新バージョン")]
    [InlineData("0.0.0-dev", HttpStatusCode.OK, "開発版")]
    [InlineData("v1.3.0", HttpStatusCode.Forbidden, "確認できませんでした")]
    [InlineData("v1.3.0", HttpStatusCode.NotFound, "正式リリースがありません")]
    public void Check_ShouldHideDownloadForOtherStates(string current, HttpStatusCode status, string message)
    {
        RunSta(() =>
        {
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Release(status))));
            var window = new AboutWindow(AppLanguage.Japanese, new UpdateCheckService(client), current);
            try
            {
                window.CheckForUpdatesAsync().GetAwaiter().GetResult();
                Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("DownloadButton")).Visibility);
                Assert.Contains(message, ((TextBlock)window.FindName("StatusText")).Text);
                Assert.True(((Button)window.FindName("RetryButton")).IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Check_ShouldPreventDuplicateRequests_AndCancelOnClose()
    {
        RunSta(() =>
        {
            int requests = 0;
            bool cancelled = false;
            using var client = new HttpClient(new Handler(async (_, token) =>
            {
                requests++;
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelled = true; throw; }
                return Release();
            }));
            var window = new AboutWindow(AppLanguage.Japanese, new UpdateCheckService(client), "v1.3.0");
            var check = window.CheckForUpdatesAsync();
            window.Show(); // Loaded must not start a duplicate request while the first check is pending.
            Assert.False(((Button)window.FindName("RetryButton")).IsEnabled);
            Assert.Equal(1, requests);
            Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("DownloadButton")).Visibility);
            window.Close();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            var deadline = DateTime.UtcNow.AddSeconds(3);
            timer.Tick += (_, _) => { if (check.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
            Assert.True(cancelled);
            Assert.True(check.IsCompleted, "Closing must complete the pending check.");
            check.GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void Check_ShouldAllowRetryAfterFailure()
    {
        RunSta(() =>
        {
            int requests = 0;
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(++requests == 1
                ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Release())));
            var window = new AboutWindow(AppLanguage.Japanese, new UpdateCheckService(client), "v1.3.0");
            try
            {
                window.CheckForUpdatesAsync().GetAwaiter().GetResult();
                Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("DownloadButton")).Visibility);
                window.CheckForUpdatesAsync().GetAwaiter().GetResult();
                Assert.Equal(Visibility.Visible, ((Button)window.FindName("DownloadButton")).Visibility);
                Assert.Equal(2, requests);
            }
            finally { window.Close(); }
        });
    }

    private static HttpResponseMessage Release(HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent("""{"tag_name":"v1.4.0","draft":false,"prerelease":false}""")
    };

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA test timed out.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
