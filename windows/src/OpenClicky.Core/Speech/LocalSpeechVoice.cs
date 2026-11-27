using OpenClicky.Core.Configuration;
using OpenClicky.Core.Platform;

namespace OpenClicky.Core.Speech;

public class LocalSpeechVoice(LocalTtsModel model, IProcessRunner processes, string executablePath, IMainThread? mainThread)
    : DownloadableVoiceModel(model.Info, model.Package, model.HelperFlag, processes, executablePath, mainThread)
{
    public LocalTtsModel Model { get; } = model;

    public virtual Func<int?> PickedSpeaker { get; set; } = () => null;

    public void PrepareForAnswer()
    {
        if (SpeechVoiceChoices.Current() == Model.Choice) WarmUp(() => AudioAsync("ok"));
    }

    public async Task<byte[]> AudioAsync(string text, string? languageCode = null, CancellationToken cancellationToken = default)
    {
        languageCode ??= ReplyLanguage.CurrentCode;
        var speaker = Model.SpeakerFor(languageCode, PickedSpeaker())
            ?? throw new KokoroException(KokoroException.LanguageNotSpokenMessage);
        var request = new KokoroEngineRequest(text, speaker.Id, Model.LanguageHint(languageCode), Package.ModelFolder);
        var reply = await RunAsync<KokoroEngineRequest, KokoroHelperReply>(request,
            new KokoroException("Téléchargez " + Info.Name + " dans les paramètres de synthèse vocale."), cancellationToken).ConfigureAwait(false);
        try
        {
            var wav = Convert.FromBase64String(reply.Wav);
            if (wav.Length > 0) return wav;
        }
        catch (FormatException) { }
        throw new KokoroException(KokoroException.NothingGeneratedMessage);
    }
}
