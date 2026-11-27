// Velopack uses the PleyCompanion repository and the installed package's channel.
// Phase 1 leaves the feed disabled in AppComposition. When enabled, manual checks ask for
// confirmation and automatic checks run only when the corresponding preference is set.

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenClicky.Core.Configuration;
using OpenClicky.Core.Settings;
using Velopack;
using Velopack.Sources;

namespace OpenClicky.Windows.Companion;

public sealed class VelopackAppUpdater : ObservableObject, IAppUpdater, IDisposable
{
    public const string RepositoryUrl = AppIdentity.RepositoryUrl;
    public const string AutomaticallyChecksKey = "openclicky.updates.automaticallyChecks";
    public const string LastCheckKey = "openclicky.updates.lastCheck";
    /// <summary>The version "Skip This Version" was pressed for (Sparkle's SUSkippedVersion).</summary>
    public const string SkippedVersionKey = "openclicky.updates.skippedVersion";

    private readonly Preferences preferences;
    private readonly UpdatePromptSheets? prompts;
    private readonly UpdateManager? updateManager;
    private Timer? dailyTimer;
    private bool isChecking;

    public VelopackAppUpdater(Preferences preferences, UpdatePromptSheets prompts, bool enableUpdateFeed)
    {
        this.preferences = preferences;
        this.prompts = prompts;
        if (!enableUpdateFeed) return;

        try
        {
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
            updateManager = manager.IsInstalled ? manager : null;
        }
        catch (Exception error)
        {
            AppLog.Write($"updates: no update manager ({error.GetType().Name})");
        }
    }

    public bool IsConfigured => updateManager is not null;

    public bool AutomaticallyChecks
    {
        get => IsConfigured && preferences.GetOptionalBool(AutomaticallyChecksKey) == true;
        set
        {
            preferences.Set(AutomaticallyChecksKey, value);
            OnPropertyChanged();
        }
    }

    public string LastCheckDescription
    {
        get
        {
            if (!IsConfigured) return "updates are disabled for this build";
            if (preferences.GetString(LastCheckKey) is not { } raw || !DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastCheck)) return "never checked";
            var local = lastCheck.ToLocalTime();
            var day = local.Date == DateTime.Today ? "today" : local.Date == DateTime.Today.AddDays(-1) ? "yesterday" : local.ToString("d MMM", CultureInfo.CurrentCulture);
            return $"last checked {day} at {local:H:mm}";
        }
    }

    /// <summary>Starts the launch check and the daily one (when automatic checks are on).</summary>
    public void Start()
    {
        if (!IsConfigured) return;
        dailyTimer = new Timer(_ =>
        {
            if (AutomaticallyChecks) _ = CheckAsync(applyNow: false);
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromDays(1));
    }

    public void CheckNow() => _ = CheckAsync(applyNow: true);

    private async Task CheckAsync(bool applyNow)
    {
        if (updateManager is null || isChecking) return;
        isChecking = true;
        (Action<int> Report, Action Close)? downloading = null;
        try
        {
            var update = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
            preferences.Set(LastCheckKey, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            OnPropertyChanged(nameof(LastCheckDescription));
            if (update is null)
            {
                AppLog.Write("updates: up to date");
                if (applyNow && prompts is not null)
                {
                    await prompts.TellAsync("You're up to date!", $"{AppIdentity.Name} {updateManager.CurrentVersion} is the newest version available.").ConfigureAwait(false);
                }
                return;
            }
            var version = update.TargetFullRelease.Version.ToString();
            if (applyNow)
            {
                // Asked even about a skipped version: they pressed Check Now (Sparkle's rule).
                if (prompts is not null)
                {
                    var choice = await prompts.AskToInstallAsync(new UpdateOffer(version, updateManager.CurrentVersion?.ToString() ?? "an older version",
                                                                                 update.TargetFullRelease.NotesMarkdown ?? "")).ConfigureAwait(false);
                    AppLog.Write($"updates: {version} available; {choice.ToString().ToLowerInvariant()}");
                    if (choice == UpdateChoice.Skip) preferences.Set(SkippedVersionKey, version);
                    if (choice != UpdateChoice.Install) return;
                    downloading = await prompts.ShowDownloadingAsync(version).ConfigureAwait(false);
                }
                await updateManager.DownloadUpdatesAsync(update, downloading?.Report).ConfigureAwait(false);
                downloading?.Report(100);
                updateManager.ApplyUpdatesAndRestart(update.TargetFullRelease);
            }
            else
            {
                if (preferences.GetString(SkippedVersionKey) == version)
                {
                    AppLog.Write($"updates: {version} available but skipped");
                    return;
                }
                AppLog.Write($"updates: {version} available; downloading");
                await updateManager.DownloadUpdatesAsync(update).ConfigureAwait(false);
                // Installed when PleyCompanion quits.
                updateManager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
            }
        }
        catch (Exception error)
        {
            AppLog.Write($"updates: check failed ({error.GetType().Name}: {error.Message})");
            downloading?.Close();
            if (applyNow && prompts is not null)
            {
                await prompts.TellAsync("Update failed", $"{AppIdentity.Name} couldn't check for or download the update. Check your connection and try again.").ConfigureAwait(false);
            }
        }
        finally
        {
            isChecking = false;
        }
    }

    public void Dispose() => dailyTimer?.Dispose();
}
