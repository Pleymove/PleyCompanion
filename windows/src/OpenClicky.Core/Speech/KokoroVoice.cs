using System.Text.Json.Serialization;
using OpenClicky.Core.Configuration;
using OpenClicky.Core.Platform;

namespace OpenClicky.Core.Speech;

public sealed partial class KokoroVoice(IProcessRunner processes, string executablePath, IMainThread? mainThread = null)
    : LocalSpeechVoice(VoiceModelCatalog.Kokoro, processes, executablePath, mainThread)
{
    public static KokoroVoice? Shared { get; set; }

    public const string RuntimeVersion = "1.13.8";
    public const string HelperFlag = "--openclicky-kokoro-helper";
    public const string DownloadSizeText = "350 MB";

    public static ModelDownload ModelDownload => VoiceModelCatalog.Kokoro.Package.Download;
    public static bool IsInstalled => VoiceModelCatalog.Kokoro.Package.IsInstalled;

    public static Func<bool> EngineIsAvailable { get; set; } = () => true;

    public static bool IsInstalledIn(string modelFolder) =>
        EngineIsAvailable()
        && File.Exists(Path.Combine(modelFolder, "model.onnx"))
        && File.Exists(Path.Combine(modelFolder, "voices.bin"))
        && File.Exists(Path.Combine(modelFolder, "tokens.txt"))
        && Directory.Exists(Path.Combine(modelFolder, "espeak-ng-data"));

    public override Func<int?> PickedSpeaker { get; set; } = () => OpenClickyConfiguration.Settings.KokoroSpeaker;
}

/// <summary>One request to the Kokoro helper (KokoroEngine.Request).</summary>
/// <param name="LanguageHint">sherpa-onnx's `lang` hint; English and Mandarin go by the model's lexicons instead.</param>
public sealed record KokoroEngineRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("speakerID")] int SpeakerId,
    [property: JsonPropertyName("languageHint")] string LanguageHint,
    [property: JsonPropertyName("modelFolder")] string ModelFolder);

/// <summary>The helper's answer: the spoken WAV, base64.</summary>
public sealed record KokoroHelperReply([property: JsonPropertyName("wav")] string Wav);

public static class KokoroEngine
{
    /// <summary>Mono float samples in [-1, 1] as a 16-bit WAV file (clipped, never wrapped).</summary>
    public static byte[] Wav(ReadOnlySpan<float> samples, int sampleRate) =>
        BuddyWAVFileBuilder.BuildWavData(BuddyPCM16AudioConverter.FloatToPcm16(samples), sampleRate);
}

public sealed class KokoroException(string message) : Exception(message)
{
    public const string NotInstalledMessage = "Kokoro isn't downloaded yet.";
    public const string LanguageNotSpokenMessage = "Kokoro doesn't speak this language.";
    public const string ModelNotLoadedMessage = "Kokoro's model didn't load.";
    public const string NothingGeneratedMessage = "Kokoro said nothing.";
}
