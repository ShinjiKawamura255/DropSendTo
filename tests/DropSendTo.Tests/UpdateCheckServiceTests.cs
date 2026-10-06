using System.Net;
using System.Reflection;
using System.Text;
using DropSendTo.Services;
using Xunit;

namespace DropSendTo.Tests;

public class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("v1.9.0", "v1.10.0", (int)UpdateCheckStatus.UpdateAvailable)]
    [InlineData("1.10.0", "v1.10.0", (int)UpdateCheckStatus.UpToDate)]
    [InlineData("v2.0.0", "v1.10.0", (int)UpdateCheckStatus.UpToDate)]
    [InlineData("v1.3.0-dev.4.abc.dirty", "v1.10.0", (int)UpdateCheckStatus.DevelopmentBuild)]
    [InlineData("1.3.0+abc", "v1.10.0", (int)UpdateCheckStatus.DevelopmentBuild)]
    [InlineData("v1.3.0-rc.1", "v1.10.0", (int)UpdateCheckStatus.DevelopmentBuild)]
    public async Task CheckAsync_ShouldCompareStableVersionsNumerically(string current, string latest, int expected)
    {
        using var client = CreateClient(HttpStatusCode.OK,
            $$"""{"tag_name":"{{latest}}","draft":false,"prerelease":false,"html_url":"https://evil.example/"}""");
        var result = await new UpdateCheckService(client).CheckAsync(current);
        Assert.Equal((UpdateCheckStatus)expected, result.Status);
        Assert.Equal(latest, result.LatestVersion);
        Assert.Equal(expected == (int)UpdateCheckStatus.UpdateAvailable
            ? $"https://github.com/ShinjiKawamura255/DropSendTo/releases/tag/{latest}" : null,
            result.DownloadPage?.AbsoluteUri);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"v1.4.0-rc.1\",\"draft\":false,\"prerelease\":true}")]
    [InlineData("{\"tag_name\":\"v1.4.0\",\"draft\":true,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v01.4.0\",\"draft\":false,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v1.4.0\"}")]
    [InlineData("{\"tag_name\":42,\"draft\":false,\"prerelease\":false}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task CheckAsync_ShouldFail_WhenResponseIsInvalid(string json)
    {
        using var client = CreateClient(HttpStatusCode.OK, json);
        var result = await new UpdateCheckService(client).CheckAsync("v1.3.0");
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.DownloadPage);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, (int)UpdateCheckStatus.NoRelease)]
    [InlineData(HttpStatusCode.Forbidden, (int)UpdateCheckStatus.Failed)]
    [InlineData(HttpStatusCode.TooManyRequests, (int)UpdateCheckStatus.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, (int)UpdateCheckStatus.Failed)]
    public async Task CheckAsync_ShouldDistinguishMissingReleaseAndHttpFailures(HttpStatusCode httpStatus, int expected)
    {
        using var client = CreateClient(httpStatus, "{}");
        Assert.Equal((UpdateCheckStatus)expected, (await new UpdateCheckService(client).CheckAsync("v1.3.0")).Status);
    }

    [Fact]
    public async Task CheckAsync_ShouldHandleNetworkFailureAndTimeout()
    {
        foreach (var exception in new Exception[] { new HttpRequestException(), new TaskCanceledException() })
        {
            using var client = new HttpClient(new Handler((_, _) => throw exception));
            Assert.Equal(UpdateCheckStatus.Failed,
                (await new UpdateCheckService(client).CheckAsync("v1.3.0")).Status);
        }
    }

    [Fact]
    public async Task CheckAsync_ShouldPropagateCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new UpdateCheckService(client).CheckAsync("v1.3.0", cts.Token));
    }

    [Fact]
    public async Task CheckAsync_ShouldRequestOnlyPublicLatestReleaseWithoutCredentials()
    {
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/ShinjiKawamura255/DropSendTo/releases/latest", request.RequestUri?.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.NotEmpty(request.Headers.UserAgent);
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/vnd.github+json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }));
        await new UpdateCheckService(client).CheckAsync("v1.3.0");
    }

    [Fact]
    public void CurrentVersion_ShouldReadEmbeddedInformationalVersion()
    {
        Assert.Equal(typeof(UpdateCheckService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            UpdateCheckService.CurrentVersion);
    }

    private static HttpClient CreateClient(HttpStatusCode status, string json) => new(new Handler((_, _) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") })));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
