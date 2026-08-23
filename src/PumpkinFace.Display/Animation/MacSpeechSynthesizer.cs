using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PumpkinFace.Display.Animation;

public readonly record struct SpeechSynthesisResult(
    string Phrase,
    string WavePath,
    IReadOnlyList<PumpkinFace.Core.VisemeFrame>? Visemes = null);

/// <summary>Creates a local WAV using the speech voice configured with macOS.</summary>
public static class MacSpeechSynthesizer
{
    public const int MaximumPhraseLength = 240;
    public const string SystemDefaultVoice = "System default";
    public const string RecommendedVoice = "Reed (English (US))";
    private static readonly string[] NaturalVoiceCandidates =
    [
        RecommendedVoice,
        "Sandy (English (US))",
        "Shelley (English (US))",
        "Flo (English (US))",
        "Eddy (English (US))",
        "Samantha",
        "Daniel",
        "Karen",
        "Moira",
        "Rishi",
        "Aman",
        "Tara",
    ];

    public static IReadOnlyList<string> FindAvailableNaturalVoices()
    {
        List<string> voices = [SystemDefaultVoice];
        if (!OperatingSystem.IsMacOS())
        {
            return voices;
        }

        try
        {
            ProcessStartInfo startInfo = new("/usr/bin/say")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("?");
            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            HashSet<string> installed = Regex.Matches(
                    output,
                    @"^(?<name>.+?)\s+en_(?:US|GB|AU|IE|IN)\s+#",
                    RegexOptions.Multiline)
                .Select(match => match.Groups["name"].Value.Trim())
                .ToHashSet(StringComparer.Ordinal);
            voices.AddRange(NaturalVoiceCandidates.Where(installed.Contains));
        }
        catch
        {
            // The system-default voice remains usable if discovery is unavailable.
        }

        return voices;
    }

    public static async Task<SpeechSynthesisResult> SynthesizeAsync(
        string phrase,
        string? voice = null,
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
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Typed speech currently requires macOS.");
        }

        string directory = Path.Combine(Path.GetTempPath(), "PumpkinFaceSpeech");
        Directory.CreateDirectory(directory);
        string wavePath = Path.Combine(directory, $"speech-{Guid.NewGuid():N}.wav");
        ProcessStartInfo startInfo = new("/usr/bin/say")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add("160");
        if (!string.IsNullOrWhiteSpace(voice) && voice != SystemDefaultVoice)
        {
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add(voice);
        }
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(wavePath);
        startInfo.ArgumentList.Add("--file-format=WAVE");
        startInfo.ArgumentList.Add("--data-format=LEI16@44100");
        startInfo.ArgumentList.Add(normalized);

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("macOS speech synthesis could not be started.");
        }

        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || !File.Exists(wavePath) || new FileInfo(wavePath).Length <= 4096)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "macOS could not synthesize that phrase." : error.Trim());
        }

        return new SpeechSynthesisResult(normalized, wavePath);
    }
}
