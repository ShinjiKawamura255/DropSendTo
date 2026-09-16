using System;
using System.Collections.Generic;
using System.Linq;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ConfigImportCoordinatorTests
{
    [Fact]
    public void RiskSummary_ShouldCountExecutableAndStartupSettings()
    {
        var config = new AppConfig();
        config.Layers[0].Slots[0].Command = "tool.exe";
        config.Layers[0].Slots[0].RunOnStartup = true;
        config.Layers[0].Slots[1].KeyboardMacroScript = "KEY A";
        config.Layers[0].Slots[2].Command = "other.exe";
        config.Layers[0].Slots[2].KeyboardMacroScript = "COMMAND";

        var summary = ConfigImportRiskSummary.FromConfig(config);

        summary.CommandSlotCount.Should().Be(2);
        summary.MacroSlotCount.Should().Be(2);
        summary.RunOnStartupSlotCount.Should().Be(1);
    }

    [Fact]
    public void Commit_ShouldLeaveStateUntouched_WhenApprovalIsRejected()
    {
        var events = new List<string>();
        var coordinator = new ConfigImportCoordinator();

        var result = coordinator.Commit(
            new AppConfig(),
            new AppConfig(),
            approved: false,
            config => events.Add($"save:{config.Version}"),
            config => events.Add($"apply:{config.Version}"));

        result.Should().Be(ConfigImportCommitResult.Rejected);
        events.Should().BeEmpty();
    }

    [Fact]
    public void Commit_ShouldNotApplyRuntime_WhenCandidateSaveFails()
    {
        var current = new AppConfig { Version = 36 };
        var candidate = new AppConfig { Version = 37 };
        var runtimeApplied = false;
        var coordinator = new ConfigImportCoordinator();

        var action = () => coordinator.Commit(
            current,
            candidate,
            approved: true,
            _ => throw new InvalidOperationException("save failed"),
            _ => runtimeApplied = true);

        action.Should().Throw<ConfigImportCommitException>().WithMessage("*save failed*");
        runtimeApplied.Should().BeFalse();
    }

    [Theory]
    [InlineData((int)ConfigRuntimeApplyStage.Core)]
    [InlineData((int)ConfigRuntimeApplyStage.Theme)]
    [InlineData((int)ConfigRuntimeApplyStage.Shortcuts)]
    [InlineData((int)ConfigRuntimeApplyStage.MouseGestures)]
    [InlineData((int)ConfigRuntimeApplyStage.LayoutAndWindow)]
    [InlineData((int)ConfigRuntimeApplyStage.LanguageAndMenus)]
    public void Commit_ShouldRestoreOldDiskAndRuntime_WhenCandidateApplyStageFails(int failStageValue)
    {
        var failStage = (ConfigRuntimeApplyStage)failStageValue;
        var current = new AppConfig { Version = 36 };
        var candidate = new AppConfig { Version = 37 };
        var events = new List<string>();
        var target = new RecordingRuntimeTarget(candidate, failStage, events);
        var coordinator = new ConfigImportCoordinator();

        var action = () => coordinator.Commit(
            current,
            candidate,
            approved: true,
            config => events.Add($"save:{config.Version}"),
            config => ConfigRuntimeApplier.Apply(config, target));

        action.Should().Throw<ConfigImportCommitException>().WithMessage("*runtime apply failed*");
        events.Should().ContainInOrder($"save:{candidate.Version}", $"apply:{candidate.Version}:{failStage}", $"save:{current.Version}");
        target.AppliedStagesFor(current).Should().Equal(Enum.GetValues<ConfigRuntimeApplyStage>());
        events.Last().Should().Be($"apply:{current.Version}:{ConfigRuntimeApplyStage.LanguageAndMenus}");
    }

    [Fact]
    public void Commit_ShouldApplyEveryRuntimeStageOnce_WhenApproved()
    {
        var current = new AppConfig { Version = 36 };
        var candidate = new AppConfig { Version = 37 };
        var events = new List<string>();
        var target = new RecordingRuntimeTarget(null, null, events);
        var coordinator = new ConfigImportCoordinator();

        var result = coordinator.Commit(
            current,
            candidate,
            approved: true,
            config => events.Add($"save:{config.Version}"),
            config => ConfigRuntimeApplier.Apply(config, target));

        result.Should().Be(ConfigImportCommitResult.Applied);
        events.First().Should().Be($"save:{candidate.Version}");
        target.AppliedStagesFor(candidate).Should().Equal(Enum.GetValues<ConfigRuntimeApplyStage>());
    }

    [Fact]
    public void NormalizeForUse_ShouldNormalizeRepresentativeImportedValues()
    {
        var config = new AppConfig
        {
            SlotRows = -10,
            SlotColumns = 99,
            CurrentLayer = 999,
            MacroConcurrencyMode = (MacroConcurrencyMode)999,
            KeyboardPlacementMode = (WindowPlacementMode)999,
            MouseGestureMinRadiusPixels = 500,
            MouseGestureMaxRadiusPixels = 20,
            Layers = new List<Layer> { new() }
        };

        ConfigService.NormalizeForUse(config);

        config.SlotRows.Should().Be(2);
        config.SlotColumns.Should().Be(8);
        config.Layers.Should().HaveCount(4);
        config.CurrentLayer.Should().Be(3);
        config.MacroConcurrencyMode.Should().Be(MacroConcurrencyMode.Exclusive);
        config.KeyboardPlacementMode.Should().Be(WindowPlacementMode.Fixed);
        config.MouseGestureMinRadiusPixels.Should().Be(config.MouseGestureMaxRadiusPixels);
    }

    private sealed class RecordingRuntimeTarget : IConfigRuntimeApplyTarget
    {
        private readonly AppConfig? _failConfig;
        private readonly ConfigRuntimeApplyStage? _failStage;
        private readonly List<string> _events;

        public RecordingRuntimeTarget(AppConfig? failConfig, ConfigRuntimeApplyStage? failStage, List<string> events)
        {
            _failConfig = failConfig;
            _failStage = failStage;
            _events = events;
        }

        public void ApplyCore(AppConfig config) => Record(config, ConfigRuntimeApplyStage.Core);
        public void ApplyTheme(AppConfig config) => Record(config, ConfigRuntimeApplyStage.Theme);
        public void ApplyShortcuts(AppConfig config) => Record(config, ConfigRuntimeApplyStage.Shortcuts);
        public void ApplyMouseGestures(AppConfig config) => Record(config, ConfigRuntimeApplyStage.MouseGestures);
        public void ApplyLayoutAndWindow(AppConfig config) => Record(config, ConfigRuntimeApplyStage.LayoutAndWindow);
        public void ApplyLanguageAndMenus(AppConfig config) => Record(config, ConfigRuntimeApplyStage.LanguageAndMenus);

        public IEnumerable<ConfigRuntimeApplyStage> AppliedStagesFor(AppConfig config) =>
            _events
                .Where(value => value.StartsWith($"apply:{config.Version}:", StringComparison.Ordinal))
                .Select(value => Enum.Parse<ConfigRuntimeApplyStage>(value[(value.LastIndexOf(':') + 1)..]));

        private void Record(AppConfig config, ConfigRuntimeApplyStage stage)
        {
            _events.Add($"apply:{config.Version}:{stage}");
            if (ReferenceEquals(config, _failConfig) && stage == _failStage)
            {
                throw new InvalidOperationException("runtime apply failed");
            }
        }
    }
}
