using System;
using System.Linq;
using DropSendTo.Services;
using DropSendTo.Models;
using System.Windows;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace DropSendTo;

public partial class App : WpfApplication
{
    private readonly ConfigService _configService = new();
    private readonly LauncherService _launcher = new();
    private readonly LoggerService _logger = LoggerService.Instance;
    private readonly SingleInstanceService _singleInstance = new(@"Global\DropSendTo");

    public App()
    {
        this.DispatcherUnhandledException += (_, e) =>
        {
            _logger.Error($"Unhandled (UI): {e.Exception}");
            WpfMessageBox.Show(
                e.Exception.Message,
                "DropSendTo Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            _logger.Error($"Unhandled (Domain): {e.ExceptionObject}");
            WpfMessageBox.Show(e.ExceptionObject?.ToString() ?? "Unhandled exception",
                "DropSendTo Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!_singleInstance.TryAcquire())
        {
            _logger.Warn("Another DropSendTo instance is already running.");
            WpfMessageBox.Show("DropSendTo はすでに起動しています。既存のウィンドウを確認してください。", "DropSendTo", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _logger.Info($"DropSendTo starting (args={e.Args.Length}).");

        try
        {
            _logger.CleanupOldLogs();
            var cfg = _configService.LoadOrCreate();
            _logger.Info($"Configuration ready (path={_configService.GetConfigPath()}, rows={cfg.SlotRows}, cols={cfg.SlotColumns}, currentLayer={cfg.CurrentLayer + 1}).");
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Length > 0)
            {
                _logger.Info($"CLI launch requested with {args.Length} argument(s).");
                // Choose first registered slot in current layer, otherwise across layers
                var layer = Math.Clamp(cfg.CurrentLayer, 0, 3);
                var slot = cfg.Layers[layer].Slots.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Command))
                           ?? cfg.Layers.SelectMany(l => l.Slots).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Command));
                if (slot != null)
                {
                    var result = _launcher.Launch(slot, args);
                    if (!result.Success)
                    {
                        _logger.Error($"Launch failed: {result.Message}");
                        WpfMessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        // Fall through to show UI for manual drop
                    }
                    else
                    {
                        _logger.Info($"Launched via CLI with {args.Length} arg(s)");
                        Shutdown();
                        return;
                    }
                }
                else
                {
                    _logger.Warn("No registered slot found for CLI launch");
                    WpfMessageBox.Show("No registered slot found. Please register a slot.", "DropSendTo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Startup error: {ex}");
        }

        var win = new MainWindow();
        win.Show();
        _logger.Info("Main window shown.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        _singleInstance.Dispose();
    }
}
