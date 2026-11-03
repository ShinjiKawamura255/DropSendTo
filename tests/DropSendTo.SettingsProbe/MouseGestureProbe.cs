using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using DropSendTo;
using DropSendTo.Models;
using DropSendTo.Services;

internal static partial class Program
{
    private static int VerifyMouseGestures(string directory)
    {
        string output = Path.GetFullPath(directory);
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable(AppDataPathResolver.StartupProbeEnvironmentVariable, "1");
        Environment.SetEnvironmentVariable(AppDataPathResolver.DataRootEnvironmentVariable, Path.Combine(output, "data"));
        var report = new List<string>();
        Application? app = null;
        MainWindow? main = null;
        try
        {
            report.Add($"App module={typeof(MainWindow).Assembly.ManifestModule.ModuleVersionId}; runtime={Environment.Version}");
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xaml"));
            var resources = source.Root!.Element(presentation + "Application.Resources")!
                .Element(presentation + "ResourceDictionary")!;
            resources.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
            resources.SetAttributeValue(XNamespace.Xmlns + "services", "clr-namespace:DropSendTo.Services;assembly=DropSendTo");
            app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString(), new ParserContext
            { BaseUri = new Uri("pack://application:,,,/DropSendTo;component/") });
            var config = new AppConfig
            {
                MousePlacementMode = WindowPlacementMode.MouseFollow,
                MousePlacementFollowsKeyboard = false,
                MouseGestureSuppressDuringPresentation = false
            };
            new ConfigService().Save(config);
            main = new MainWindow();
            var handle = new WindowInteropHelper(main).EnsureHandle();
            int awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(handle));
            report.Add($"Window DPI awareness={awareness} (expected 2=per-monitor)");
            Assert(awareness == 2, "Application manifest must enable per-monitor DPI awareness");
            var service = (ShortcutService)typeof(MainWindow).GetField("_shortcutService", PrivateInstance)!.GetValue(main)!;
            var hook = typeof(ShortcutService).GetField("_mouseHook", PrivateInstance)!.GetValue(service)!;
            var worker = (Dispatcher)typeof(MouseHookThread).GetField("_dispatcher", PrivateInstance)!.GetValue(hook)!;
            Assert(worker.Thread != Dispatcher.CurrentDispatcher.Thread, "Native mouse hook owns an independent message pump");
            long ticks = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc).Ticks;
            var detector = new MouseGestureDetector(() => new DateTime(Interlocked.Read(ref ticks), DateTimeKind.Utc));
            typeof(ShortcutService).GetField("_mouseGestureDetector", PrivateInstance)!.SetValue(service, detector);
            var move = typeof(ShortcutService).GetMethod("HandleMouseGesture", PrivateInstance)!;
            var dataType = move.GetParameters()[0].ParameterType;
            var pointField = dataType.GetField("pt")!;
            var xField = pointField.FieldType.GetField("x")!;
            var yField = pointField.FieldType.GetField("y")!;
            var pending = typeof(ShortcutService).GetField("_pendingMouseGesture", PrivateInstance)!;
            int shows = 0, hides = 0;
            System.Drawing.Point observedCenter = default;
            service.MouseGestureShowRequested += (_, e) => { shows++; observedCenter = e.Center; };
            service.MouseGestureHideRequested += (_, _) => hides++;
            var screens = new List<GestureRect>();
            GestureMonitorCallback collect = (IntPtr monitor, IntPtr _, ref GestureRect rect, IntPtr state) =>
            {
                var info = new GestureMonitorInfo { Size = Marshal.SizeOf<GestureMonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("GetMonitorInfo failed");
                screens.Add(info.Work);
                return true;
            };
            Assert(EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, collect, IntPtr.Zero), "Enumerate actual monitors");
            GC.KeepAlive(collect);
            var originalLeft = config.WindowLeft;
            var originalTop = config.WindowTop;
            int image = 0;
            foreach (var screen in screens)
            {
                int x = (screen.Left + screen.Right) / 2, y = (screen.Top + screen.Bottom) / 2;
                for (int step = 0; step < 4; step++)
                {
                    bool show = step % 2 == 0;
                    int before = show ? shows : hides;
                    Interlocked.Add(ref ticks, TimeSpan.FromMilliseconds(400).Ticks);
                    var operation = worker.InvokeAsync(() =>
                    {
                        for (int i = 0; i <= 4000; i++)
                        {
                            double angle = (show ? 1 : -1) * 2 * Math.PI * i / 1000;
                            object point = Activator.CreateInstance(pointField.FieldType)!;
                            xField.SetValue(point, x + (int)(100 * Math.Cos(angle)));
                            yField.SetValue(point, y + (int)(100 * Math.Sin(angle)));
                            object data = Activator.CreateInstance(dataType)!;
                            pointField.SetValue(data, point);
                            Interlocked.Add(ref ticks, TimeSpan.TicksPerMillisecond);
                            move.Invoke(service, new[] { data });
                            if (pending.GetValue(service) != null) return;
                        }
                        throw new InvalidOperationException("Gesture did not produce a UI request");
                    });
                    // Do not pump the UI: worker recognition must finish independently.
                    Assert(operation.Task.Wait(TimeSpan.FromSeconds(5)), "Gesture worker completed while UI was paused");
                    operation.Task.GetAwaiter().GetResult();
                    Assert((show ? shows : hides) == before, "UI delivery remains asynchronous");
                    PumpUntil(() => (show ? shows : hides) == before + 1);
                    Assert(main.IsVisible == show, "Show/hide/re-show updates actual native window visibility");
                    if (show)
                    {
                        Assert(Math.Abs(observedCenter.X - x) <= 3 && Math.Abs(observedCenter.Y - y) <= 3,
                            "Queued event preserves physical circle center");
                        Assert(GetWindowRect(handle, out var rect), "Read physical window bounds");
                        var dpi = VisualTreeHelper.GetDpi(main);
                        var expected = CursorWindowPlacementService.CalculatePosition(new Point(x, y),
                            new Size(main.ActualWidth, main.ActualHeight),
                            new ScreenBounds(screen.Left, screen.Top, screen.Right - screen.Left, screen.Bottom - screen.Top),
                            dpi, CursorWindowPlacement.MouseFollow);
                        Assert(Math.Abs(rect.Left - expected.X) <= 4 && Math.Abs(rect.Top - expected.Y) <= 4,
                            $"Physical position matches center: actual=({rect.Left},{rect.Top}), expected={expected}");
                        report.Add($"PASS monitor=({screen.Left},{screen.Top}) dpi={dpi.DpiScaleX * 100}% step={step} center=({x},{y}) window=({rect.Left},{rect.Top})");
                        Capture(main, Path.Combine(output, $"gesture-{image++}.png"));
                    }
                }
            }
            var saved = new ConfigService().LoadOrCreate();
            Assert(saved.WindowLeft == originalLeft && saved.WindowTop == originalTop,
                "Transient gesture positions do not overwrite saved fixed position");
            report.Add($"PASS {screens.Count} actual monitors; {shows} shows and {hides} hides; no restart; isolated settings");
            File.WriteAllLines(Path.Combine(output, "report.txt"), report);
            return 0;
        }
        catch (Exception ex)
        {
            report.Add(ex.ToString());
            File.WriteAllLines(Path.Combine(output, "report.txt"), report);
            return 1;
        }
        finally
        {
            main?.Close();
            app?.Shutdown();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GestureRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct GestureMonitorInfo { public int Size; public GestureRect Monitor, Work; public uint Flags; }
    private delegate bool GestureMonitorCallback(IntPtr monitor, IntPtr dc, ref GestureRect rect, IntPtr state);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, GestureMonitorCallback callback, IntPtr state);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref GestureMonitorInfo info);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out GestureRect rect);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
}
