using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class FacePoseTests
{
    [Fact]
    public void NeutralPose_UsesStableVisibleDefaults()
    {
        var pose = FacePose.Neutral;

        Assert.Equal(0.55f, pose.PupilSize);
        Assert.Equal(1f, pose.LeftEyelidOpen);
        Assert.Equal(1f, pose.RightEyelidOpen);
        Assert.Equal(0.5f, pose.MouthWidth);
        Assert.Equal(1f, pose.LightingIntensity);
        Assert.Equal(pose, pose.Clamp());
    }

    [Fact]
    public void Clamp_ConstrainsEveryChannelAndRepairsNonFiniteValues()
    {
        var pose = new FacePose
        {
            LeftGazeX = -2f,
            LeftGazeY = 2f,
            RightGazeX = float.NaN,
            RightGazeY = float.PositiveInfinity,
            PupilSize = -1f,
            LeftEyelidOpen = 4f,
            RightEyelidOpen = float.NaN,
            LeftBrowTension = -4f,
            RightBrowTension = 4f,
            JawOpen = 2f,
            MouthWidth = -2f,
            MouthRoundness = 5f,
            LeftMouthCorner = -5f,
            RightMouthCorner = 5f,
            Tremble = 9f,
            LightingIntensity = 9f,
        }.Clamp();

        Assert.Equal(-1f, pose.LeftGazeX);
        Assert.Equal(1f, pose.LeftGazeY);
        Assert.Equal(FacePose.Neutral.RightGazeX, pose.RightGazeX);
        Assert.Equal(FacePose.Neutral.RightGazeY, pose.RightGazeY);
        Assert.Equal(0f, pose.PupilSize);
        Assert.Equal(1f, pose.LeftEyelidOpen);
        Assert.Equal(FacePose.Neutral.RightEyelidOpen, pose.RightEyelidOpen);
        Assert.Equal(-1f, pose.LeftBrowTension);
        Assert.Equal(1f, pose.RightBrowTension);
        Assert.Equal(1f, pose.JawOpen);
        Assert.Equal(0f, pose.MouthWidth);
        Assert.Equal(1f, pose.MouthRoundness);
        Assert.Equal(-1f, pose.LeftMouthCorner);
        Assert.Equal(1f, pose.RightMouthCorner);
        Assert.Equal(1f, pose.Tremble);
        Assert.Equal(2f, pose.LightingIntensity);
    }

    [Fact]
    public void Lerp_InterpolatesAllChannelsAndClampsAmount()
    {
        var start = FacePose.Neutral with
        {
            LeftGazeX = -1f,
            RightGazeY = -1f,
            JawOpen = 0f,
            LightingIntensity = 0f,
        };
        var end = FacePose.Neutral with
        {
            LeftGazeX = 1f,
            RightGazeY = 1f,
            JawOpen = 1f,
            LightingIntensity = 2f,
        };

        var halfway = FacePose.Lerp(start, end, 0.5f);

        Assert.Equal(0f, halfway.LeftGazeX);
        Assert.Equal(0f, halfway.RightGazeY);
        Assert.Equal(0.5f, halfway.JawOpen);
        Assert.Equal(1f, halfway.LightingIntensity);
        Assert.Equal(start, FacePose.Lerp(start, end, -1f));
        Assert.Equal(end, FacePose.Lerp(start, end, 2f));
        Assert.Equal(start, FacePose.Lerp(start, end, float.NaN));
    }

    [Fact]
    public void Lerp_OnlyChangesSelectedChannelGroups()
    {
        var start = FacePose.Neutral with
        {
            LeftGazeX = -1f,
            PupilSize = 0.2f,
            JawOpen = 0.1f,
            Tremble = 0.1f,
            LightingIntensity = 0.5f,
        };
        var end = FacePose.Neutral with
        {
            LeftGazeX = 1f,
            PupilSize = 0.9f,
            JawOpen = 0.9f,
            Tremble = 0.9f,
            LightingIntensity = 1.5f,
        };

        var result = FacePose.Lerp(
            start,
            end,
            1f,
            FacePoseChannels.Gaze | FacePoseChannels.Lighting);

        Assert.Equal(end.LeftGazeX, result.LeftGazeX);
        Assert.Equal(end.LightingIntensity, result.LightingIntensity);
        Assert.Equal(start.PupilSize, result.PupilSize);
        Assert.Equal(start.JawOpen, result.JawOpen);
        Assert.Equal(start.Tremble, result.Tremble);
    }
}
