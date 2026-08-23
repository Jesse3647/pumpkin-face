using System.Diagnostics;
using SherpaOnnx;

namespace PumpkinFace.Display.Animation;

/// <summary>Downloads and runs the Kokoro neural voice model entirely on this Mac.</summary>
public sealed class KokoroSpeechSynthesizer : IDisposable
{
    public const int MaximumPhraseLength = 240;
    private const string ModelArchiveUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-multi-lang-v1_0.tar.bz2";
    private const string ModelDirectoryName = "kokoro-multi-lang-v1_0";
    private readonly string _modelsDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OfflineTts? _synthesizer;
    private bool? _usingBritishEnglish;

    private static readonly IReadOnlyDictionary<string, int> SpeakerIds =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["af_alloy"] = 0, ["af_aoede"] = 1, ["af_bella"] = 2,
            ["af_heart"] = 3, ["af_jessica"] = 4, ["af_kore"] = 5,
            ["af_nicole"] = 6, ["af_nova"] = 7, ["af_river"] = 8,
            ["af_sarah"] = 9, ["af_sky"] = 10, ["am_adam"] = 11,
            ["am_echo"] = 12, ["am_eric"] = 13, ["am_fenrir"] = 14,
            ["am_liam"] = 15, ["am_michael"] = 16, ["am_onyx"] = 17,
            ["am_puck"] = 18, ["am_santa"] = 19, ["bf_alice"] = 20,
            ["bf_emma"] = 21, ["bf_isabella"] = 22, ["bf_lily"] = 23,
            ["bm_daniel"] = 24, ["bm_fable"] = 25, ["bm_george"] = 26,
            ["bm_lewis"] = 27,
        };

    public KokoroSpeechSynthesizer(string modelsDirectory)
    {
        _modelsDirectory = modelsDirectory;
    }

    public static IReadOnlyList<SpeechVoiceChoice> Voices { get; } =
    [
        new("kokoro:af_alloy", "US female — Alloy"),
        new("kokoro:af_aoede", "US female — Aoede"),
        new("kokoro:af_bella", "US female — Bella"),
        new("kokoro:af_heart", "US female — Heart"),
        new("kokoro:af_jessica", "US female — Jessica"),
        new("kokoro:af_kore", "US female — Kore"),
        new("kokoro:af_nicole", "US female — Nicole"),
        new("kokoro:af_nova", "US female — Nova"),
        new("kokoro:af_river", "US female — River"),
        new("kokoro:af_sarah", "US female — Sarah"),
        new("kokoro:af_sky", "US female — Sky"),
        new("kokoro:am_adam", "US male — Adam"),
        new("kokoro:am_echo", "US male — Echo"),
        new("kokoro:am_eric", "US male — Eric"),
        new("kokoro:am_fenrir", "US male — Fenrir"),
        new("kokoro:am_liam", "US male — Liam"),
        new("kokoro:am_michael", "US male — Michael"),
        new("kokoro:am_onyx", "US male — Onyx"),
        new("kokoro:am_puck", "US male — Puck"),
        new("kokoro:am_santa", "US male — Santa"),
        new("kokoro:bf_alice", "UK female — Alice"),
        new("kokoro:bf_emma", "UK female — Emma"),
        new("kokoro:bf_isabella", "UK female — Isabella"),
        new("kokoro:bf_lily", "UK female — Lily"),
        new("kokoro:bm_daniel", "UK male — Daniel"),
        new("kokoro:bm_fable", "UK male — Fable"),
        new("kokoro:bm_george", "UK male — George"),
        new("kokoro:bm_lewis", "UK male — Lewis"),
    ];

    public async Task<SpeechSynthesisResult> SynthesizeAsync(
        string phrase,
        string voiceId,
        CancellationToken cancellationToken = default)
    {
        string normalized = phrase.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalized);
        if (normalized.Length > MaximumPhraseLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(phrase),
                $"Phrases are limited to {MaximumPhraseLength} characters.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string voiceName = voiceId.StartsWith("kokoro:", StringComparison.Ordinal)
                ? voiceId["kokoro:".Length..]
                : "af_heart";
            if (!SpeakerIds.TryGetValue(voiceName, out int speakerId))
            {
                voiceName = "af_heart";
                speakerId = SpeakerIds[voiceName];
            }
            await EnsureLoadedAsync(voiceName, cancellationToken);

            string directory = Path.Combine(Path.GetTempPath(), "PumpkinFaceSpeech");
            Directory.CreateDirectory(directory);
            string wavePath = Path.Combine(directory, $"kokoro-{Guid.NewGuid():N}.wav");

            OfflineTtsGenerationConfig generation = new()
            {
                Sid = speakerId,
                Speed = 1f,
                SilenceScale = 0.2f,
            };
            OfflineTtsCallbackProgressWithArg callback = (_, _, _, _) =>
                cancellationToken.IsCancellationRequested ? 0 : 1;
            var audio = await Task.Run(
                () => _synthesizer!.GenerateWithConfig(normalized, generation, callback),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!audio.SaveToWaveFile(wavePath))
            {
                throw new IOException("The neural voice could not save its audio file.");
            }

            // sherpa-onnx does not expose timestamps for offline TTS. The caller
            // stretches its deterministic viseme plan to the WAV's measured duration.
            return new SpeechSynthesisResult(normalized, wavePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _synthesizer?.Dispose();
        _gate.Dispose();
    }

    private async Task EnsureLoadedAsync(string voiceName, CancellationToken cancellationToken)
    {
        bool useBritishEnglish = voiceName.StartsWith('b');
        if (_synthesizer is not null && _usingBritishEnglish == useBritishEnglish)
        {
            return;
        }

        _synthesizer?.Dispose();
        _synthesizer = null;
        string modelDirectory = await EnsureModelInstalledAsync(cancellationToken);
        OfflineTtsConfig config = new();
        config.Model.Kokoro.Model = Path.Combine(modelDirectory, "model.onnx");
        config.Model.Kokoro.Voices = Path.Combine(modelDirectory, "voices.bin");
        config.Model.Kokoro.Tokens = Path.Combine(modelDirectory, "tokens.txt");
        config.Model.Kokoro.DataDir = Path.Combine(modelDirectory, "espeak-ng-data");
        config.Model.Kokoro.Lexicon = Path.Combine(
            modelDirectory,
            useBritishEnglish ? "lexicon-gb-en.txt" : "lexicon-us-en.txt");
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        config.Model.Debug = 0;
        config.Model.Provider = "cpu";
        _synthesizer = await Task.Run(() => new OfflineTts(config), cancellationToken);
        _usingBritishEnglish = useBritishEnglish;
    }

    private async Task<string> EnsureModelInstalledAsync(CancellationToken cancellationToken)
    {
        string modelDirectory = Path.Combine(_modelsDirectory, ModelDirectoryName);
        if (HasRequiredModelFiles(modelDirectory))
        {
            return modelDirectory;
        }

        Directory.CreateDirectory(_modelsDirectory);
        string archivePath = Path.Combine(_modelsDirectory, $"{ModelDirectoryName}.tar.bz2");
        if (!File.Exists(archivePath))
        {
            string temporaryPath = $"{archivePath}.download";
            using HttpClient client = new() { Timeout = TimeSpan.FromMinutes(15) };
            using HttpResponseMessage response = await client.GetAsync(
                ModelArchiveUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (FileStream destination = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }
            File.Move(temporaryPath, archivePath, overwrite: true);
        }

        await ExtractModelAsync(archivePath, cancellationToken);
        if (!HasRequiredModelFiles(modelDirectory))
        {
            throw new InvalidDataException("The downloaded neural voice model is incomplete.");
        }
        File.Delete(archivePath);
        return modelDirectory;
    }

    private async Task ExtractModelAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "Automatic neural voice installation currently requires macOS.");
        }

        ProcessStartInfo startInfo = new("/usr/bin/tar")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-xjf");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(_modelsDirectory);
        using Process process = Process.Start(startInfo) ??
            throw new IOException("The neural voice model extractor could not start.");
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"The neural voice model could not be unpacked: {error.Trim()}");
        }
    }

    private static bool HasRequiredModelFiles(string modelDirectory) =>
        File.Exists(Path.Combine(modelDirectory, "model.onnx")) &&
        File.Exists(Path.Combine(modelDirectory, "voices.bin")) &&
        File.Exists(Path.Combine(modelDirectory, "tokens.txt")) &&
        File.Exists(Path.Combine(modelDirectory, "lexicon-us-en.txt")) &&
        File.Exists(Path.Combine(modelDirectory, "lexicon-gb-en.txt")) &&
        Directory.Exists(Path.Combine(modelDirectory, "espeak-ng-data"));
}
