using OpenClicky.Core.Configuration;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Platform;

namespace OpenClicky.Core.Speech;

/// <summary>One lazily loaded helper per installed model, reused by every consumer.</summary>
public sealed class VoiceModelRuntime(IProcessRunner processes, string executablePath, IMainThread? mainThread)
{
    public static VoiceModelRuntime? Shared { get; set; }

    private readonly IReadOnlyDictionary<LocalSttModel, ParakeetDictation> recognition =
        VoiceModelCatalog.Recognition.ToDictionary(model => model, model => new ParakeetDictation(model, processes, executablePath, mainThread));

    private readonly IReadOnlyDictionary<SpeechVoiceChoice, LocalSpeechVoice> synthesis =
        VoiceModelCatalog.Synthesis.ToDictionary(model => model.Choice, model => model.Choice == SpeechVoiceChoice.Kokoro
            ? new KokoroVoice(processes, executablePath, mainThread)
            : new LocalSpeechVoice(model, processes, executablePath, mainThread));

    public ParakeetDictation? FindRecognizer(LocalSttModel model) => recognition.GetValueOrDefault(model);

    public LocalSpeechVoice? FindVoice(SpeechVoiceChoice voice) => synthesis.GetValueOrDefault(voice);

    public void SelectRecognizer(LocalSttModel model, DictationSettings settings)
    {
        if (FindRecognizer(model) is not { IsModelInstalled: true } next) return;
        var previous = ParakeetDictation.Current;
        OpenClickyConfiguration.Update(shell =>
        {
            shell.ParakeetModel = model == VoiceModelCatalog.ParakeetV3 ? null : model.Info.Id;
            shell.TranscriptionProvider = "parakeet";
        });
        settings.Engine = DictationEngineChoice.Parakeet;
        if (previous is not null && previous != next) _ = previous.UnloadAsync();
    }

    public async Task RemoveRecognizerAsync(LocalSttModel model)
    {
        if (FindRecognizer(model) is not { } recognizer) return;
        await recognizer.RemoveAsync();
        if (recognizer.IsModelInstalled || ParakeetDictation.SelectedModel != model) return;
        var fallback = VoiceModelCatalog.Recognition.FirstOrDefault(candidate => FindRecognizer(candidate)?.IsModelInstalled == true);
        if (fallback is null) return;
        OpenClickyConfiguration.Update(shell => shell.ParakeetModel =
            fallback == VoiceModelCatalog.ParakeetV3 ? null : fallback.Info.Id);
    }

    public void SelectMoonshine(DictationSettings settings)
    {
        if (!MoonshineFacts.IsInstalled) return;
        OpenClickyConfiguration.Update(shell => shell.TranscriptionProvider = "moonshine");
        settings.Engine = DictationEngineChoice.Moonshine;
        _ = ParakeetDictation.Current?.UnloadAsync();
    }

    public void SelectVoice(LocalTtsModel model)
    {
        if (FindVoice(model.Choice)?.IsModelInstalled != true || model.SpeakerFor(ReplyLanguage.CurrentCode, null) is null) return;
        var previous = FindVoice(SpeechVoiceChoices.Current());
        OpenClickyConfiguration.Update(shell => shell.SpeechVoice = model.Choice.StoredValue());
        if (previous is not null && previous.Model != model) _ = previous.UnloadAsync();
    }

    public IEnumerable<DownloadableVoiceModel> Models => recognition.Values.Cast<DownloadableVoiceModel>().Concat(synthesis.Values);

    public void UnloadForMemoryPressure()
    {
        foreach (var model in Models) model.UnloadForMemoryPressure();
    }
}
