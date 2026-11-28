// The helper processes for Kokoro and Parakeet (KokoroHelper.swift, ParakeetEngine.swift's
// ParakeetHelper, KokoroEngine.swift): OpenClicky.exe started with a flag, caught in Program.Main
// before any UI exists. One JSON line each way over stdin/stdout (SherpaHelperProtocol); the
// helper ends when the app closes its stdin, or after 180 s without a request (the app ends it
// sooner itself; this is so an orphan never lingers).
//
// The engines run on sherpa-onnx's .NET API (org.k2fsa.sherpa.onnx 1.13.8, the same version the
// Mac embeds), configured as the Mac configures the C API.

using System.IO;
using System.Text;
using OpenClicky.Core.Speech;
using SherpaOnnx;

namespace OpenClicky.Windows.Speech;

public static class SherpaHelperMain
{
    public const string ParakeetFlag = ParakeetDictation.HelperFlag;
    public const string MoonshineFlag = MoonshineDictation.HelperFlag;

    /// <summary>How long a helper waits for a request before it ends on its own.</summary>
    public static readonly TimeSpan IdleExit = TimeSpan.FromSeconds(180);

    public static bool IsHelperInvocation(string[] args) => args.Any(argument => argument is ParakeetFlag or MoonshineFlag)
        || VoiceModelCatalog.Synthesis.Any(model => args.Contains(model.HelperFlag));

    /// <summary>Runs the helper named in <paramref name="args"/> until stdin closes; returns the exit code.</summary>
    public static int Run(string[] args)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var input = new StreamReader(Console.OpenStandardInput(), utf8);
        using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true, NewLine = "\n" };
        var idleInput = new IdleExitReader(input, IdleExit);
        if (VoiceModelCatalog.Synthesis.FirstOrDefault(model => args.Contains(model.HelperFlag)) is { } ttsModel)
        {
            using var engine = new OfflineTtsHelperEngine(ttsModel);
            SherpaHelperProtocol.Run<KokoroEngineRequest, KokoroHelperReply>(idleInput, output, request => new KokoroHelperReply(Convert.ToBase64String(engine.Generate(request))));
            return 0;
        }
        var model = args.Contains(ParakeetFlag) ? OfflineRecognizerModel.Parakeet
            : args.Contains(MoonshineFlag) ? OfflineRecognizerModel.Moonshine
            : null;
        if (model is not null)
        {
            using var engine = new OfflineRecognizerHelperEngine(model);
            SherpaHelperProtocol.Run<ParakeetEngineRequest, ParakeetEngineReply>(idleInput, output, engine.Transcribe);
            return 0;
        }
        return 2;
    }

    /// <summary>Workers for the engines: min(4, max(1, cores − 1)).</summary>
    internal static int ThreadCount => Math.Min(4, Math.Max(1, Environment.ProcessorCount - 1));

    /// <summary>A reader whose ReadLine gives up (returns null, ending the helper) after a quiet spell.</summary>
    private sealed class IdleExitReader(TextReader inner, TimeSpan idleLimit) : TextReader
    {
        public override string? ReadLine()
        {
            var read = Task.Run(inner.ReadLine);
            return read.Wait(idleLimit) ? read.Result : null;
        }
    }
}

/// <summary>Sherpa's offline synthesizer, used only inside the helper.</summary>
internal sealed class OfflineTtsHelperEngine(LocalTtsModel model) : IDisposable
{
    private OfflineTts? tts;
    private string? loadedLanguageHint;
    private string? loadedModelFolder;

    /// <summary>The text spoken, as WAV bytes; loads the model first if it isn't (about 0.8 s).</summary>
    public byte[] Generate(KokoroEngineRequest request)
    {
        if (tts is null || loadedLanguageHint != request.LanguageHint || loadedModelFolder != request.ModelFolder)
        {
            tts?.Dispose();
            tts = null;
            var startedAt = DateTime.UtcNow;
            // sherpa-onnx hands back a dead engine rather than an error for missing files.
            if (!model.Package.HasRequiredFiles(request.ModelFolder)) throw new KokoroException(KokoroException.ModelNotLoadedMessage);
            try
            {
                tts = new OfflineTts(Config(request));
            }
            catch (Exception)
            {
                throw new KokoroException(KokoroException.ModelNotLoadedMessage);
            }
            loadedLanguageHint = request.LanguageHint;
            loadedModelFolder = request.ModelFolder;
            Console.Error.WriteLine($"{model.Info.Id}: model loaded in {(DateTime.UtcNow - startedAt).TotalSeconds:0.0} s");
        }
        var audio = tts.Generate(request.Text, 1.0f, request.SpeakerId);
        try
        {
            var samples = audio.Samples;
            if (samples is null || samples.Length == 0) throw new KokoroException(KokoroException.NothingGeneratedMessage);
            return KokoroEngine.Wav(samples, audio.SampleRate);
        }
        finally
        {
            audio.Dispose();
        }
    }

    private OfflineTtsConfig Config(KokoroEngineRequest request)
    {
        var folder = request.ModelFolder;
        var config = new OfflineTtsConfig();
        if (model.Choice == SpeechVoiceChoice.Piper)
        {
            config.Model.Vits.Model = Path.Combine(folder, "fr_FR-siwis-medium.onnx");
            config.Model.Vits.Tokens = Path.Combine(folder, "tokens.txt");
            config.Model.Vits.DataDir = Path.Combine(folder, "espeak-ng-data");
        }
        else
        {
            config.Model.Kokoro.Model = Path.Combine(folder, "model.onnx");
            config.Model.Kokoro.Voices = Path.Combine(folder, "voices.bin");
            config.Model.Kokoro.Tokens = Path.Combine(folder, "tokens.txt");
            config.Model.Kokoro.DataDir = Path.Combine(folder, "espeak-ng-data");
            config.Model.Kokoro.DictDir = Path.Combine(folder, "dict");
            config.Model.Kokoro.LengthScale = 1.0f;
            if (request.LanguageHint is "en-us" or "zh")
            {
                config.Model.Kokoro.Lexicon = string.Join(",", new[] { "lexicon-us-en.txt", "lexicon-zh.txt" }
                    .Select(name => Path.Combine(folder, name)));
            }
            else
            {
                config.Model.Kokoro.Lang = request.LanguageHint;
            }
        }
        config.Model.NumThreads = SherpaHelperMain.ThreadCount;
        config.Model.Provider = "cpu";
        config.MaxNumSentences = 1;
        config.SilenceScale = 0.2f;
        return config;
    }

    public void Dispose() => tts?.Dispose();
}

/// <summary>A model sherpa-onnx's OfflineRecognizer runs in a helper: its log name, the check that its
/// files are there, and its configuration for a model folder.</summary>
internal sealed record OfflineRecognizerModel(string Name, Func<string, bool> IsInstalledIn, Func<string, OfflineRecognizerConfig> Config)
{
    /// <summary>NVIDIA's Parakeet TDT 0.6B v3 (int8), a transducer (ParakeetEngine.swift).</summary>
    public static readonly OfflineRecognizerModel Parakeet = new("parakeet", ParakeetDictation.IsInstalledIn, modelFolder =>
    {
        var config = BaseConfig();
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelFolder, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelFolder, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelFolder, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(modelFolder, "tokens.txt");
        config.ModelConfig.ModelType = "nemo_transducer";
        return config;
    });

    /// <summary>Useful Sensors' Moonshine tiny (English, int8), the model that ships inside the app.</summary>
    public static readonly OfflineRecognizerModel Moonshine = new("moonshine", MoonshineDictation.IsInstalledIn, modelFolder =>
    {
        var config = BaseConfig();
        config.ModelConfig.Moonshine.Preprocessor = Path.Combine(modelFolder, "preprocess.onnx");
        config.ModelConfig.Moonshine.Encoder = Path.Combine(modelFolder, "encode.int8.onnx");
        config.ModelConfig.Moonshine.UncachedDecoder = Path.Combine(modelFolder, "uncached_decode.int8.onnx");
        config.ModelConfig.Moonshine.CachedDecoder = Path.Combine(modelFolder, "cached_decode.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(modelFolder, "tokens.txt");
        return config;
    });

    private static OfflineRecognizerConfig BaseConfig()
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = ParakeetEngine.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.NumThreads = SherpaHelperMain.ThreadCount;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        return config;
    }
}

/// <summary>A dictation model through sherpa-onnx's OfflineRecognizer: the whole take decoded at once.</summary>
internal sealed class OfflineRecognizerHelperEngine(OfflineRecognizerModel model) : IDisposable
{
    private OfflineRecognizer? recognizer;

    /// <summary>The words in the request's audio; loads the model first if it isn't. An empty request only
    /// loads it (the warm-up when the dictation key goes down).</summary>
    public ParakeetEngineReply Transcribe(ParakeetEngineRequest request)
    {
        if (recognizer is null)
        {
            var startedAt = DateTime.UtcNow;
            if (!model.IsInstalledIn(request.ModelFolder)) throw new ParakeetException(ParakeetException.ModelNotLoadedMessage);
            try
            {
                recognizer = new OfflineRecognizer(model.Config(request.ModelFolder));
            }
            catch (Exception)
            {
                throw new ParakeetException(ParakeetException.ModelNotLoadedMessage);
            }
            Console.Error.WriteLine($"{model.Name}: model loaded in {(DateTime.UtcNow - startedAt).TotalSeconds:0.0} s");
        }
        byte[] pcm16;
        try { pcm16 = Convert.FromBase64String(request.Pcm16Base64); }
        catch (FormatException) { throw new ParakeetException(ParakeetException.UnreadableAudioMessage); }
        var samples = ParakeetEngine.FloatSamples(pcm16);
        if (samples.Length == 0) return new ParakeetEngineReply("");
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(ParakeetEngine.SampleRate, samples);
        recognizer.Decode(stream);
        return new ParakeetEngineReply((stream.Result?.Text ?? "").Trim());
    }

    public void Dispose() => recognizer?.Dispose();
}
