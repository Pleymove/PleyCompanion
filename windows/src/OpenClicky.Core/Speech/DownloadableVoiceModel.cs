using CommunityToolkit.Mvvm.ComponentModel;
using OpenClicky.Core.Configuration;
using OpenClicky.Core.Platform;

namespace OpenClicky.Core.Speech;

/// <summary>Download, removal and inference lifecycle shared by the local CPU models.</summary>
public abstract partial class DownloadableVoiceModel : ObservableObject
{
    private readonly IProcessRunner processes;
    private readonly IMainThread? mainThread;
    private readonly SherpaHelperClient helper;
    private readonly SemaphoreSlim inference = new(1, 1);
    private readonly object installationLock = new();
    private CancellationTokenSource? downloadCancellation;
    private volatile bool removing;
    private int warming;

    [ObservableProperty]
    private ModelInstallState state;

    protected DownloadableVoiceModel(VoiceModelInfo info, VoiceModelPackage package, string flag,
        IProcessRunner processes, string executablePath, IMainThread? mainThread)
    {
        Info = info;
        Package = package;
        this.processes = processes;
        this.mainThread = mainThread;
        helper = new SherpaHelperClient(flag, info.Id, processes, executablePath);
        state = package.IsInstalled ? ModelInstallState.Installed : ModelInstallState.NotInstalled;
    }

    public VoiceModelInfo Info { get; }
    public VoiceModelPackage Package { get; }

    public bool IsModelInstalled => !removing && Package.IsInstalled;

    private void SetState(ModelInstallState value)
    {
        if (mainThread is null || mainThread.IsMainThread) State = value;
        else mainThread.Post(() => State = value);
    }

    public void Download()
    {
        lock (installationLock)
        {
            if (removing || downloadCancellation is not null || IsModelInstalled) return;
            var cancellation = new CancellationTokenSource();
            downloadCancellation = cancellation;
            SetState(ModelInstallState.Downloading(0));
            _ = Task.Run(() => InstallAsync(cancellation));
        }
    }

    private async Task InstallAsync(CancellationTokenSource cancellation)
    {
        var outcome = ModelInstallState.Installed;
        try
        {
            await Package.Download.InstallAsync(Package.Folder,
                fraction => SetState(fraction >= 1 ? ModelInstallState.Unpacking : ModelInstallState.Downloading(fraction)),
                processes, cancellation.Token,
                staging => Package.HasRequiredFiles(Path.Combine(staging, Package.ArchiveFolder))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = ModelInstallState.NotInstalled;
        }
        catch (Exception error)
        {
            AppLog.Write($"{Info.Id}: install failed ({error.Message})");
            outcome = ModelInstallState.Failed(error.Message);
        }
        finally
        {
            lock (installationLock)
            {
                downloadCancellation = null;
                cancellation.Dispose();
                SetState(outcome);
            }
        }
    }

    public void CancelDownload()
    {
        lock (installationLock) downloadCancellation?.Cancel();
    }

    public Task RemoveAsync()
    {
        lock (installationLock)
        {
            if (removing || downloadCancellation is not null) return Task.CompletedTask;
            removing = true;
            SetState(ModelInstallState.Removing);
            return Task.Run(RemoveFilesAsync);
        }
    }

    private async Task RemoveFilesAsync()
    {
        var outcome = ModelInstallState.NotInstalled;
        await inference.WaitAsync().ConfigureAwait(false);
        try
        {
            await helper.UnloadAsync().ConfigureAwait(false);
            if (Directory.Exists(Package.Folder)) Directory.Delete(Package.Folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"{Info.Id}: removal failed ({error.Message})");
            outcome = ModelInstallState.Failed("Suppression impossible. " + error.Message);
        }
        finally
        {
            inference.Release();
            lock (installationLock)
            {
                removing = false;
                SetState(outcome);
            }
        }
    }

    protected async Task<TReply> RunAsync<TRequest, TReply>(TRequest request, Exception missingError, CancellationToken cancellationToken)
    {
        await inference.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsModelInstalled) throw missingError;
            return await helper.SendAsync<TRequest, TReply>(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            inference.Release();
        }
    }

    // The helper exits after 180 seconds without a request. A later request restarts it on demand.
    public Task UnloadAsync() => helper.UnloadAsync();

    public void UnloadForMemoryPressure() => _ = UnloadAsync();

    protected void WarmUp(Func<Task> load)
    {
        if (!IsModelInstalled || helper.IsRunning || Interlocked.Exchange(ref warming, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await load().ConfigureAwait(false); }
            catch (Exception error) { AppLog.Write($"{Info.Id}: warm-up failed ({error.Message})"); }
            finally { Interlocked.Exchange(ref warming, 0); }
        });
    }
}
