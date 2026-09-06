using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class PlayfulScarePerformanceTests
{
    private static readonly FacePose Rest = FacePose.Neutral with { MouthWidth = .85f, LeftMouthCorner = .9f, RightMouthCorner = .9f };

    [Fact]
    public void SceneHasReadableSetupScareLaughterAndReturnsToRest()
    {
        FacePose notice = PlayfulScarePerformance.Sample(.65, Rest);
        FacePose curious = PlayfulScarePerformance.Sample(2.3, Rest);
        FacePose windup = PlayfulScarePerformance.Sample(5.4, Rest);
        FacePose scare = PlayfulScarePerformance.Sample(6.1, Rest);
        Assert.True(notice.LeftGazeX > .4);
        Assert.True(curious.MotionRoll > .1);
        Assert.NotEqual(curious.LeftBrowTension, curious.RightBrowTension);
        Assert.True(windup.LeftEyelidOpen < .5);
        Assert.True(scare.LeftEyelidOpen > .9);
        Assert.True(scare.MouthRoundness > .8);
        Assert.True(scare.SpeechBlend > .9);
        Assert.Equal(Rest, PlayfulScarePerformance.Sample(PlayfulScarePerformance.Duration, Rest));
    }

    [Fact]
    public void RecordedChucklesHaveUnevenJawPulsesAndKeepEyeActing()
    {
        var frames = Enumerable.Range(0, 120).Select(i => PlayfulScarePerformance.Sample(7.2 + i / 100d, Rest)).ToArray();
        Assert.True(frames.Max(f => f.JawOpen) - frames.Min(f => f.JawOpen) > .3);
        Assert.All(frames, f => Assert.True(f.LeftEyelidOpen < .9));
        Assert.True(PlayfulScarePerformance.Sample(9.45, Rest).SpeechBlend < .01);
    }

    [Fact]
    public void RandomAccessMatchesSequentialSamplingAndAllPosesAreFiniteAndBounded()
    {
        var frames = Enumerable.Range(0, 1351).Select(i => PlayfulScarePerformance.Sample(i / 100d, Rest)).ToArray();
        foreach (int index in Enumerable.Range(0, 1351).Reverse())
        {
            Assert.Equal(frames[index], PlayfulScarePerformance.Sample(index / 100d, Rest));
            Assert.Equal(frames[index].Clamp(), frames[index]);
        }
        Assert.Equal(Rest, PlayfulScarePerformance.Sample(double.NaN, Rest));
        Assert.Equal(Rest, PlayfulScarePerformance.Sample(-1, Rest));
    }

    [Fact]
    public void MotionCanBeDisabledWithoutLosingTheFacialPerformance()
    {
        FacePose pose = PlayfulScarePerformance.Sample(6.1, Rest, 0);
        Assert.Equal(0, pose.MotionX); Assert.Equal(0, pose.MotionY); Assert.Equal(0, pose.MotionRoll);
        Assert.True(pose.MouthRoundness > .8);
        Assert.True(pose.LeftBrowTension < -.3);
    }

    [Fact]
    public void WinkClosesOnlyOneEyeAndTransitionHasNoLargeFrameJumps()
    {
        FacePose wink = PlayfulScarePerformance.Sample(11.4, Rest);
        Assert.True(wink.LeftEyelidOpen < .01);
        Assert.True(wink.RightEyelidOpen > .5);
        for (double t = .01; t <= 13.5; t += .01)
        {
            FacePose a = PlayfulScarePerformance.Sample(t - .01, Rest), b = PlayfulScarePerformance.Sample(t, Rest);
            Assert.InRange(Math.Abs(a.MotionY - b.MotionY), 0, .04);
            Assert.InRange(Math.Abs(a.LeftGazeX - b.LeftGazeX), 0, .09);
        }
    }
}
