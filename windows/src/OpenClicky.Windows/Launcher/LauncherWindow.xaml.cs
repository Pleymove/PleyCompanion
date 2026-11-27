using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using OpenClicky.Core.Configuration;
using OpenClicky.Core.Launcher;
using OpenClicky.Core.Platform;
using OpenClicky.Windows.Platform;

namespace OpenClicky.Windows.Launcher;

public partial class LauncherWindow : Window
{
    private sealed record Row(string Name, string Detail, string Glyph, Action Run, BitmapSource? Icon = null, Action? Reveal = null);
    private readonly LauncherApplications apps;
    private readonly LauncherFiles files;
    private readonly LocalToolRegistry tools;
    private readonly IScreens screens;
    private readonly Preferences preferences;
    private readonly Dictionary<string, BitmapSource?> icons = new();
    private readonly nint handle;
    private CancellationTokenSource? pending;
    private bool filesOnly;
    private bool registered;
    private bool closing;
    private nint previous;
    private string shortcutLabel = "";
    private static readonly (string Name, uint Mod, uint Key)[] Bindings =
        [("Alt + Espace", 1, 32), ("Ctrl + Espace", 2, 32), ("Ctrl + Maj + P", 6, 0x50)];

    internal LauncherWindow(IScreens screens, IDesktopActionExecutor desktop, Preferences preferences, LocalToolRegistry registry)
    {
        InitializeComponent();
        this.screens = screens;
        this.preferences = preferences;
        tools = registry;
        apps = new(desktop, preferences);
        files = new(() => new[] { "desktop", "documents", "downloads" }.Select(desktop.KnownFolderPath));
        tools.Register(new("files", "Rechercher des fichiers", () => { filesOnly = true; Query.Clear(); Refresh(); Query.Focus(); }));
        handle = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(handle).AddHook(Messages);
        Bind(Math.Clamp(preferences.GetInt("launcher.shortcut"), 0, Bindings.Length - 1), false);
        var menu = new ContextMenu();
        for (int i = 0; i < Bindings.Length; i++)
        {
            int choice = i;
            var item = new MenuItem { Header = Bindings[i].Name };
            item.Click += (_, _) => Bind(choice, true);
            menu.Items.Add(item);
        }
        Shortcut.Click += (_, _) => { menu.PlacementTarget = Shortcut; menu.IsOpen = true; };
        PreviewKeyDown += OnKey;
        Query.TextChanged += (_, _) => Refresh();
        Results.MouseDoubleClick += (_, _) => Execute(false);
        Closed += (_, _) => { Cancel(); UnregisterHotKey(handle, 1); };
    }

    internal async Task Load()
    {
        try { await apps.Load(); if (IsVisible) Refresh(); }
        catch (Exception e) { AppLog.Write($"launcher catalog: {e.Message}"); }
    }

    private void Bind(int choice, bool persist)
    {
        if (registered && shortcutLabel == Bindings[choice].Name) return;
        var binding = Bindings[choice];
        if (registered) UnregisterHotKey(handle, 1);
        registered = RegisterHotKey(handle, 1, binding.Mod | 0x4000, binding.Key);
        shortcutLabel = binding.Name;
        if (registered && persist) preferences.Set("launcher.shortcut", choice);
        Footer();
    }
    private void Footer() => Status.Text = registered
        ? $"↑ ↓  Parcourir     Entrée  Ouvrir     Ctrl + Entrée  Emplacement     ·     {shortcutLabel}"
        : $"{shortcutLabel} indisponible (Windows ou Raycast). Choisissez un autre raccourci.";
    internal bool HasShortcut => registered;

    private nint Messages(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x312 && wParam == 1) { handled = true; Toggle(); }
        return 0;
    }

    internal void Toggle()
    {
        if (closing) return;
        if (IsVisible) { Dismiss(); return; }
        previous = NativeMethods.GetForegroundWindow();
        var monitor = MonitorFromWindow(previous, 2);
        var screen = screens.All().FirstOrDefault(s => s.MonitorHandle == monitor) ?? screens.CursorScreen();
        if (screen != null)
        {
            var work = screen.WorkArea;
            int w = Math.Min(work.Width, (int)(640 * screen.Scale));
            int h = Math.Min(work.Height, (int)(428 * screen.Scale));
            SetWindowPos(handle, 0, work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) / 2, w, h, 0x14);
        }
        filesOnly = false;
        Query.Clear();
        Show();
        Activate();
        Query.Focus();
        Footer();
        Refresh();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(90)));
    }

    private void Dismiss()
    {
        if (closing) return;
        closing = true;
        Cancel();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(65));
        fade.Completed += (_, _) => { Hide(); closing = false; };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Cancel()
    {
        pending?.Cancel();
        pending = null;
    }

    private async void Refresh()
    {
        Cancel();
        if (!IsVisible || closing) return;
        var query = Query.Text.Trim();
        Hint.Text = filesOnly ? "Rechercher un fichier ou un dossier…" : "Rechercher une application, un fichier…";
        Hint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Hidden;
        var rows = new List<Row>();
        var appIcons = new List<(int Index, string Path)>();
        if (!filesOnly)
        {
            if (query.Length > 0 && Calculator.Evaluate(query) is { } value)
                rows.Add(new(value, "Calculatrice · Copier le résultat", "\uE8EF", () => Clipboard.SetText(value)));
            foreach (var tool in tools.Tools.OrderByDescending(t => LauncherSearch.Score(t.Name, query)))
                if (LauncherSearch.Score(tool.Name, query) > 0)
                    rows.Add(new(tool.Name, "Commande", "\uE756", () => tools.Execute(tool.Id)));
            foreach (var app in apps.Search(query))
            {
                var path = app.Kind == DesktopAppTargetKind.PackagedApp ? @"shell:AppsFolder\" + app.Target : app.Target;
                if (!icons.TryGetValue(path, out var icon)) appIcons.Add((rows.Count, path));
                rows.Add(new(app.DisplayName, "Application", icon == null ? "\uECAA" : "", () => apps.Launch(app), icon));
            }
        }
        Results.ItemsSource = rows;
        Results.SelectedIndex = rows.Count > 0 ? 0 : -1;
        using var source = new CancellationTokenSource();
        pending = source;
        var token = source.Token;
        try
        {
            foreach (var (index, path) in appIcons)
            {
                var image = await Task.Run(() => ShellIcon.Load(path), token);
                token.ThrowIfCancellationRequested();
                icons[path] = image;
                if (image != null) rows[index] = rows[index] with { Icon = image, Glyph = "" };
            }
            if (appIcons.Count > 0) Results.Items.Refresh();
            if (query.Length < 2) return;
            await Task.Delay(140, token);
            var matches = await files.Search(query, token);
            token.ThrowIfCancellationRequested();
            rows.AddRange(matches.Select(f => new Row(f.Name, $"{f.Kind} · {f.Path}", "\uE8A5", f.Open, Reveal: () => f.Reveal())));
            Results.Items.Refresh();
            if (Results.SelectedIndex < 0 && rows.Count > 0) Results.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!token.IsCancellationRequested) Status.Text = e.Message; }
        finally { if (pending == source) pending = null; }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
        if (e.Key is Key.Up or Key.Down && Results.Items.Count > 0)
        {
            Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Up ? -1 : 1), 0, Results.Items.Count - 1);
            Results.ScrollIntoView(Results.SelectedItem);
            e.Handled = true;
        }
        if (e.Key == Key.Enter && !Shortcut.IsKeyboardFocused)
        {
            Execute(Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            e.Handled = true;
        }
    }
    private void Execute(bool reveal)
    {
        if (Results.SelectedItem is not Row row || closing) return;
        var action = reveal ? row.Reveal : row.Run;
        if (action == null) return;
        try
        {
            if (row.Detail == "Commande" && row.Name == tools.Tools.Single(t => t.Id == "files").Name) { action(); return; }
            Cancel();
            Hide();
            SetForegroundWindow(previous);
            action();
        }
        catch (Exception e) { Show(); Activate(); Status.Text = e.Message; }
    }

    [DllImport("user32")] private static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint key);
    [DllImport("user32")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
}
