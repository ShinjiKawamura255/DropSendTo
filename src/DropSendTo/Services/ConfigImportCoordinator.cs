using System;
using System.Linq;
using DropSendTo.Models;

namespace DropSendTo.Services;

internal sealed record ConfigImportRiskSummary(
    int CommandSlotCount,
    int MacroSlotCount,
    int RunOnStartupSlotCount)
{
    public static ConfigImportRiskSummary FromConfig(AppConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        var slots = (config.Layers ?? new())
            .Where(layer => layer?.Slots != null)
            .SelectMany(layer => layer.Slots);

        return new ConfigImportRiskSummary(
            slots.Count(slot => !string.IsNullOrWhiteSpace(slot.Command)),
            slots.Count(slot => !string.IsNullOrWhiteSpace(slot.KeyboardMacroScript)),
            slots.Count(slot => slot.RunOnStartup));
    }
}

internal enum ConfigImportCommitResult
{
    Rejected,
    Applied
}

internal enum ConfigImportFailureKind
{
    CandidateRuntimeApply,
    CandidateSave
}

internal enum ConfigImportRestoreStatus
{
    NotRequired,
    Succeeded,
    Failed
}

internal sealed record ConfigImportFailureResult(
    ConfigImportFailureKind Kind,
    ConfigImportRestoreStatus DiskRestoreStatus,
    ConfigImportRestoreStatus RuntimeRestoreStatus,
    string PrimaryFailureDetail,
    string? DiskRestoreFailureDetail,
    string? RuntimeRestoreFailureDetail);

internal sealed class ConfigImportCommitException : InvalidOperationException
{
    public ConfigImportCommitException(ConfigImportFailureResult failure, Exception innerException)
        : base(BuildMessage(failure), innerException)
    {
        Failure = failure;
    }

    public ConfigImportFailureResult Failure { get; }

    private static string BuildMessage(ConfigImportFailureResult failure)
    {
        var message = failure.Kind == ConfigImportFailureKind.CandidateRuntimeApply
            ? $"Config import runtime apply failed: {failure.PrimaryFailureDetail}."
            : $"Config import save failed: {failure.PrimaryFailureDetail}.";

        if (failure.DiskRestoreStatus == ConfigImportRestoreStatus.Failed)
        {
            message += $" Disk restore failed: {failure.DiskRestoreFailureDetail}.";
        }
        if (failure.RuntimeRestoreStatus == ConfigImportRestoreStatus.Failed)
        {
            message += $" Runtime restore failed: {failure.RuntimeRestoreFailureDetail}.";
        }

        return message;
    }
}

internal static class ConfigImportFailureMessageFormatter
{
    public static string Format(ConfigImportFailureResult failure, AppLanguage language)
    {
        if (failure == null) throw new ArgumentNullException(nameof(failure));

        return language == AppLanguage.English
            ? FormatEnglish(failure)
            : FormatJapanese(failure);
    }

    private static string FormatJapanese(ConfigImportFailureResult failure)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            failure.Kind == ConfigImportFailureKind.CandidateRuntimeApply
                ? "インポート設定を実行中のアプリへ適用できませんでした。"
                : "インポート設定を保存できませんでした。"
        };

        parts.Add(failure.DiskRestoreStatus switch
        {
            ConfigImportRestoreStatus.NotRequired => "ディスク設定は変更されていません。",
            ConfigImportRestoreStatus.Succeeded => "ディスク設定は以前の設定へ復元しました。",
            _ => "ディスク設定を以前の設定へ復元できませんでした。"
        });
        parts.Add(failure.RuntimeRestoreStatus switch
        {
            ConfigImportRestoreStatus.Succeeded => "実行中の設定は以前の設定へ復元しました。",
            ConfigImportRestoreStatus.NotRequired => "実行中の設定は変更されていません。",
            _ => "実行中の設定を以前の設定へ復元できませんでした。"
        });
        parts.Add("ログをご確認ください。");
        return string.Join("", parts);
    }

    private static string FormatEnglish(ConfigImportFailureResult failure)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            failure.Kind == ConfigImportFailureKind.CandidateRuntimeApply
                ? "The imported configuration could not be applied to the running application. "
                : "The imported configuration could not be saved. "
        };

        parts.Add(failure.DiskRestoreStatus switch
        {
            ConfigImportRestoreStatus.NotRequired => "The saved configuration was not changed. ",
            ConfigImportRestoreStatus.Succeeded => "The saved configuration was restored. ",
            _ => "The saved configuration could not be restored. "
        });
        parts.Add(failure.RuntimeRestoreStatus switch
        {
            ConfigImportRestoreStatus.Succeeded => "The running configuration was restored. ",
            ConfigImportRestoreStatus.NotRequired => "The running configuration was not changed. ",
            _ => "The running configuration could not be restored. "
        });
        parts.Add("Please check the log for details.");
        return string.Concat(parts);
    }
}

internal sealed class ConfigImportCoordinator
{
    public ConfigImportCommitResult Commit(
        AppConfig current,
        AppConfig candidate,
        bool approved,
        Action<AppConfig> save,
        Action<AppConfig> applyRuntime)
    {
        if (current == null) throw new ArgumentNullException(nameof(current));
        if (candidate == null) throw new ArgumentNullException(nameof(candidate));
        if (save == null) throw new ArgumentNullException(nameof(save));
        if (applyRuntime == null) throw new ArgumentNullException(nameof(applyRuntime));
        if (!approved) return ConfigImportCommitResult.Rejected;

        try
        {
            applyRuntime(candidate);
        }
        catch (Exception applyException)
        {
            Exception? runtimeRollbackException = null;
            try
            {
                applyRuntime(current);
            }
            catch (Exception ex)
            {
                runtimeRollbackException = ex;
            }

            throw new ConfigImportCommitException(
                new ConfigImportFailureResult(
                    ConfigImportFailureKind.CandidateRuntimeApply,
                    ConfigImportRestoreStatus.NotRequired,
                    runtimeRollbackException == null
                        ? ConfigImportRestoreStatus.Succeeded
                        : ConfigImportRestoreStatus.Failed,
                    applyException.Message,
                    null,
                    runtimeRollbackException?.Message),
                applyException);
        }

        try
        {
            save(candidate);
            return ConfigImportCommitResult.Applied;
        }
        catch (Exception saveException)
        {
            Exception? diskRollbackException = null;
            Exception? runtimeRollbackException = null;
            try
            {
                save(current);
            }
            catch (Exception ex)
            {
                diskRollbackException = ex;
            }

            try
            {
                applyRuntime(current);
            }
            catch (Exception ex)
            {
                runtimeRollbackException = ex;
            }

            throw new ConfigImportCommitException(
                new ConfigImportFailureResult(
                    ConfigImportFailureKind.CandidateSave,
                    diskRollbackException == null
                        ? ConfigImportRestoreStatus.Succeeded
                        : ConfigImportRestoreStatus.Failed,
                    runtimeRollbackException == null
                        ? ConfigImportRestoreStatus.Succeeded
                        : ConfigImportRestoreStatus.Failed,
                    saveException.Message,
                    diskRollbackException?.Message,
                    runtimeRollbackException?.Message),
                saveException);
        }
    }
}

internal enum ConfigRuntimeApplyStage
{
    Core,
    Theme,
    Shortcuts,
    MouseGestures,
    LayoutAndWindow,
    LanguageAndMenus
}

internal interface IConfigRuntimeApplyTarget
{
    void ApplyCore(AppConfig config);
    void ApplyTheme(AppConfig config);
    void ApplyShortcuts(AppConfig config);
    void ApplyMouseGestures(AppConfig config);
    void ApplyLayoutAndWindow(AppConfig config);
    void ApplyLanguageAndMenus(AppConfig config);
}

internal static class ConfigRuntimeApplier
{
    public static void Apply(AppConfig config, IConfigRuntimeApplyTarget target)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (target == null) throw new ArgumentNullException(nameof(target));

        target.ApplyCore(config);
        target.ApplyTheme(config);
        target.ApplyShortcuts(config);
        target.ApplyMouseGestures(config);
        target.ApplyLayoutAndWindow(config);
        target.ApplyLanguageAndMenus(config);
    }
}
