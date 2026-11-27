using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenClicky.Core.Configuration;
using OpenClicky.Core.Launcher;
using OpenClicky.Windows.Platform;

namespace OpenClicky.Windows.Launcher;

internal static class LauncherProbe
{
    internal static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var screens = new Screens();
        var window = new LauncherWindow(screens, new DesktopActionExecutor(),
            new Preferences(Path.Combine(folder, "probe.json")), new LocalToolRegistry());
        app.Startup += async (_, _) =>
        {
            try
            {
                await window.Load();
                window.Toggle();
                await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                window.Hide();
                var timer = Stopwatch.StartNew();
                window.Toggle();
                await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                double warm = timer.Elapsed.TotalMilliseconds;
                // Select a real installed app regardless of the runner's display language.
                var target = window.Results.Items.Cast<object>().FirstOrDefault(row =>
                    row.GetType().GetProperty("Detail")?.GetValue(row) as string == "Application");
                string? name = target?.GetType().GetProperty("Name")?.GetValue(target) as string;
                if (string.IsNullOrEmpty(name)) throw new InvalidOperationException("Catalogue applicatif vide.");
                timer.Restart();
                window.Query.Text = name;
                window.UpdateLayout();
                if (window.Results.Items.Count == 0) throw new InvalidOperationException("Recherche sans résultat.");
                double search = timer.Elapsed.TotalMilliseconds;
                await Task.Delay(300);
                var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(Path.Combine(folder, "launcher.png"));
                png.Save(output);
                File.WriteAllLines(Path.Combine(folder, "timings.txt"), [
                    $"Ouverture à chaud jusqu’au dispatcher inactif (1 essai) : {warm:F1} ms",
                    $"Saisie du nom et disposition des premiers résultats (1 essai) : {search:F1} ms"
                ]);
                window.Close();
                app.Shutdown(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(folder, "error.txt"), e.ToString());
                app.Shutdown(1);
            }
        };
        return app.Run();
    }
}
