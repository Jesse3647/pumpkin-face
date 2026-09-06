using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class AttentionPerformanceTests
{
    private static Guid Look(PerformanceController c, float x = .65f, float y = -.2f, double hold = 8)
    {
        Guid id = Guid.NewGuid();
        c.Handle(new SetGazeTargetCommand(id, x, y, hold));
        return id;
    }
    private static PerformanceOutcome Outcome(PerformanceController c, Guid id) => c.Snapshot.Requests.Single(r => r.RequestId == id).Outcome;

    [Theory]
    [InlineData(GestureId.Nod)] [InlineData(GestureId.Shake)] [InlineData(GestureId.CuriousTilt)]
    [InlineData(GestureId.Surprise)] [InlineData(GestureId.Delight)]
    public void GesturesPreserveHeldAttentionAndItsCompletion(GestureId gesture)
    {
        PerformanceController c = new();
        Guid gaze = Look(c);
        c.Update(.6);
        FacePose before = c.Frame;
        Guid action = Guid.NewGuid();
        c.Handle(new PlayGestureCommand(action, gesture));
        Assert.InRange(Math.Abs(before.MotionX - c.Frame.MotionX), 0, .00001f);
        Assert.InRange(Math.Abs(before.MotionY - c.Frame.MotionY), 0, .00001f);
        Assert.Equal(before.LeftGazeX, c.Frame.LeftGazeX);
        for (int i = 0; i < 120; i++)
        {
            c.Update(1d / 60);
            Assert.InRange(c.Frame.LeftGazeX, .632f, .668f);
            Assert.InRange(c.Frame.LeftGazeY, -.212f, -.188f);
            Assert.Equal(PerformanceOutcome.Running, Outcome(c, gaze));
        }
        Assert.Equal(PerformanceOutcome.Completed, Outcome(c, action));
        c.Update(6);
        Assert.Equal(PerformanceOutcome.Completed, Outcome(c, gaze));
        Assert.Equal(default, c.Frame);
    }

    [Fact]
    public void ChangingAndCancellingAttentionDoesNotCancelGestureMotion()
    {
        PerformanceController c = new();
        Guid gesture = Guid.NewGuid();
        c.Handle(new PlayGestureCommand(gesture, GestureId.CuriousTilt));
        c.Update(.25);
        FacePose before = c.Frame;
        Guid first = Look(c);
        Assert.Equal(before, c.Frame);
        c.Update(.3);
        Guid second = Look(c, -.7f, .3f);
        Assert.Equal(PerformanceOutcome.Cancelled, Outcome(c, first));
        Assert.Equal(PerformanceOutcome.Running, Outcome(c, gesture));
        c.Update(.2);
        c.Handle(new CancelPerformanceCommand(second));
        c.Update(.25);
        Assert.Equal(0, c.Frame.LeftGazeX);
        Assert.NotEqual(0, c.Frame.MotionRoll);
        Assert.Equal(PerformanceOutcome.Running, Outcome(c, gesture));
    }

    [Fact]
    public void HeldAttentionHasSmallOccasionalCoherentCorrections()
    {
        PerformanceController c = new(42);
        Look(c, .8f, .4f, 12);
        c.Update(.5);
        int correcting = 0, still = 0;
        for (int i = 0; i < 600; i++)
        {
            c.Update(1d / 60);
            Assert.InRange(c.Frame.LeftGazeX, .782f, .818f);
            Assert.InRange(c.Frame.LeftGazeY, .388f, .412f);
            Assert.Equal(c.Frame.LeftGazeX, c.Frame.RightGazeX);
            Assert.Equal(c.Frame.LeftGazeY, c.Frame.RightGazeY);
            if (Math.Abs(c.Frame.LeftGazeX - .8f) > .0001f) correcting++; else still++;
        }
        Assert.True(correcting > 10);
        Assert.True(still > correcting * 3);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void AttentionSequenceIsIdenticalAcrossFrameRates(int fps)
    {
        PerformanceController a = new(42), b = new(42);
        Look(a, hold: 10); Look(b, hold: 10);
        for (int second = 0; second < 10; second++)
        {
            if (second == 2 || second == 4)
            {
                GestureId gesture = second == 2 ? GestureId.CuriousTilt : GestureId.Nod;
                a.Handle(new PlayGestureCommand(Guid.NewGuid(), gesture));
                b.Handle(new PlayGestureCommand(Guid.NewGuid(), gesture));
            }
            a.Update(.013); a.Update(.087); a.Update(.9);
            for (int frame = 0; frame < fps; frame++) b.Update(1d / fps);
            Assert.Equal(a.Frame, b.Frame);
        }
    }

    [Fact]
    public void StopSettlesAttentionFollowAndGestureTogether()
    {
        PerformanceController c = new();
        Guid gaze = Look(c);
        c.Update(.5);
        c.Handle(new PlayGestureCommand(Guid.NewGuid(), GestureId.CuriousTilt));
        c.Update(.4);
        FacePose before = c.Frame;
        c.Handle(new StopCommand());
        Assert.Equal(before, c.Frame);
        c.Update(.25);
        Assert.Equal(default, c.Frame);
        Assert.Equal(PerformanceOutcome.Cancelled, Outcome(c, gaze));
    }
}
