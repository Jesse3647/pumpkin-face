using PumpkinFace.Core;
using PumpkinFace.Display.Animation;

namespace PumpkinFace.Display.Tests;

public sealed class SpeechPerformanceTests
{
    private static ActionSceneController Speech(float weight = 1)
    {
        ActionSceneController c = new(seed: 42) { ExternalSpeechCompletion = true };
        c.ConfigureSpeech(new[] {
            new VisemeFrame(TimeSpan.Zero, Viseme.Ah, weight),
            new VisemeFrame(TimeSpan.FromSeconds(1), Viseme.Ah, weight),
        }, TimeSpan.FromSeconds(1));
        c.SetSelected(SceneId.Talking, true);
        return c;
    }

    [Fact]
    public void SpeechEntersSmoothlyAndVisemeWeightsAffectTheMouth()
    {
        ActionSceneController full = Speech(), silent = Speech(0);
        Assert.Equal(0, full.Frame.SpeechBlend);
        full.SynchronizeSpeech(TimeSpan.FromSeconds(.05));
        Assert.InRange(full.Frame.SpeechBlend, .45f, .55f);
        full.SynchronizeSpeech(TimeSpan.FromSeconds(.5));
        silent.SynchronizeSpeech(TimeSpan.FromSeconds(.5));
        Assert.True(full.Frame.JawOpen > silent.Frame.JawOpen + .5f);
    }

    [Fact]
    public void ReleaseDuringEntryDoesNotJumpToFullSpeechAndCanBeRepeated()
    {
        ActionSceneController c = Speech();
        c.SynchronizeSpeech(TimeSpan.FromSeconds(.035));
        float before = c.Frame.SpeechBlend;
        c.BeginSpeechRelease();
        Assert.Equal(before, c.Frame.SpeechBlend);
        c.Update(.1);
        float partial = c.Frame.SpeechBlend;
        c.BeginSpeechRelease();
        Assert.Equal(partial, c.Frame.SpeechBlend);
        c.Update(.3);
        Assert.False(c.IsSelected(SceneId.Talking));
        Assert.Equal(0, c.Frame.SpeechBlend);
    }

    [Fact]
    public void AudibleClockCorrectsRenderTimeAndCompletionReleases()
    {
        ActionSceneController c = Speech();
        c.Update(.8);
        c.SynchronizeSpeech(TimeSpan.FromSeconds(.025));
        Assert.True(c.Frame.SpeechBlend < .2);
        c.Update(2);
        Assert.True(c.Frame.SpeechActive);
        c.BeginSpeechRelease();
        Assert.False(c.Frame.SpeechActive);
        c.Update(.4);
        Assert.Equal(ActionSceneFrame.Rest, c.Frame);
    }
}
