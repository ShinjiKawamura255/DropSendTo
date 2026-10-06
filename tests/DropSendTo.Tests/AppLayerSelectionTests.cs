using System;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class AppLayerSelectionTests
{
    private static readonly ActiveWindowInfo Excel = new(new IntPtr(1), "EXCEL.EXE", "売上.xlsx - Excel", "C:\\Office\\EXCEL.EXE");

    private static AppConfig CreateConfig() => new()
    {
        AppLayerSwitchingEnabled = true,
        AppLayerRules = new()
        {
            new() { ExecutableName = "excel.exe", TitleContains = "売上", LayerIndex = 2 },
            new() { ExecutableName = "EXCEL.EXE", LayerIndex = 1 }
        }
    };

    [Fact]
    public void Match_ShouldUseFirstEnabledValidRule_WithBothConditions()
    {
        var config = CreateConfig();
        AppLayerRuleMatcher.FindMatch(config, Excel).Should().Be(0);
        AppLayerRuleMatcher.FindMatch(config, Excel with { Title = "予算.xlsx" }).Should().Be(1);
        AppLayerRuleMatcher.FindMatch(config, Excel with { ExecutableName = "other-excel.exe" }).Should().BeNull();
        config.AppLayerRules[0].Enabled = false;
        AppLayerRuleMatcher.FindMatch(config, Excel).Should().Be(1);
        config.AppLayerRules[0].Enabled = true;
        config.AppLayerRules[0].LayerIndex = 99;
        AppLayerRuleMatcher.FindMatch(config, Excel).Should().Be(1);
    }

    [Fact]
    public void Match_ShouldRestrictPathOnlyWhenSpecified()
    {
        var config = CreateConfig();
        config.AppLayerRules[0].ExecutablePath = "C:\\Other\\EXCEL.EXE";
        AppLayerRuleMatcher.FindMatch(config, Excel).Should().Be(1);
        config.AppLayerRules[0].ExecutablePath = "c:\\office\\excel.exe";
        AppLayerRuleMatcher.FindMatch(config, Excel).Should().Be(0);
        AppLayerRuleMatcher.FindMatch(config, Excel with { ExecutablePath = string.Empty }).Should().Be(1);
    }

    [Fact]
    public void Selection_ShouldApplyCoalescedExternalChange_UnlessUserHasSelectedManually()
    {
        var selection = new AppLayerSelection(0);
        var config = CreateConfig();
        selection.RequestEvaluation();
        selection.Observe(Excel, isOwnForeground: true);
        selection.TakePendingLayer(config, false).Should().Be(2);
        selection.RequestEvaluation();
        selection.SelectManually(3);
        selection.Observe(Excel, isOwnForeground: true);
        selection.TakePendingLayer(config, false).Should().BeNull();
    }

    [Fact]
    public void Selection_ShouldPreserveManualChoiceUntilExternalReactivation()
    {
        var selection = new AppLayerSelection(0);
        var config = CreateConfig();
        selection.Observe(Excel, isOwnForeground: false);
        selection.TakePendingLayer(config, blocked: false).Should().Be(2);
        selection.ManualLayer.Should().Be(0);
        selection.Observe(Excel, isOwnForeground: true);
        selection.SelectManually(3);
        selection.Observe(Excel with { Title = "changed" }, isOwnForeground: true);
        selection.TakePendingLayer(config, blocked: false).Should().BeNull();
        selection.Observe(Excel, isOwnForeground: false);
        selection.TakePendingLayer(config, blocked: false).Should().Be(2);
        selection.ManualLayer.Should().Be(3);
    }

    [Fact]
    public void Selection_ShouldApplyLatestContextAfterSearchDragOrEditing()
    {
        var selection = new AppLayerSelection(0);
        var config = CreateConfig();
        selection.Observe(Excel, false);
        selection.TakePendingLayer(config, blocked: true).Should().BeNull();
        selection.Observe(Excel with { Title = "予算.xlsx" }, false);
        selection.Observe(Excel with { Title = "予算.xlsx" }, true);
        selection.TakePendingLayer(config, blocked: false).Should().Be(1);
        selection.TakePendingLayer(config, blocked: false).Should().BeNull();
    }

    [Fact]
    public void Selection_ShouldKeepPinnedLayerAndReevaluateOnRelease()
    {
        var selection = new AppLayerSelection(0);
        var config = CreateConfig();
        selection.SetPinned(true);
        selection.Observe(Excel, false);
        selection.SelectManually(3);
        selection.TakePendingLayer(config, false).Should().BeNull();
        selection.SetPinned(false);
        selection.TakePendingLayer(config, false).Should().Be(2);
    }

    [Fact]
    public void Selection_ShouldUseCommonOrManualFallback_AndPreserveDisabledBehavior()
    {
        var selection = new AppLayerSelection(3);
        var config = CreateConfig();
        selection.Observe(Excel with { ExecutableName = "notepad.exe" }, false);
        selection.TakePendingLayer(config, false).Should().Be(3);
        config.AppLayerFallbackLayer = 0;
        selection.RequestEvaluation();
        selection.TakePendingLayer(config, false).Should().Be(0);
        config.AppLayerSwitchingEnabled = false;
        selection.Observe(Excel, false);
        selection.TakePendingLayer(config, false).Should().BeNull();
        AppLayerSelection.ShouldSwitchForShortcut(config).Should().BeTrue();
        config.AppLayerSwitchingEnabled = true;
        AppLayerSelection.ShouldSwitchForShortcut(config).Should().BeFalse();
    }

    [Fact]
    public void Selection_ShouldLeaveDisplayUnchanged_WhenTargetCannotBeRead()
    {
        var selection = new AppLayerSelection(0);
        selection.RequestEvaluation();
        selection.Observe(null, isOwnForeground: true);
        selection.TakePendingLayer(CreateConfig(), false).Should().BeNull();
        selection.ManualLayer.Should().Be(0);
    }

    [Fact]
    public void Normalize_ShouldDisableInvalidTargetsWithoutRedirectingRules()
    {
        var config = CreateConfig();
        config.AppLayerFallbackLayer = 99;
        config.AppLayerRules[0].LayerIndex = 99;
        config.AppLayerRules[1].ExecutableName = " EXCEL.EXE ";
        ConfigService.NormalizeForUse(config);
        config.AppLayerFallbackLayer.Should().Be(-1);
        config.AppLayerRules[0].Enabled.Should().BeFalse();
        config.AppLayerRules[0].LayerIndex.Should().Be(99);
        config.AppLayerRules[1].ExecutableName.Should().Be("EXCEL.EXE");
    }
}
