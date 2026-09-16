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

internal sealed class ConfigImportCommitException : InvalidOperationException
{
    public ConfigImportCommitException(string message, Exception innerException)
        : base(message, innerException)
    {
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
            save(candidate);
        }
        catch (Exception ex)
        {
            throw new ConfigImportCommitException($"Config import save failed: {ex.Message}", ex);
        }

        try
        {
            applyRuntime(candidate);
            return ConfigImportCommitResult.Applied;
        }
        catch (Exception applyException)
        {
            Exception? saveRollbackException = null;
            Exception? runtimeRollbackException = null;
            try
            {
                save(current);
            }
            catch (Exception ex)
            {
                saveRollbackException = ex;
            }

            try
            {
                applyRuntime(current);
            }
            catch (Exception ex)
            {
                runtimeRollbackException = ex;
            }

            var rollbackDetails = string.Empty;
            if (saveRollbackException != null)
            {
                rollbackDetails += $" Disk rollback failed: {saveRollbackException.Message}.";
            }
            if (runtimeRollbackException != null)
            {
                rollbackDetails += $" Runtime rollback failed: {runtimeRollbackException.Message}.";
            }

            throw new ConfigImportCommitException(
                $"Config import runtime apply failed: {applyException.Message}.{rollbackDetails}",
                applyException);
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
