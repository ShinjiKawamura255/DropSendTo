using System;
using DropSendTo.Models;
using DropSendTo.Services;
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
    private readonly HorizontalMouseWheelService _horizontalMouseWheelService = new();

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
        bool ownsInstance = _singleInstance.TryAcquire();
        var startup = StartupLaunchCoordinator.Start(ownsInstance, e.Args,
            _configService.LoadOrCreate, (slot, paths) => _launcher.Launch(slot, paths));
        if (startup.Status == StartupLaunchStatus.AlreadyRunning)
        {
            _logger.Warn("Another DropSendTo instance is already running.");
            WpfMessageBox.Show("DropSendTo はすでに起動しています。既存のウィンドウを確認してください。", "DropSendTo", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (startup.Error != null)
        {
            _logger.Error("CLI launch failed.");
            WpfMessageBox.Show(startup.Error, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        if (startup.Status is StartupLaunchStatus.CliLaunched or StartupLaunchStatus.CliFailed)
        {
            _logger.Info($"Transient CLI launch completed (status={startup.Status}, args={e.Args.Length}).");
            Shutdown();
            return;
        }

        base.OnStartup(e);
        _horizontalMouseWheelService.Start();

        _logger.Info($"DropSendTo starting (args={e.Args.Length}).");

        AppConfig cfg;
        try
        {
            _logger.CleanupOldLogs();
            cfg = _configService.LoadOrCreate();
            _logger.Info($"Configuration ready (path={_configService.GetConfigPath()}, rows={cfg.SlotRows}, cols={cfg.SlotColumns}, currentLayer={cfg.CurrentLayer + 1}).");
        }
        catch (Exception ex)
        {
            _logger.Error($"Configuration startup failed: {ex}");
            ShowStartupNotice(
                "設定を読み込めないため起動を中止します。アクセス権や設定の対応バージョンを確認してください。\n" +
                "Startup stopped. Check access rights and the configuration version.\n" + ex.Message,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        try { ThemeService.ApplyTheme(cfg.Theme); }
        catch (Exception ex) { _logger.Error($"Startup theme error: {ex}"); }

        if (!_logger.IsAvailable)
        {
            var message = cfg.Language == AppLanguage.English
                ? "Logs cannot be saved. The app will continue. Check the log folder's permissions and available disk space."
                : "ログを保存できません。アプリは続行します。ログフォルダーのアクセス権や空き容量を確認してください。";
            ShowStartupNotice(message, MessageBoxImage.Warning);
        }

        try
        {
            var win = new MainWindow();
            win.Show();
            _logger.Info("Main window shown.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Main window startup failed: {ex}");
            ShowStartupNotice(ex.Message, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal virtual void ShowStartupNotice(string message, MessageBoxImage image) =>
        WpfMessageBox.Show(message, "DropSendTo", MessageBoxButton.OK, image);

    protected override void OnExit(ExitEventArgs e)
    {
        _horizontalMouseWheelService.Dispose();
        base.OnExit(e);
        _singleInstance.Dispose();
    }
}
