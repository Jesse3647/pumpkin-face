using PumpkinFace.Display.Animation;

namespace PumpkinFace.Display.Tests;

public sealed class KokoroSpeechSynthesizerTests
{
    [Fact]
    public void Voices_ExposeEveryAmericanAndBritishEnglishSpeaker()
    {
        IReadOnlyList<SpeechVoiceChoice> voices = KokoroSpeechSynthesizer.Voices;

        Assert.Equal(28, voices.Count);
        Assert.Equal(28, voices.Select(voice => voice.Id).Distinct().Count());
        Assert.Equal(20, voices.Count(voice => voice.Label.StartsWith("US ")));
        Assert.Equal(8, voices.Count(voice => voice.Label.StartsWith("UK ")));
        Assert.Contains(voices, voice => voice.Id == "kokoro:af_heart");
        Assert.Contains(voices, voice => voice.Id == "kokoro:am_onyx");
        Assert.Contains(voices, voice => voice.Id == "kokoro:bm_lewis");
    }
}
