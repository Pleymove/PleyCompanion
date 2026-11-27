using OpenClicky.Core.Configuration;
using OpenClicky.Core.Launcher;
using OpenClicky.Core.Platform;

namespace OpenClicky.Windows.Platform;

internal sealed class LauncherApplications(IDesktopActionExecutor desktop, Preferences preferences)
{
    private const string RecentKey = "launcher.recentApplications";
    private DesktopAppTarget[] catalog = [];
    public async Task Load() => catalog = await Task.Run(() => DesktopActionExecutor.InstalledApplications().ToArray());

    public IEnumerable<DesktopAppTarget> Search(string query)
    {
        var recent = preferences.GetStringArray(RecentKey) ?? [];
        return catalog.Select(app => (app, score: LauncherSearch.Score(app.DisplayName, query),
                recent: Array.IndexOf(recent, app.Target)))
            .Where(hit => hit.score > 0)
            .OrderByDescending(hit => hit.score)
            .ThenBy(hit => hit.recent < 0 ? int.MaxValue : hit.recent)
            .ThenBy(hit => hit.app.DisplayName.Length)
            .Take(12).Select(hit => hit.app);
    }

    public void Launch(DesktopAppTarget app)
    {
        if (!desktop.LaunchApplication(app)) throw new InvalidOperationException("Application indisponible.");
        var recent = preferences.GetStringArray(RecentKey) ?? [];
        preferences.Set(RecentKey, recent.Prepend(app.Target).Distinct(StringComparer.OrdinalIgnoreCase).Take(32));
    }
}
