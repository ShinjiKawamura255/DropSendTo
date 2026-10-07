using System;
using System.Linq;
using DropSendTo.Models;

namespace DropSendTo.Services;

internal enum StartupLaunchStatus { ShowWindow, CliLaunched, AlreadyRunning, CliFailed }

internal sealed record StartupLaunchResult(StartupLaunchStatus Status, string? Error = null);

/// <summary>Separates transient CLI execution from the resident window's single-instance lifetime.</summary>
internal static class StartupLaunchCoordinator
{
    internal static StartupLaunchResult Start(bool ownsInstance, string[] arguments,
        Func<AppConfig> loadConfig, Func<SlotModel, string[], LaunchResult> launch)
    {
        if (arguments.Length == 0)
            return new(ownsInstance ? StartupLaunchStatus.ShowWindow : StartupLaunchStatus.AlreadyRunning);
        var failureStatus = ownsInstance ? StartupLaunchStatus.ShowWindow : StartupLaunchStatus.CliFailed;
        try
        {
            var config = loadConfig();
            var layer = Math.Clamp(config.CurrentLayer, 0, config.Layers.Count - 1);
            var slot = config.Layers[layer].Slots.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Command))
                ?? config.Layers.SelectMany(l => l.Slots).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Command));
            if (slot == null) return new(failureStatus, "No registered slot found. Please register a slot.");
            var result = launch(slot, arguments);
            return result.Success ? new(StartupLaunchStatus.CliLaunched) : new(failureStatus, result.Message);
        }
        catch (Exception ex) { return new(failureStatus, ex.Message); }
    }
}
