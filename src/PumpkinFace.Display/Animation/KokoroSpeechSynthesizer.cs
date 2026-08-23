using System.Diagnostics;
using SherpaOnnx;

namespace PumpkinFace.Display.Animation;

/// <summary>Downloads and runs the Kokoro neural voice model entirely on this Mac.</summary>
public sealed class KokoroSpeechSynthesizer : IDisposable
{
    private const string ModelArchiveUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-multi-lang-v1_0.tar.bz2";
    private const string ModelDirectoryName = "kokoro-multi-lang-v1_0";
    private readonly string _modelsDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OfflineTts? _synthesizer;

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
        new("kokoro:af_heart", "Neural — Heart (US female)"),
        new("kokoro:af_bella", "Neural — Bella (US female)"),
        new("kokoro:af_nicole", "Neural — Nicole (US female)"),
        new("kokoro:af_sarah", "Neural — Sarah (US female)"),
        new("kokoro:af_sky", "Neural — Sky (US female)"),
        new("kokoro:am_adam", "Neural — Adam (US male)"),
        new("kokoro:am_michael", "Neural — Michael (US male)"),
        new("kokoro:am_puck", "Neural — Puck (US male)"),
        new("kokoro:bf_emma", "Neural — Emma (UK female)"),
        new("kokoro:bm_george", "Neural — George (UK male)"),
    ];

    public async Task<SpeechSynthesisResult> SynthesizeAsync(
        string phrase,
        string voiceId,
        CancellationToken cancellationToken = default)
    {
        string normalized = phrase.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalized);
        if (normalized.Length > MacSpeechSynthesizer.MaximumPhraseLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(phrase),
                $"Phrases are limited to {MacSpeechSynthesizer.MaximumPhraseLength} characters.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            string voiceName = voiceId.StartsWith("kokoro:", StringComparison.Ordinal)
                ? voiceId["kokoro:".Length..]
                : "af_heart";
            int speakerId = SpeakerIds.GetValueOrDefault(voiceName, SpeakerIds["af_heart"]);

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

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_synthesizer is not null)
        {
            return;
        }

        string modelDirectory = await EnsureModelInstalledAsync(cancellationToken);
        OfflineTtsConfig config = new();
        config.Model.Kokoro.Model = Path.Combine(modelDirectory, "model.onnx");
        config.Model.Kokoro.Voices = Path.Combine(modelDirectory, "voices.bin");
        config.Model.Kokoro.Tokens = Path.Combine(modelDirectory, "tokens.txt");
        config.Model.Kokoro.DataDir = Path.Combine(modelDirectory, "espeak-ng-data");
        config.Model.Kokoro.Lexicon = Path.Combine(modelDirectory, "lexicon-us-en.txt");
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        config.Model.Debug = 0;
        config.Model.Provider = "cpu";
        _synthesizer = await Task.Run(() => new OfflineTts(config), cancellationToken);
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
        Directory.Exists(Path.Combine(modelDirectory, "espeak-ng-data"));
}
