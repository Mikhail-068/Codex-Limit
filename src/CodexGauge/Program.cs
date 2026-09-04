using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexGauge;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--self-test") return SelfTest.Run(args[1]);
        if (args.Length >= 2 && args[0] == "--probe")
        {
            try
            {
                using var client = new CodexClient();
                var result = client.ReadLimitsAsync().GetAwaiter().GetResult();
                var windows = QuotaParser.Parse(result);
                File.WriteAllText(args[1], JsonSerializer.Serialize(new { ok = windows.Count > 0, checkedAt = DateTimeOffset.UtcNow, windows }, new JsonSerializerOptions { WriteIndented = true }));
                return windows.Count > 0 ? 0 : 2;
            }
            catch (Exception e) { File.WriteAllText(args[1], JsonSerializer.Serialize(new { ok = false, error = e is CodexException ? e.Message : e.GetType().Name })); return 1; }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        if (args.Length >= 2 && args[0] is "--render" or "--smoke")
        {
            var live = args[0] == "--smoke";
            var window = new GaugeWindow(true) { ShowInTaskbar = false };
            window.Loaded += async (_, _) =>
            {
                if (live)
                {
                    window.StartMonitoring();
                    await System.Threading.Tasks.Task.Delay(13500);
                    File.WriteAllText(args[1] + ".json", JsonSerializer.Serialize(new {
                        ok = window.SuccessfulRefreshes >= 2 && window.Face.Fresh,
                        successfulRefreshes = window.SuccessfulRefreshes, lastUpdate = window.LastUpdate,
                        remaining = window.Face.Quota?.Remaining, fresh = window.Face.Fresh,
                        topmost = window.Topmost, transparent = window.AllowsTransparency,
                        frameless = window.WindowStyle == WindowStyle.None, pollSeconds = 10, width = window.Width, height = window.Height
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    var demoValue = args.Length > 2 && double.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 45;
                    window.Face.Quota = new QuotaWindow("demo", "codex", "Codex", demoValue, 10080, DateTimeOffset.Now.AddDays(3));
                    window.Face.Fresh = true; window.Face.Preview = true;
                }
                window.Face.UpdateValue(false); window.UpdateLayout();
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                Capture(window.Face, args[1]); window.Close();
            };
            return app.Run(window);
        }
        using var mutex = new Mutex(true, "Local\\CodexGauge.Widget", out var created);
        if (!created) return 0;
        try { return app.Run(new GaugeWindow()); }
        finally { mutex.ReleaseMutex(); }
    }

    public static void Capture(FrameworkElement visual, string path)
    {
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth * 2, (int)visual.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
