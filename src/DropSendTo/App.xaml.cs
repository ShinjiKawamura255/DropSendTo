using System;
using System.Linq;
using System.Windows;
using DropSendTo.Services;
using DropSendTo.Models;

namespace DropSendTo;

public partial class App : Application
{
    private readonly ConfigService _configService = new();
    private readonly LauncherService _launcher = new();
    private readonly LoggerService _logger = LoggerService.Instance;

    public App()
    {
        this.DispatcherUnhandledException += (_, e) =>
        {
            _logger.Error($"Unhandled (UI): {e.Exception}");
            MessageBox.Show(
                e.Exception.Message,
                "DropSendTo Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            _logger.Error($"Unhandled (Domain): {e.ExceptionObject}");
            MessageBox.Show(e.ExceptionObject?.ToString() ?? "Unhandled exception",
                "DropSendTo Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var cfg = _configService.LoadOrCreate();
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Length > 0)
            {
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
                        MessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("No registered slot found. Please register a slot.", "DropSendTo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Startup error: {ex}");
        }

        var win = new MainWindow();
        win.Show();
    }
}
