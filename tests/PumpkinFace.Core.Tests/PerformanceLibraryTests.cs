using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class PerformanceLibraryTests
{
    [Fact]
    public void EveryPerformanceIsBoundedReproducibleAndReturnsToRest()
    {
        foreach (var clip in PerformanceLibrary.All)
        {
            Assert.Equal(FacePose.Neutral, PerformanceLibrary.Sample(clip.Id, 0, FacePose.Neutral));
            Assert.Equal(FacePose.Neutral, PerformanceLibrary.Sample(clip.Id, clip.Duration, FacePose.Neutral));
            for (double time = 0; time < clip.Duration; time += .017)
            {
                FacePose pose = PerformanceLibrary.Sample(clip.Id, time, FacePose.Neutral);
                Assert.Equal(pose.Clamp(), pose);
                Assert.Equal(pose, PerformanceLibrary.Sample(clip.Id, time, FacePose.Neutral));
                FacePose still = PerformanceLibrary.Sample(clip.Id, time, FacePose.Neutral, 0);
                Assert.Equal(0, still.MotionX); Assert.Equal(0, still.MotionY); Assert.Equal(0, still.MotionRoll);
            }
        }
    }

    [Fact]
    public void CuriosityHasNoHumMouthAndMusicDoesNotPretendToSing()
    {
        for (double time = 2.4; time < 3.7; time += .01)
            Assert.Equal(0, PlayfulScarePerformance.Sample(time, FacePose.Neutral).SpeechBlend);
        foreach (string id in new[] { "organ", PerformanceLibrary.SongId, "wink" })
            for (double time = 0; time < 20; time += .01)
            {
                FacePose pose = PerformanceLibrary.Sample(id, time, FacePose.Neutral);
                Assert.Equal(0, pose.SpeechBlend);
                Assert.Equal(0, pose.JawOpen);
            }
    }

    [Fact]
    public void ShortClipsKeepTheirOwnVocalShapeAndDoNotIncludeTheOtherReaction()
    {
        FacePose boo = PerformanceLibrary.Sample("boo", 1.1, FacePose.Neutral);
        FacePose laugh = PerformanceLibrary.Sample("chuckle", .6, FacePose.Neutral);
        Assert.True(boo.MouthRoundness > .8);
        Assert.True(laugh.MouthWidth > .7);
        Assert.True(laugh.MouthRoundness < .2);
        Assert.Equal(FacePose.Neutral, PerformanceLibrary.Sample("missing", 1, FacePose.Neutral));
        Assert.Equal(FacePose.Neutral, PerformanceLibrary.Sample(PerformanceLibrary.SongId, double.NaN, FacePose.Neutral));
        Assert.Equal(FacePose.Neutral, PerformanceLibrary.Sample(PerformanceLibrary.SongId, 9, FacePose.Neutral, songDuration: 9));
    }

    [Fact]
    public void VaderStaresAndBreathesWithoutTalkingThenRestoresTheSelectedExpression()
    {
        FacePose rest = FacePose.Neutral with { LeftMouthCorner = .9f, RightMouthCorner = .9f, LightingIntensity = 1.3f };
        FacePose entrance = PerformanceLibrary.Sample("vader", 2, rest);
        FacePose audience = PerformanceLibrary.Sample("vader", 5, rest);
        Assert.True(entrance.LeftEyelidOpen < .7f && entrance.PupilSize < rest.PupilSize);
        Assert.True(entrance.LeftGazeX < -.2f);
        Assert.Equal(0, audience.LeftGazeX);
        Assert.True(audience.LeftMouthCorner < 0);
        Assert.True(audience.LightingIntensity < rest.LightingIntensity);
        for (double t = .01; t < VaderEntrancePerformance.Duration; t += .01)
        {
            FacePose a = PerformanceLibrary.Sample("vader", t - .01, rest);
            FacePose b = PerformanceLibrary.Sample("vader", t, rest);
            Assert.Equal(0, b.JawOpen);
            Assert.Equal(0, b.SpeechBlend);
            Assert.InRange(Math.Abs(a.MotionY - b.MotionY), 0, .015);
            Assert.InRange(Math.Abs(a.LightingIntensity - b.LightingIntensity), 0, .035);
        }
        Assert.Equal(rest, PerformanceLibrary.Sample("vader", VaderEntrancePerformance.Duration, rest));
    }

    [Fact]
    public void SaberIgnitesSweepsAndShutsDownOnlyDuringVader()
    {
        FacePose sample(double time) => PerformanceLibrary.Sample("vader", time, FacePose.Neutral);
        Assert.Equal(0, sample(VaderEntrancePerformance.SaberIgnition).SaberGlow);
        Assert.True(sample(3.6).SaberGlow > .7f);
        Assert.True(sample(8).SaberSweep > sample(4).SaberSweep);
        Assert.True(sample(VaderEntrancePerformance.SaberShutdown).SaberGlow > .7f);
        Assert.Equal(0, sample(11).SaberGlow);
        Assert.Equal(0, sample(VaderEntrancePerformance.Duration).SaberGlow);
        foreach (var clip in PerformanceLibrary.All.Where(c => c.Id != "vader"))
            Assert.Equal(0, PerformanceLibrary.Sample(clip.Id, clip.Duration / 2, FacePose.Neutral).SaberGlow);
        FacePose release = FacePose.Lerp(sample(5), FacePose.Neutral, .5f);
        Assert.InRange(release.SaberGlow, .3f, .5f);
        Assert.Equal(FacePose.Neutral, FacePose.Lerp(sample(5), FacePose.Neutral, 1));
    }
}
