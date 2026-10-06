using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DropSendTo.Services;

internal enum UpdateCheckStatus { UpdateAvailable, UpToDate, DevelopmentBuild, NoRelease, Failed }

internal sealed record UpdateCheckResult(UpdateCheckStatus Status, string? LatestVersion = null, Uri? DownloadPage = null);

/// <summary>Checks the public stable release without credentials or local configuration.</summary>
internal sealed class UpdateCheckService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HttpClient _client;
    internal static readonly Uri ReleasesPage = new("https://github.com/ShinjiKawamura255/DropSendTo/releases");
    internal static string CurrentVersion => typeof(UpdateCheckService).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown-dev";

    internal UpdateCheckService(HttpClient? client = null) => _client = client ?? SharedClient;

    internal async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/ShinjiKawamura255/DropSendTo/releases/latest");
            request.Headers.UserAgent.ParseAdd("DropSendTo-UpdateCheck/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(UpdateCheckStatus.NoRelease);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
                !TryParseStableVersion(tag.GetString(), out var latest))
                return new(UpdateCheckStatus.Failed);

            var latestTag = tag.GetString()!;
            if (!TryParseStableVersion(currentVersion, out var current))
                return new(UpdateCheckStatus.DevelopmentBuild, latestTag);
            return latest > current
                ? new(UpdateCheckStatus.UpdateAvailable, latestTag,
                    new Uri($"{ReleasesPage}/tag/{Uri.EscapeDataString(latestTag)}"))
                : new(UpdateCheckStatus.UpToDate, latestTag);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(UpdateCheckStatus.Failed);
        }
        catch (HttpRequestException) { return new(UpdateCheckStatus.Failed); }
        catch (JsonException) { return new(UpdateCheckStatus.Failed); }
    }

    private static bool TryParseStableVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        return text != null && Regex.IsMatch(text, @"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") &&
            Version.TryParse(text.TrimStart('v'), out version!);
    }
}
