namespace PumpkinFace.Display.Animation;

public readonly record struct SpeechSynthesisResult(
    string Phrase,
    string WavePath,
    IReadOnlyList<PumpkinFace.Core.VisemeFrame>? Visemes = null);
