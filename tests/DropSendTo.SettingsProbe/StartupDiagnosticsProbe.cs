using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;
using DropSendTo;
using DropSendTo.Models;
using DropSendTo.Services;

internal static partial class Program
{
    private static int VerifyStartupDiagnostics(string mode, string outputDirectory)
    {
        string output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        string root = Path.Combine(output, "data");
        string configDirectory = Path.Combine(root, "DropSendTo");
        Directory.CreateDirectory(configDirectory);
        Environment.SetEnvironmentVariable(AppDataPathResolver.StartupProbeEnvironmentVariable, "1");
        Environment.SetEnvironmentVariable(AppDataPathResolver.DataRootEnvironmentVariable, root);
        bool future = mode == "future-schema";
        var config = new AppConfig
        {
            Version = future ? AppConfig.CurrentVersion + 1 : AppConfig.CurrentVersion,
            Language = mode == "logger-english" ? AppLanguage.English : AppLanguage.Japanese,
            StartupBehavior = StartupWindowBehavior.StartInTray
        };
        string primary = Path.Combine(configDirectory, "config.json");
        string before = JsonSerializer.Serialize(config);
        File.WriteAllText(primary, before);
        File.WriteAllText(primary + ".bak", before);
        if (!future) File.WriteAllText(Path.Combine(configDirectory, "logs"), "isolated blocked log directory");

        var startupArguments = (StartupEventArgs)Activator.CreateInstance(typeof(StartupEventArgs), nonPublic: true)!;
        typeof(StartupEventArgs).GetField("_args", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(startupArguments, Array.Empty<string>());
        string trace = Path.Combine(output, "stages.txt");
        File.AppendAllText(trace, "Construct application\n");
        var app = new StartupDiagnosticApplication(startupArguments, trace);
        File.AppendAllText(trace, "Initialize resources\n");
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
        File.AppendAllText(trace, "Isolate instance mutex\n");
        // Do not contend with or shut down the user's resident app.
        var instanceField = typeof(App).GetField("_singleInstance", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((SingleInstanceService)instanceField.GetValue(app)!).Dispose();
        instanceField.SetValue(app, new SingleInstanceService("DropSendTo.StartupProbe." + Guid.NewGuid().ToString("N")));
        File.AppendAllText(trace, "Run application\n");
        app.Dispatcher.BeginInvoke(new Action(app.StartDiagnosticStartup));
        int exitCode = app.Run();
        File.AppendAllText(trace, "Application exited\n");
        File.WriteAllText(Path.Combine(output, "notices.txt"), string.Join("\n", app.Notices));
        try
        {
            Assert(app.Failure == null, "The diagnostic harness has no startup exception");
            Assert(app.Notices.Count > 0, "The real startup path requested a user notice");
            if (future)
            {
                Assert(exitCode == 1 && !app.MainWindowStarted, "Unsupported schema stops startup with no resident window");
                Assert(File.ReadAllText(primary) == before && File.ReadAllText(primary + ".bak") == before,
                    "Unsupported schema leaves primary and backup unchanged");
                Assert(app.Notices.Any(text => text.Contains("起動を中止")), "Failure notice explains startup cancellation");
            }
            else
            {
                string expected = mode == "logger-english" ? "Logs cannot be saved" : "ログを保存できません";
                Assert(app.Notices.Any(text => text.Contains(expected)), "Logging notice uses the selected language");
                Assert(exitCode == 0 && app.MainWindowStarted, "Logging failure allows resident startup and normal shutdown");
            }
            File.WriteAllText(Path.Combine(output, "report.txt"), $"PASS: {mode}; startup/notice content; exit={exitCode}");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "report.txt"), ex + "\n" + app.Failure);
            return 1;
        }
    }

    private sealed class StartupDiagnosticApplication(StartupEventArgs startupArguments, string trace) : App
    {
        public List<string> Notices { get; } = new();
        public bool MainWindowStarted { get; private set; }
        public Exception? Failure { get; private set; }

        internal override void ShowStartupNotice(string message, MessageBoxImage image)
        {
            File.AppendAllText(trace, "Startup notice requested\n");
            Notices.Add(message);
        }

        // Resource loading can pump the dispatcher and deliver Application's startup callback early.
        // Defer the real startup until resources and the probe-owned mutex are both installed.
        protected override void OnStartup(StartupEventArgs e) { }

        public void StartDiagnosticStartup()
        {
            try
            {
                // Probe switches are not product CLI arguments. Leave real startup decisions intact.
                File.AppendAllText(trace, "Invoke startup\n");
                base.OnStartup(startupArguments);
                File.AppendAllText(trace, "Startup returned\n");
                if (MainWindow is MainWindow main)
                {
                    MainWindowStarted = true;
                    Dispatcher.BeginInvoke(new Action(main.Close));
                }
            }
            catch (Exception ex)
            {
                Failure = ex;
                Console.Error.WriteLine(ex);
                Shutdown(2);
            }
        }
    }
}
