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
    public void Commit_ShouldRestoreDiskAndRuntime_WhenCandidateSaveFails()
    {
        var current = new AppConfig { Version = 36 };
        var candidate = new AppConfig { Version = 37 };
        var events = new List<string>();
        var coordinator = new ConfigImportCoordinator();

        var action = () => coordinator.Commit(
            current,
            candidate,
            approved: true,
            config =>
            {
                events.Add($"save:{config.Version}");
                if (ReferenceEquals(config, candidate))
                {
                    throw new InvalidOperationException("save failed");
                }
            },
            config => events.Add($"apply:{config.Version}"));

        var exception = action.Should().Throw<ConfigImportCommitException>().Which;
        exception.Failure.Kind.Should().Be(ConfigImportFailureKind.CandidateSave);
        exception.Failure.DiskRestoreStatus.Should().Be(ConfigImportRestoreStatus.Succeeded);
        exception.Failure.RuntimeRestoreStatus.Should().Be(ConfigImportRestoreStatus.Succeeded);
        exception.Failure.PrimaryFailureDetail.Should().Contain("save failed");
        events.Should().Equal(
            $"apply:{candidate.Version}",
            $"save:{candidate.Version}",
            $"save:{current.Version}",
            $"apply:{current.Version}");
    }

    [Theory]
    [InlineData((int)ConfigRuntimeApplyStage.Core)]
    [InlineData((int)ConfigRuntimeApplyStage.Theme)]
    [InlineData((int)ConfigRuntimeApplyStage.Shortcuts)]
    [InlineData((int)ConfigRuntimeApplyStage.MouseGestures)]
    [InlineData((int)ConfigRuntimeApplyStage.LayoutAndWindow)]
    [InlineData((int)ConfigRuntimeApplyStage.LanguageAndMenus)]
    public void Commit_ShouldLeaveDiskUntouchedAndRestoreRuntime_WhenCandidateApplyStageFails(int failStageValue)
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

        var exception = action.Should().Throw<ConfigImportCommitException>().Which;
        exception.Failure.Kind.Should().Be(ConfigImportFailureKind.CandidateRuntimeApply);
        exception.Failure.DiskRestoreStatus.Should().Be(ConfigImportRestoreStatus.NotRequired);
        exception.Failure.RuntimeRestoreStatus.Should().Be(ConfigImportRestoreStatus.Succeeded);
        events.Should().NotContain(value => value.StartsWith("save:", StringComparison.Ordinal));
        events.Should().ContainInOrder($"apply:{candidate.Version}:{failStage}", $"apply:{current.Version}:{ConfigRuntimeApplyStage.Core}");
        target.AppliedStagesFor(current).Should().Equal(Enum.GetValues<ConfigRuntimeApplyStage>());
        events.Last().Should().Be($"apply:{current.Version}:{ConfigRuntimeApplyStage.LanguageAndMenus}");
    }

    [Fact]
    public void Commit_ShouldReportBothRestoreFailures_WhenCandidateSaveAndRollbacksFail()
    {
        var current = new AppConfig { Version = 36 };
        var candidate = new AppConfig { Version = 37 };
        var coordinator = new ConfigImportCoordinator();

        var action = () => coordinator.Commit(
            current,
            candidate,
            approved: true,
            config => throw new InvalidOperationException(
                ReferenceEquals(config, candidate) ? "candidate save failed" : "disk restore failed"),
            config =>
            {
                if (ReferenceEquals(config, current))
                {
                    throw new InvalidOperationException("runtime restore failed");
                }
            });

        var exception = action.Should().Throw<ConfigImportCommitException>().Which;
        exception.Failure.Kind.Should().Be(ConfigImportFailureKind.CandidateSave);
        exception.Failure.DiskRestoreStatus.Should().Be(ConfigImportRestoreStatus.Failed);
        exception.Failure.RuntimeRestoreStatus.Should().Be(ConfigImportRestoreStatus.Failed);
        exception.Failure.DiskRestoreFailureDetail.Should().Contain("disk restore failed");
        exception.Failure.RuntimeRestoreFailureDetail.Should().Contain("runtime restore failed");
    }

    [Fact]
    public void Commit_ShouldReportRuntimeRestoreFailureWithoutSaving_WhenCandidateRuntimeFails()
    {
        var current = new AppConfig { Version = 36 };
        var candidate = new AppConfig { Version = 37 };
        var saveCalls = 0;
        var coordinator = new ConfigImportCoordinator();

        var action = () => coordinator.Commit(
            current,
            candidate,
            approved: true,
            _ => saveCalls++,
            config => throw new InvalidOperationException(
                ReferenceEquals(config, candidate) ? "candidate runtime failed" : "runtime restore failed"));

        var exception = action.Should().Throw<ConfigImportCommitException>().Which;
        exception.Failure.Kind.Should().Be(ConfigImportFailureKind.CandidateRuntimeApply);
        exception.Failure.DiskRestoreStatus.Should().Be(ConfigImportRestoreStatus.NotRequired);
        exception.Failure.RuntimeRestoreStatus.Should().Be(ConfigImportRestoreStatus.Failed);
        exception.Failure.RuntimeRestoreFailureDetail.Should().Contain("runtime restore failed");
        saveCalls.Should().Be(0);
    }

    [Theory]
    [InlineData((int)AppLanguage.Japanese, "ディスク設定を以前の設定へ復元できませんでした", "実行中の設定を以前の設定へ復元できませんでした")]
    [InlineData((int)AppLanguage.English, "The saved configuration could not be restored", "The running configuration could not be restored")]
    public void FailureMessage_ShouldIdentifyEachIncompleteRestore(
        int languageValue,
        string expectedDiskText,
        string expectedRuntimeText)
    {
        var failure = new ConfigImportFailureResult(
            ConfigImportFailureKind.CandidateSave,
            ConfigImportRestoreStatus.Failed,
            ConfigImportRestoreStatus.Failed,
            "candidate save failed",
            "disk restore failed",
            "runtime restore failed");

        var message = ConfigImportFailureMessageFormatter.Format(failure, (AppLanguage)languageValue);

        message.Should().Contain(expectedDiskText);
        message.Should().Contain(expectedRuntimeText);
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
        events.Should().ContainInOrder(
            $"apply:{candidate.Version}:{ConfigRuntimeApplyStage.Core}",
            $"apply:{candidate.Version}:{ConfigRuntimeApplyStage.LanguageAndMenus}",
            $"save:{candidate.Version}");
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
