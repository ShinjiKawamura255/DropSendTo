using System;
using System.IO;

namespace DropSendTo.Services;

internal static class AppDataPathResolver
{
    internal const string StartupProbeEnvironmentVariable = "DROPSENDTO_STARTUP_PROBE";
    internal const string DataRootEnvironmentVariable = "DROPSENDTO_DATA_ROOT";

    public static string ResolveBaseDirectory()
    {
        var fallback = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return ResolveBaseDirectory(
            Environment.GetEnvironmentVariable(StartupProbeEnvironmentVariable),
            Environment.GetEnvironmentVariable(DataRootEnvironmentVariable),
            fallback);
    }

    internal static string ResolveBaseDirectory(string? probeFlag, string? configuredRoot, string fallbackBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackBaseDirectory);

        if (string.Equals(probeFlag, "1", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(configuredRoot) &&
            Path.IsPathFullyQualified(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot);
        }

        return Path.GetFullPath(fallbackBaseDirectory);
    }
}
