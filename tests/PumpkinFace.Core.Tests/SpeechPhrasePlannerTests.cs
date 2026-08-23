using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class SpeechPhrasePlannerTests
{
    [Fact]
    public void Plan_MapsPhraseAcrossMeasuredAudioDuration()
    {
        TimeSpan duration = TimeSpan.FromSeconds(2.4);

        IReadOnlyList<VisemeFrame> frames = SpeechPhrasePlanner.Plan("Boom over", duration);

        Assert.Equal(TimeSpan.Zero, frames[0].Timestamp);
        Assert.Equal(Viseme.Silence, frames[0].Shape);
        Assert.Equal(duration, frames[^1].Timestamp);
        Assert.Equal(Viseme.Silence, frames[^1].Shape);
        Assert.Contains(frames, frame => frame.Shape == Viseme.Mbp);
        Assert.Contains(frames, frame => frame.Shape == Viseme.Ooh);
        Assert.Contains(frames, frame => frame.Shape == Viseme.Oh);
        Assert.True(frames.Zip(frames.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp));
    }

    [Fact]
    public void Plan_RejectsEmptyPhraseAndInvalidDuration()
    {
        Assert.Throws<ArgumentException>(() =>
            SpeechPhrasePlanner.Plan("  ", TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpeechPhrasePlanner.Plan("Boo", TimeSpan.Zero));
    }
}
