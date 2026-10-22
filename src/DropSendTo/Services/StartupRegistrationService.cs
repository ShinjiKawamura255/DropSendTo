using System;
using System.IO;
using Microsoft.Win32;

namespace DropSendTo.Services;

public interface IStartupRegistrationStore
{
    string? GetValue(string valueName);

    void SetValue(string valueName, string value);

    void DeleteValue(string valueName);
}

public sealed class StartupRegistrationService
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "DropSendTo";

    private readonly IStartupRegistrationStore _store;
    private readonly Func<string> _launchCommandProvider;

    public StartupRegistrationService()
        : this(new CurrentUserStartupRegistrationStore(), StartupCommandBuilder.BuildCurrentProcessCommand)
    {
    }

    public StartupRegistrationService(
        IStartupRegistrationStore store,
        Func<string> launchCommandProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _launchCommandProvider = launchCommandProvider ?? throw new ArgumentNullException(nameof(launchCommandProvider));
    }

    public bool IsRegistered() => !string.IsNullOrWhiteSpace(_store.GetValue(ValueName));

    public void Register()
    {
        _store.SetValue(ValueName, _launchCommandProvider());
    }

    public void Unregister()
    {
        _store.DeleteValue(ValueName);
    }
}

public sealed class CurrentUserStartupRegistrationStore : IStartupRegistrationStore
{
    public string? GetValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistrationService.RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void SetValue(string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistrationService.RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows のスタートアップ設定を開けませんでした。");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistrationService.RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

public static class StartupCommandBuilder
{
    public static string BuildCurrentProcessCommand()
    {
        return BuildLaunchCommand(Environment.ProcessPath, Environment.GetCommandLineArgs()[0], AppContext.BaseDirectory);
    }

    internal static string BuildLaunchCommand(string? processPath, string? entryPointPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("実行ファイルのパスを取得できませんでした。");
        }

        var fileName = Path.GetFileNameWithoutExtension(processPath);
        if (string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entryPointPath))
            {
                throw new InvalidOperationException("アプリケーションの起動パスを取得できませんでした。");
            }

            // argv[0] identifies the actual DLL, including renamed DLLs. Resolve a relative name
            // against the entry point directory, not a working directory the app may have changed.
            var entryAssemblyPath = Path.IsPathFullyQualified(entryPointPath)
                ? entryPointPath
                : Path.Combine(baseDirectory, Path.GetFileName(entryPointPath));
            return $"{Quote(processPath)} {Quote(entryAssemblyPath)}";
        }

        return Quote(processPath);
    }

    public static string Quote(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("パスが空です。", nameof(path));
        }

        return $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
