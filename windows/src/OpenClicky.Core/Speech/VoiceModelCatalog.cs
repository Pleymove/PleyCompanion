using OpenClicky.Core.Configuration;
using OpenClicky.Core.Dictation;

namespace OpenClicky.Core.Speech;

public sealed record VoiceModelPackage(string StorageFolder, string ArchiveFolder, ModelDownload Download,
    Func<string, bool> HasRequiredFiles)
{
    public string Folder => Path.Combine(OpenClickyPaths.ModelsDirectory, StorageFolder);
    public string ModelFolder => Path.Combine(Folder, ArchiveFolder);
    public bool IsInstalled => HasRequiredFiles(ModelFolder);
}

public sealed record VoiceModelInfo(string Id, string Name, string Detail, string Languages, string Size);

public sealed record LocalSttModel(VoiceModelInfo Info, VoiceModelPackage Package);

public sealed record LocalTtsModel(VoiceModelInfo Info, VoiceModelPackage Package, string HelperFlag,
    SpeechVoiceChoice Choice, Func<string, int?, KokoroVoice.Speaker?> SpeakerFor, Func<string, string> LanguageHint);

/// <summary>The packaged CPU models and their on-disk contracts. Keep legacy storage names stable.</summary>
public static class VoiceModelCatalog
{
    public static readonly LocalSttModel ParakeetV3 = new(
        new("parakeet-v3", "Parakeet V3", "NVIDIA Parakeet TDT 0.6B · ONNX int8", "25 langues européennes · détection automatique", "465 Mio"),
        new("Parakeet", ParakeetFacts.ModelFolderName,
            new(new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2"),
                "1495e91b59d7fc53ddca91f3431758ec99f8fa253fdb0e1e851be0f72b14b0f9", 487_211_443),
            ParakeetFacts.IsInstalledIn));

    public static readonly LocalSttModel ParakeetUltra = new(
        new("parakeet-ultra", "Parakeet Ultra", "Parakeet V3 affiné par Moondream · ONNX int8", "25 langues européennes · détection automatique", "465 Mio"),
        new("ParakeetUltra", "sherpa-onnx-nemo-parakeet-tdt-0.6b-ultra-int8",
            new(new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-ultra-int8.tar.bz2"),
                "d216b71d7f628b3f2f2a20ee7ead39ff0b075984959eb4bf6ddf8dc8da737c09", 487_134_290),
            ParakeetFacts.IsInstalledIn));

    public static readonly VoiceModelInfo Moonshine = new("moonshine", "Moonshine Tiny",
        "Useful Sensors · ONNX int8", "Anglais", "Inclus dans l’installateur");

    public static readonly LocalTtsModel Kokoro = new(
        new("kokoro", "Kokoro v1.0", "82 millions de paramètres · ONNX · plusieurs voix", "Français, anglais, espagnol, italien, portugais, hindi, japonais, mandarin", "350 Mo"),
        new("Kokoro", "kokoro-multi-lang-v1_0",
            new(new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-multi-lang-v1_0.tar.bz2"),
                "60703707bc05ccd48d60e1eb723f38b07d6e474795a0b6a588d7220400e0f7a4", 349_886_476),
            KokoroVoice.IsInstalledIn),
        KokoroVoice.HelperFlag, SpeechVoiceChoice.Kokoro, KokoroVoice.SpeakerFor, KokoroVoice.LanguageHint);

    public static readonly LocalTtsModel Piper = new(
        new("piper-siwis", "Piper · Siwis", "VITS · voix Siwis · 22,05 kHz", "Français (France)", "67 Mo"),
        new("PiperSiwis", "vits-piper-fr_FR-siwis-medium",
            new(new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-fr_FR-siwis-medium.tar.bz2"),
                "375909aa30842b3a4efa10b1beb1d761af792960ae6873b4d53889f96c66195b", 67_207_459),
            folder => KokoroVoice.EngineIsAvailable()
                && File.Exists(Path.Combine(folder, "fr_FR-siwis-medium.onnx"))
                && File.Exists(Path.Combine(folder, "tokens.txt"))
                && Directory.Exists(Path.Combine(folder, "espeak-ng-data"))),
        "--pley-piper-helper", SpeechVoiceChoice.Piper,
        (language, _) => language == "fr" ? new KokoroVoice.Speaker(0, "Siwis", "fr", "France") : null,
        _ => "fr-fr");

    public static readonly IReadOnlyList<LocalSttModel> Recognition = [ParakeetV3, ParakeetUltra];
    public static readonly IReadOnlyList<LocalTtsModel> Synthesis = [Kokoro, Piper];

    public static LocalSttModel SelectedParakeet(string? id) =>
        Recognition.FirstOrDefault(model => model.Info.Id == id) ?? ParakeetV3;

    public static LocalTtsModel? FindVoice(SpeechVoiceChoice choice) =>
        Synthesis.FirstOrDefault(model => model.Choice == choice);
}
