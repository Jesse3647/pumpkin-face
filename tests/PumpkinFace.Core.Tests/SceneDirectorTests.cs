using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class SceneDirectorTests
{
    [Fact]
    public void Constructor_StartsWithBoundedNeutralDelayWhenAutoplayIsEnabled()
    {
        var director = new SceneDirector(seed: 123);

        Assert.Equal(SceneDirectorState.Neutral, director.Snapshot.State);
        Assert.True(director.Snapshot.AutoplayEnabled);
        Assert.True(SceneTimings.NeutralDelay.Contains(director.Snapshot.PhaseDuration));
        Assert.Equal(director.Snapshot.PhaseDuration, director.Snapshot.NeutralDelayRemaining);
        Assert.Equal(TimeSpan.FromMilliseconds(250), director.CrossfadeDuration);
    }

    [Fact]
    public void SameSeed_ProducesIdenticalTimingAndShuffleSequence()
    {
        var first = new SceneDirector(seed: 8675309);
        var second = new SceneDirector(seed: 8675309);

        for (var index = 0; index < 80; index++)
        {
            Assert.Equal(first.Snapshot, second.Snapshot);
            var elapsed = first.Snapshot.PhaseRemaining;
            first.Update(elapsed);
            second.Update(elapsed);
        }

        Assert.Equal(first.Snapshot, second.Snapshot);
    }

    [Fact]
    public void Autoplay_UsesDurationRangesAndNeverImmediatelyRepeats()
    {
        var director = new SceneDirector(seed: 42);
        var scenes = new List<EmotionId>();
        director.Transitioned += transition =>
        {
            if (transition.Kind == SceneTransitionKind.SceneStarted)
            {
                scenes.Add(Assert.IsType<EmotionId>(transition.Scene));
                Assert.False(transition.IsManuallyTriggered);
                Assert.True(SceneTimings.For(transition.Scene.Value).Contains(transition.Snapshot.PhaseDuration));
            }
        };

        while (scenes.Count < 24)
        {
            if (director.Snapshot.State == SceneDirectorState.Neutral)
            {
                Assert.True(SceneTimings.NeutralDelay.Contains(director.Snapshot.PhaseDuration));
            }

            director.Update(director.Snapshot.PhaseRemaining);
        }

        Assert.All(
            scenes.Zip(scenes.Skip(1)),
            pair => Assert.NotEqual(pair.First, pair.Second));

        foreach (var bag in scenes.Chunk(3))
        {
            Assert.Equal(3, bag.Distinct().Count());
        }
    }

    [Fact]
    public void SceneTimings_KeepRandomizedStretchCloseToTheAuthoredTempo()
    {
        foreach (var scene in Enum.GetValues<EmotionId>())
        {
            var timing = SceneTimings.For(scene);
            var spread = timing.Maximum - timing.Minimum;

            Assert.True(timing.Minimum > TimeSpan.Zero);
            Assert.True(spread > TimeSpan.Zero);
            Assert.True(spread <= TimeSpan.FromSeconds(1));
            Assert.True(timing.Maximum.TotalSeconds / timing.Minimum.TotalSeconds <= 1.13d);
        }
    }

    [Fact]
    public void ManualScene_InterruptsImmediatelyAndAutoplayResumesAfterCompletion()
    {
        var director = new SceneDirector(seed: 101);
        var transitions = new List<SceneDirectorTransition>();
        director.Transitioned += transitions.Add;

        Assert.True(director.Handle(new PlayEmotionCommand(EmotionId.Happy)));
        director.Update(TimeSpan.FromSeconds(1));
        Assert.True(director.Handle(new PlayEmotionCommand(EmotionId.Frightened)));

        var interrupted = director.Snapshot;
        Assert.Equal(SceneDirectorState.Playing, interrupted.State);
        Assert.Equal(EmotionId.Frightened, interrupted.CurrentScene);
        Assert.True(interrupted.IsManuallyTriggered);
        Assert.Equal(TimeSpan.Zero, interrupted.PhaseElapsed);
        Assert.Equal(EmotionId.Happy, transitions[^1].PreviousScene);
        Assert.Equal(TimeSpan.FromMilliseconds(250), transitions[^1].CrossfadeDuration);

        director.Update(interrupted.PhaseDuration);

        Assert.Equal(SceneDirectorState.Neutral, director.Snapshot.State);
        Assert.Null(director.Snapshot.CurrentScene);
        Assert.True(director.Snapshot.AutoplayEnabled);
        Assert.True(SceneTimings.NeutralDelay.Contains(director.Snapshot.PhaseDuration));
        Assert.Equal(SceneTransitionKind.SceneCompleted, transitions[^1].Kind);
        Assert.True(transitions[^1].IsManuallyTriggered);

        director.Update(director.Snapshot.PhaseRemaining);
        Assert.Equal(SceneDirectorState.Playing, director.Snapshot.State);
        Assert.False(director.Snapshot.IsManuallyTriggered);
    }

    [Fact]
    public void ManualScene_RetriggerEmitsASecondStartAndRestartsItsTimeline()
    {
        var director = new SceneDirector(seed: 2026);
        var transitions = new List<SceneDirectorTransition>();
        director.Transitioned += transitions.Add;

        director.Handle(new PlayEmotionCommand(EmotionId.Happy));
        director.Update(TimeSpan.FromSeconds(1));
        long revisionBeforeRetrigger = director.Snapshot.Revision;

        director.Handle(new PlayEmotionCommand(EmotionId.Happy));

        SceneDirectorTransition retrigger = transitions[^1];
        Assert.Equal(SceneTransitionKind.SceneStarted, retrigger.Kind);
        Assert.Equal(EmotionId.Happy, retrigger.PreviousScene);
        Assert.Equal(EmotionId.Happy, retrigger.Scene);
        Assert.Equal(TimeSpan.Zero, retrigger.Snapshot.PhaseElapsed);
        Assert.True(SceneTimings.For(EmotionId.Happy).Contains(retrigger.Snapshot.PhaseDuration));
        Assert.True(retrigger.Snapshot.Revision > revisionBeforeRetrigger);
        Assert.Equal(TimeSpan.FromMilliseconds(250), retrigger.CrossfadeDuration);
    }

    [Fact]
    public void DisablingAutoplay_LetsCurrentSceneFinishThenHoldsNeutral()
    {
        var director = new SceneDirector(seed: 12);
        director.Handle(new PlayEmotionCommand(EmotionId.Sad));

        director.Handle(new SetAutoplayCommand(false));
        Assert.False(director.Snapshot.AutoplayEnabled);
        Assert.Equal(SceneDirectorState.Playing, director.Snapshot.State);

        director.Update(director.Snapshot.PhaseRemaining);
        Assert.Equal(SceneDirectorState.Neutral, director.Snapshot.State);
        Assert.Equal(TimeSpan.Zero, director.Snapshot.PhaseDuration);

        director.Update(TimeSpan.FromDays(1));
        Assert.Equal(SceneDirectorState.Neutral, director.Snapshot.State);
        Assert.Null(director.Snapshot.CurrentScene);
    }

    [Fact]
    public void Stop_CancelsImmediatelyAndEnableAutoplayStartsFreshDelay()
    {
        var director = new SceneDirector(seed: 99);
        director.Handle(new PlayEmotionCommand(EmotionId.Frightened));
        director.Update(TimeSpan.FromSeconds(1));

        director.Handle(new StopCommand());

        Assert.Equal(SceneDirectorState.Stopped, director.Snapshot.State);
        Assert.False(director.Snapshot.AutoplayEnabled);
        Assert.Null(director.Snapshot.CurrentScene);
        director.Update(TimeSpan.FromDays(7));
        Assert.Equal(SceneDirectorState.Stopped, director.Snapshot.State);

        director.Handle(new SetAutoplayCommand(true));
        Assert.Equal(SceneDirectorState.Neutral, director.Snapshot.State);
        Assert.True(director.Snapshot.AutoplayEnabled);
        Assert.True(SceneTimings.NeutralDelay.Contains(director.Snapshot.PhaseDuration));
    }

    [Fact]
    public void NextScene_NeverSelectsTheCurrentlyPlayingScene()
    {
        var director = new SceneDirector(seed: 321);
        director.Handle(new PlayEmotionCommand(EmotionId.Happy));

        director.Handle(new NextEmotionCommand());

        Assert.Equal(SceneDirectorState.Playing, director.Snapshot.State);
        Assert.NotEqual(EmotionId.Happy, director.Snapshot.CurrentScene);
        Assert.True(director.Snapshot.IsManuallyTriggered);
    }

    [Fact]
    public void LargeUpdate_MatchesIncrementalUpdates()
    {
        var largeStep = new SceneDirector(seed: 7654);
        var smallSteps = new SceneDirector(seed: 7654);

        largeStep.Update(TimeSpan.FromMinutes(2));
        for (var index = 0; index < 120; index++)
        {
            smallSteps.Update(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(largeStep.Snapshot, smallSteps.Snapshot);
    }

    [Fact]
    public void Director_RejectsInvalidTimeAndIgnoresDisplayOnlyCommands()
    {
        var director = new SceneDirector(seed: 1);
        var before = director.Snapshot;

        Assert.Throws<ArgumentOutOfRangeException>(() => director.Update(TimeSpan.FromTicks(-1)));
        Assert.False(director.Handle(new ApplyCalibrationCommand(ProjectionCalibration.Default)));
        Assert.Equal(before, director.Snapshot);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => director.Handle(new PlayEmotionCommand((EmotionId)999)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SceneDirector(crossfadeDuration: TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void VisemeAndAudioContracts_NormalizeForFutureLipSync()
    {
        var frame = new VisemeFrame(TimeSpan.FromSeconds(-1), (Viseme)999, 4f).Normalize();
        Assert.Equal(TimeSpan.Zero, frame.Timestamp);
        Assert.Equal(Viseme.Neutral, frame.Shape);
        Assert.Equal(1f, frame.Weight);

        IAudioClock clock = new FakeAudioClock(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(150));
        Assert.Equal(TimeSpan.Zero, clock.AudiblePosition);
    }

    private sealed record FakeAudioClock(TimeSpan PlaybackPosition, TimeSpan OutputLatency) : IAudioClock
    {
        public bool IsPlaying => true;
    }
}
