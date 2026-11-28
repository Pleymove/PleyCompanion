using OpenClicky.Core.Configuration;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Platform;

namespace OpenClicky.Core.Speech;

/// <summary>A Parakeet TDT model shared by dictation, the assistant and the microphone test.</summary>
public sealed class ParakeetDictation : DownloadableVoiceModel
{
    public static ParakeetDictation? Shared { get; set; }
    public const string HelperFlag = "--openclicky-parakeet-helper";

    public static ModelDownload ModelDownload => VoiceModelCatalog.ParakeetV3.Package.Download;
    public const string DownloadSizeText = ParakeetFacts.DownloadSizeText;
    public static IReadOnlySet<string> LanguageCodes => ParakeetFacts.LanguageCodes;
    public static bool Hears(string? language) => ParakeetFacts.Hears(language);
    public static bool IsInstalled => ParakeetFacts.IsInstalled;
    public static bool IsInstalledIn(string folder) => ParakeetFacts.IsInstalledIn(folder);

    public static LocalSttModel SelectedModel => VoiceModelCatalog.SelectedParakeet(OpenClickyConfiguration.Settings.ParakeetModel);

    public static ParakeetDictation? Current => VoiceModelRuntime.Shared?.FindRecognizer(SelectedModel) ?? Shared;

    public static string? UnavailableReason(string? language)
    {
        var model = SelectedModel;
        var installed = VoiceModelRuntime.Shared?.FindRecognizer(model)?.IsModelInstalled
            ?? (model == VoiceModelCatalog.ParakeetV3 && ParakeetFacts.IsInstalled);
        return DictationEngineResolver.ParakeetUnavailableReason(installed, language, model);
    }

    public ParakeetDictation(IProcessRunner processes, string executablePath, IMainThread? mainThread = null)
        : this(VoiceModelCatalog.ParakeetV3, processes, executablePath, mainThread) { }

    public ParakeetDictation(LocalSttModel model, IProcessRunner processes, string executablePath, IMainThread? mainThread)
        : base(model.Info, model.Package, HelperFlag, processes, executablePath, mainThread) { }

    public void PrepareForTake() => WarmUp(() => TranscribeAsync([]));

    /// <summary>Decode 16 kHz mono PCM16 audio.</summary>
    public async Task<string> TranscribeAsync(byte[] pcm16, CancellationToken cancellationToken = default)
    {
        var request = new ParakeetEngineRequest(Convert.ToBase64String(pcm16), Package.ModelFolder);
        var reply = await RunAsync<ParakeetEngineRequest, ParakeetEngineReply>(request,
            new ParakeetException(ParakeetException.NotInstalledMessage), cancellationToken).ConfigureAwait(false);
        return reply.Text;
    }
}
