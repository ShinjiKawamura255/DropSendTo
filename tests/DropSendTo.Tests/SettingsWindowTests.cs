using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using DropSendTo.Models;
using Xunit;

namespace DropSendTo.Tests;

public class SettingsWindowTests
{
    [Fact]
    public void Refresh_ShouldNotApplyChanges_AndShouldReadReplacedConfig()
    {
        RunSta(() =>
        {
            var config = new AppConfig { AlwaysOnTop = false, SlotSize = SlotSize.Small };
            int changes = 0;
            var window = new SettingsWindow(() => config, () => false,
                (_, _) => changes++, _ => Task.CompletedTask);
            try
            {
                var size = (ComboBox)window.GetSettingControl(SettingsOption.SlotSize);
                Assert.Equal(SlotSize.Small, size.SelectedValue);
                config = new AppConfig { AlwaysOnTop = true, SlotSize = SlotSize.Large };
                window.RefreshSettings();
                Assert.True(((CheckBox)window.GetSettingControl(SettingsOption.AlwaysOnTop)).IsChecked);
                Assert.Equal(SlotSize.Large, size.SelectedValue);
                Assert.Equal(0, changes);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Choice_ShouldApplyOnce_WhenUserChangesValue()
    {
        RunSta(() =>
        {
            var config = new AppConfig();
            var changes = new List<(SettingsOption Option, object Value)>();
            var window = new SettingsWindow(() => config, () => false,
                (option, value) =>
                {
                    changes.Add((option, value));
                    if (option == SettingsOption.Theme) config.Theme = (AppTheme)value;
                }, _ => Task.CompletedTask);
            try
            {
                ((ComboBox)window.GetSettingControl(SettingsOption.Theme)).SelectedValue = AppTheme.Light;
                Assert.Single(changes);
                Assert.Equal((SettingsOption.Theme, (object)AppTheme.Light), changes[0]);
                window.RefreshSettings();
                Assert.Single(changes);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(AppLanguage.Japanese)]
    [InlineData(AppLanguage.English)]
    public void Categories_ShouldKeepAllSettingsReachable_AndHonorFollowKeyboard(AppLanguage language)
    {
        RunSta(() =>
        {
            var config = new AppConfig
            {
                Language = language,
                MousePlacementFollowsKeyboard = true,
                MousePlacementMode = WindowPlacementMode.CursorScreenCenter,
                SearchPlacementFollowsKeyboard = true,
                SearchPlacementMode = SearchOverlayPlacementMode.MouseFollow
            };
            var window = new SettingsWindow(() => config, () => true,
                (_, _) => throw new Exception("Viewing settings must not change them."), _ => Task.CompletedTask);
            try
            {
                Assert.Equal(-1, ((ComboBox)window.GetSettingControl(SettingsOption.MousePlacement)).SelectedValue);
                Assert.Equal(-1, ((ComboBox)window.GetSettingControl(SettingsOption.SearchPlacement)).SelectedValue);
                for (int i = 0; i < 5; i++) window.SelectCategory(i);
                Assert.True(((CheckBox)window.GetSettingControl(SettingsOption.StartupRegistration)).IsChecked);
                Assert.Equal(language, ((ComboBox)window.GetSettingControl(SettingsOption.Language)).SelectedValue);
                Assert.Equal(5, window.CategoryCount);
                Assert.Equal(Enum.GetValues<SettingsAction>().Length, window.ActionCount);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void FailedChange_ShouldShowError_AndResynchronizeTheControl()
    {
        RunSta(() =>
        {
            var config = new AppConfig();
            var window = new SettingsWindow(() => config, () => false,
                (_, _) => throw new IOException("Save failed"), _ => Task.CompletedTask);
            try
            {
                var theme = (ComboBox)window.GetSettingControl(SettingsOption.Theme);
                theme.SelectedValue = AppTheme.Light;
                Assert.Equal(AppTheme.Dark, theme.SelectedValue);
                Assert.Equal("Save failed", ((TextBlock)window.FindName("StatusText")).Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void LanguageRefresh_ShouldPreserveCategory_AndNotApplyAnyChanges()
    {
        RunSta(() =>
        {
            var config = new AppConfig();
            int changes = 0;
            var window = new SettingsWindow(() => config, () => false,
                (_, _) => changes++, _ => Task.CompletedTask);
            try
            {
                window.SelectCategory(4);
                config.Language = AppLanguage.English;
                window.RefreshSettings();
                Assert.Equal(4, window.SelectedCategory);
                Assert.Equal("Settings", window.Title);
                Assert.Equal(AppLanguage.English,
                    ((ComboBox)window.GetSettingControl(SettingsOption.Language)).SelectedValue);
                Assert.Equal(0, changes);
            }
            finally { window.Close(); }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
