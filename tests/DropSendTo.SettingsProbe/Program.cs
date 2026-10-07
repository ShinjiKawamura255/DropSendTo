using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using DropSendTo;
using DropSendTo.Models;
using DropSendTo.Services;

// Run only through Test-SettingsWindow.ps1, with an isolated data directory.
internal static class Program
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--cli-target")
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(args.Skip(2).ToArray()));
            return 0;
        }
        bool cliOnly = args[0] == "--cli-only";
        string output = Path.GetFullPath(args[cliOnly ? 1 : 0]);
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable(AppDataPathResolver.StartupProbeEnvironmentVariable, "1");
        Environment.SetEnvironmentVariable(AppDataPathResolver.DataRootEnvironmentVariable, Path.Combine(output, "data"));
        var report = new List<string>();
        try
        {
            VerifyResidentCli(output, cliOnly ? args[2] : null);
            report.Add("PASS: actual App startup executes CLI arguments while the resident mutex is held, without rewriting saved settings");
            if (cliOnly)
            {
                File.WriteAllLines(Path.Combine(output, "report.txt"), report);
                return 0;
            }
            var app = new Application();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xaml"));
            var resources = source.Root!.Element(presentation + "Application.Resources")!
                .Element(presentation + "ResourceDictionary")!;
            resources.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
            resources.SetAttributeValue(XNamespace.Xmlns + "services", "clr-namespace:DropSendTo.Services;assembly=DropSendTo");
            app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString(), new ParserContext
            {
                BaseUri = new Uri("pack://application:,,,/DropSendTo;component/")
            });
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            new ConfigService().Save(new AppConfig());
            var main = new MainWindow();
            new WindowInteropHelper(main).EnsureHandle();
            var configField = typeof(MainWindow).GetField("_config", PrivateInstance)!;
            AppConfig ReadConfig() => (AppConfig)configField.GetValue(main)!;
            var apply = typeof(MainWindow).GetMethod("ApplySetting", PrivateInstance)!;
            void Apply(SettingsOption option, object value) => apply.Invoke(main, new[] { (object)option, value });
            var execute = typeof(MainWindow).GetMethod("ExecuteSettingsActionAsync", PrivateInstance)!;
            var window = new SettingsWindow(ReadConfig, () => false, Apply,
                action => (Task)execute.Invoke(main, new object[] { action })!)
            {
                Owner = main,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            typeof(MainWindow).GetField("_settingsWindow", PrivateInstance)!.SetValue(main, window);
            try
            {
                window.Show();
                Pump();
                Assert(main.ContextMenu.Items.OfType<MenuItem>().Count() == 6, "Quick menu contains six actions including About");
                Assert(main.IsEnabled, "Settings do not disable MainWindow");

                // Exercise the actual tray mouse-event handler with isolated settings, without a physical shell click.
                typeof(MainWindow).GetMethod("MinimizeWindowToTray", PrivateInstance)!.Invoke(main, null);
                Pump();
                Assert(!main.IsVisible, "MainWindow is hidden in the tray");
                NotifyTrayClick("Right");
                Pump();
                Assert(main.ContextMenu.IsOpen, "Tray right-click opens the shared menu");
                var aboutMenu = (MenuItem)main.FindName("AboutMenuItem");
                aboutMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                main.ContextMenu.IsOpen = false;
                Pump();
                var aboutField = typeof(MainWindow).GetField("_aboutWindow", PrivateInstance)!;
                var about = (Window)aboutField.GetValue(main)!;
                Assert(about.IsVisible && ReferenceEquals(about.Owner, main) && main.IsEnabled,
                    "Tray About is visible, owned and modeless");
                aboutMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(ReferenceEquals(about, aboutField.GetValue(main)), "About menu re-entry reuses the window");
                Capture(about, Path.Combine(output, "about-tray.png"));
                about.Close();
                Pump();
                Assert(aboutField.GetValue(main) == null, "Closing About releases the window");
                NotifyTrayClick("Left");
                Pump();
                Assert(main.IsVisible, "Tray left-click restores MainWindow");
                report.Add("PASS: simulated tray mouse events open a modeless About, reuse it, close it and restore MainWindow");

                // Exercise a real persistence failure after destructive in-memory edits.
                Apply(SettingsOption.LayerCount, 5);
                ReadConfig().Layers[4].Name = "save-failure-sentinel";
                Apply(SettingsOption.HideEmptySlotNames, false);
                string backupPath = Path.Combine(output, "data", "DropSendTo", "config.json.bak");
                using (var backupLock = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bool failed = false;
                    try { Apply(SettingsOption.LayerCount, 4); }
                    catch (TargetInvocationException ex) when (ex.InnerException is IOException) { failed = true; }
                    Assert(failed, "Locked backup causes a real save failure");
                    Assert(ReadConfig().Layers.Count == 5 && ReadDisk(output).Layers.Count == 5,
                        "Failed tab reduction restores both runtime and saved settings");
                    Assert(ReadConfig().Layers[4].Name == "save-failure-sentinel", "Failed reduction retains slots and tab names");
                    foreach (var failedOption in new (SettingsOption Option, object Value)[]
                    {
                        (SettingsOption.Theme, AppTheme.Light), (SettingsOption.Language, AppLanguage.English),
                        (SettingsOption.AlwaysOnTop, false), (SettingsOption.KeyboardPlacement, 2),
                        (SettingsOption.MousePlacement, 1), (SettingsOption.SearchPlacement, 2)
                    })
                    {
                        try { Apply(failedOption.Option, failedOption.Value); throw new Exception("Expected persistence failure"); }
                        catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
                        Assert(ReadConfig().Theme == AppTheme.Dark && ThemeService.GetCurrentTheme() == AppTheme.Dark &&
                            ReadConfig().Language == AppLanguage.Japanese && main.Topmost &&
                            ReadConfig().KeyboardPlacementMode == WindowPlacementMode.Fixed &&
                            ReadConfig().MousePlacementFollowsKeyboard &&
                            ReadConfig().SearchPlacementMode == SearchOverlayPlacementMode.Fixed,
                            $"{failedOption.Option} restores runtime services and configuration");
                    }
                    try
                    {
                        typeof(MainWindow).GetMethod("ApplyCustomSlotSize", PrivateInstance)!.Invoke(main,
                            new object[] { new CustomSlotSizeOptions { SlotHeight = 60, TitleFontSize = 20 } });
                        throw new Exception("Expected detail persistence failure");
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
                    Assert(ReadConfig().SlotSize == SlotSize.Medium, "Failed detail change restores custom sizing");
                }
                Apply(SettingsOption.HideEmptySlotNames, true);
                Assert(ReadDisk(output).Layers.Count == 5 && ReadDisk(output).Layers[4].Name == "save-failure-sentinel",
                    "An unrelated save cannot commit a previously failed tab reduction");
                Apply(SettingsOption.LayerCount, 4);
                report.Add("PASS: real backup-lock failures restore tabs, theme, language, topmost, placement and custom sizing without leaking into a later save");

                void NotifyTrayClick(string button)
                {
                    var handler = typeof(MainWindow).GetMethod("OnNotifyIconMouseClick", PrivateInstance)!;
                    var eventType = handler.GetParameters()[1].ParameterType;
                    var buttonType = eventType.GetConstructors().Single().GetParameters()[0].ParameterType;
                    var args = Activator.CreateInstance(eventType, Enum.Parse(buttonType, button), 1, 0, 0, 0);
                    handler.Invoke(main, new object?[] { null, args });
                }

                // Check persistence through the real MainWindow and ConfigService paths.
                Apply(SettingsOption.KeyboardPlacement, 0);
                Apply(SettingsOption.MousePlacement, -1);
                Apply(SettingsOption.MousePlacement, 0);
                var disk = ReadDisk(output);
                Assert(!disk.MousePlacementFollowsKeyboard && disk.MousePlacementMode == WindowPlacementMode.Fixed,
                    "Detaching from keyboard saves even when the placement mode is unchanged");
                Apply(SettingsOption.SearchPlacement, 2);
                Apply(SettingsOption.SearchPlacement, -1);
                Assert(ReadDisk(output).SearchPlacementFollowsKeyboard, "Search follow-keyboard setting is saved");
                Apply(SettingsOption.SlotLayout, new SlotLayoutSelection(3, 4));
                Assert(ReadDisk(output).SlotRows == 3 && ReadDisk(output).SlotColumns == 4, "Grid is saved");
                Apply(SettingsOption.LayerCount, 5);
                Assert(ReadDisk(output).Layers.Count == 5, "Tab count is saved");
                Apply(SettingsOption.EmacsNavigation, true);
                Apply(SettingsOption.ViNavigation, true);
                Apply(SettingsOption.HideEmptySlotNames, true);
                Apply(SettingsOption.AlwaysOnTop, false);
                Apply(SettingsOption.StartupBehavior, StartupWindowBehavior.StartInTray);
                Apply(SettingsOption.MacroConcurrency, MacroConcurrencyMode.SuspendAndResume);
                disk = ReadDisk(output);
                Assert(disk.EnableEmacsNavigation && disk.EnableViNavigation && disk.HideEmptySlotNames && !disk.AlwaysOnTop &&
                    disk.StartupBehavior == StartupWindowBehavior.StartInTray &&
                    disk.MacroConcurrencyMode == MacroConcurrencyMode.SuspendAndResume, "Simple choices persist");
                Assert(!main.Topmost, "Always-on-top changes the real window");
                report.Add("PASS: quick menu, modeless ownership, persistence and runtime reflection");

                foreach (var language in Enum.GetValues<AppLanguage>())
                {
                    Apply(SettingsOption.Language, language);
                    foreach (var theme in Enum.GetValues<AppTheme>())
                    {
                        Apply(SettingsOption.Theme, theme);
                        foreach (var small in new[] { false, true })
                        {
                            window.Width = small ? 660 : 860;
                            window.Height = small ? 460 : 700;
                            for (int category = 0; category < 5; category++)
                            {
                                window.SelectCategory(category);
                                Pump();
                                window.UpdateLayout();
                                Assert(((ScrollViewer)window.FindName("PageScroll")).ViewportWidth > 200,
                                    "Settings content has a usable viewport");
                                Capture(window, Path.Combine(output, $"{language}-{theme}-{(small ? "small" : "normal")}-{category}.png"));
                            }
                        }
                    }
                }
                report.Add("PASS: 40 WPF renders (5 categories × 2 languages × 2 themes × 2 sizes)");

                Apply(SettingsOption.Language, AppLanguage.Japanese);
                Apply(SettingsOption.Theme, AppTheme.Dark);
                window.Width = 860;
                window.Height = 700;
                window.SelectCategory(0);
                var scroll = (ScrollViewer)window.FindName("PageScroll");
                Pump();
                scroll.ScrollToBottom();
                Pump();
                Assert(scroll.VerticalOffset > 0, "Display settings can be scrolled to the last controls");
                Capture(window, Path.Combine(output, "Japanese-Dark-display-bottom.png"));

                // Use actual routed clicks and a Dispatcher pump to verify awaitable modeless details.
                foreach (var action in new[]
                {
                    SettingsAction.Prefix, SettingsAction.SearchHotkey, SettingsAction.MouseGestures,
                    SettingsAction.CustomSlotSize, SettingsAction.LayerNames, SettingsAction.AppLayers,
                    SettingsAction.ShowLayerPreferences, SettingsAction.StartupSlots, SettingsAction.MinimizeTriggers
                })
                {
                    int category = action == SettingsAction.CustomSlotSize ? 0 :
                        action is SettingsAction.Prefix or SettingsAction.SearchHotkey or SettingsAction.MouseGestures ? 1 :
                        action is SettingsAction.StartupSlots or SettingsAction.MinimizeTriggers ? 3 : 2;
                    window.SelectCategory(category);
                    var button = Descendants<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == action.ToString());
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump();
                    Assert(main.IsEnabled && window.IsEnabled, $"{action} is modeless");
                    Assert(!((StackPanel)window.FindName("Pages")).IsEnabled, "Settings editing pauses while detail is open");
                    var detail = window.OwnedWindows.Cast<Window>().Single();
                    detail.Close();
                    PumpUntil(() => ((StackPanel)window.FindName("Pages")).IsEnabled);
                    Assert(string.IsNullOrEmpty(((TextBlock)window.FindName("StatusText")).Text), $"{action} closes without an error");
                }
                report.Add("PASS: all nine detail windows open and close modelessly; editing resumes");

                // Ensure modeless OK paths do not assign DialogResult and throw.
                foreach (var action in new[] { SettingsAction.SearchHotkey, SettingsAction.CustomSlotSize, SettingsAction.MinimizeTriggers })
                {
                    var task = (Task)execute.Invoke(main, new object[] { action })!;
                    Pump();
                    var detail = window.OwnedWindows.Cast<Window>().Single();
                    string method = action == SettingsAction.MinimizeTriggers ? "OnSave" : "OnOk";
                    if (detail.GetType().GetMethod(method, PrivateInstance) == null) method = "OnOk";
                    detail.GetType().GetMethod(method, PrivateInstance)!.Invoke(detail, new object[] { detail, new RoutedEventArgs() });
                    PumpUntil(() => task.IsCompleted);
                    task.GetAwaiter().GetResult();
                }
                report.Add("PASS: modeless confirmation paths for search hotkey, custom size and minimize conditions");

                var menuEntry = main.ContextMenu.Items.OfType<MenuItem>().First();
                menuEntry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(ReferenceEquals(window, typeof(MainWindow).GetField("_settingsWindow", PrivateInstance)!.GetValue(main)),
                    "Reopening settings reuses the existing window");
                report.Add("PASS: menu re-entry reuses the settings window");
            }
            finally { window.Close(); main.Close(); app.Shutdown(); }
            File.WriteAllLines(Path.Combine(output, "report.txt"), report);
            return 0;
        }
        catch (Exception ex)
        {
            report.Add(ex.ToString());
            File.WriteAllLines(Path.Combine(output, "report.txt"), report);
            return 1;
        }
    }

    private static AppConfig ReadDisk(string output) => JsonSerializer.Deserialize<AppConfig>(
        File.ReadAllText(Path.Combine(output, "data", "DropSendTo", "config.json")))!;

    private static void VerifyResidentCli(string output, string? executable = null)
    {
        string root = Path.Combine(output, "cli-data");
        string resultPath = Path.Combine(output, "cli-result.json");
        if (File.Exists(resultPath)) File.Delete(resultPath);
        var config = new AppConfig { CurrentLayer = 2 };
        config.Layers[2].Slots[0] = new SlotModel
        {
            Command = Environment.ProcessPath,
            ArgumentsTemplate = $"--cli-target \"{resultPath}\" {{args}}"
        };
        var service = new ConfigService(root);
        service.Save(config);
        string configBefore = File.ReadAllText(service.GetConfigPath());
        using var resident = new SingleInstanceService(@"Global\DropSendTo");
        _ = resident.TryAcquire(); // An existing real resident also satisfies the test's mutex precondition.
        var start = new ProcessStartInfo(executable ?? Path.Combine(AppContext.BaseDirectory, "DropSendTo.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        start.Environment[AppDataPathResolver.StartupProbeEnvironmentVariable] = "1";
        start.Environment[AppDataPathResolver.DataRootEnvironmentVariable] = root;
        string[] arguments = [@"C:\入力 folder\file one.txt", "plain.txt"];
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            Assert(process.WaitForExit(10000), "Transient CLI process exits while the resident mutex is held");
            Assert(process.ExitCode == 0, "CLI startup succeeds");
            Assert(SpinWait.SpinUntil(() =>
            {
                try { return JsonSerializer.Deserialize<string[]>(File.ReadAllText(resultPath))!.SequenceEqual(arguments); }
                catch (Exception ex) when (ex is IOException or JsonException) { return false; }
            }, 5000), "CLI preserves Japanese, spaces and multiple arguments");
            Assert(File.ReadAllText(service.GetConfigPath()) == configBefore, "Transient CLI leaves saved settings unchanged");
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void PumpUntil(Func<bool> completed)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!completed())
        {
            if (DateTime.UtcNow > timeout) throw new TimeoutException("Detail completion timed out.");
            Pump();
            Thread.Sleep(10);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Capture(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
