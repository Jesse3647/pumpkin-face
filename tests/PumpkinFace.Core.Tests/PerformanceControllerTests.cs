using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class PerformanceControllerTests
{
    private static Guid Play(PerformanceController controller, GestureId gesture, float intensity = .65f)
    {
        Guid id = Guid.NewGuid();
        controller.Handle(new PlayGestureCommand(id, gesture, intensity));
        return id;
    }
    private static PerformanceOutcome Outcome(PerformanceController c, Guid id) =>
        c.Snapshot.Requests.Single(r => r.RequestId == id).Outcome;

    [Fact]
    public void InactiveLayersPreserveIndependentExpressionControls()
    {
        FacePose expression = FacePose.Neutral with { LeftGazeX = .3f, RightGazeY = -.2f,
            LeftEyelidOpen = .8f, RightEyelidOpen = .6f, JawOpen = .3f, LeftBrowTension = -.5f };
        PerformanceController controller = new();
        Assert.Equal(expression, controller.ComposePose(expression));
        Assert.Equal(expression, SpeechPoseCompositor.Compose(expression, default));
    }

    [Fact]
    public void LeftWinkAndNodOverlapWithoutClosingRightEye()
    {
        PerformanceController c = new();
        Guid wink = Play(c, GestureId.LeftWink), nod = Play(c, GestureId.Nod);
        c.Update(.2);
        FacePose pose = c.ComposePose(FacePose.Neutral);
        Assert.True(pose.LeftEyelidOpen < .05);
        Assert.Equal(1, pose.RightEyelidOpen);
        Assert.NotEqual(0, pose.MotionY);
        Assert.Equal(PerformanceOutcome.Running, Outcome(c, wink));
        Assert.Equal(PerformanceOutcome.Running, Outcome(c, nod));
        c.Update(1);
        Assert.Equal(PerformanceOutcome.Completed, Outcome(c, wink));
        Assert.Equal(PerformanceOutcome.Completed, Outcome(c, nod));
    }

    [Fact]
    public void BlinkClosesFastHoldsAndReopensMoreSlowly()
    {
        PerformanceController c = new();
        Play(c, GestureId.Blink);
        c.Update(.075);
        Assert.True(c.ComposePose(FacePose.Neutral).LeftEyelidOpen < .01);
        c.Update(.025);
        Assert.True(c.ComposePose(FacePose.Neutral).LeftEyelidOpen < .02);
        c.Update(.1);
        Assert.InRange(c.ComposePose(FacePose.Neutral).LeftEyelidOpen, .4f, .9f);
        c.Update(.09);
        Assert.Equal(FacePose.Neutral, c.ComposePose(FacePose.Neutral));
    }

    [Fact]
    public void ConflictingGestureStartsAtCurrentPoseAndCancelsPrevious()
    {
        PerformanceController c = new();
        Guid first = Play(c, GestureId.CuriousTilt);
        c.Update(.5);
        FacePose before = c.Frame;
        Guid second = Play(c, GestureId.Shake);
        Assert.Equal(before, c.Frame);
        Assert.Equal(PerformanceOutcome.Cancelled, Outcome(c, first));
        c.Update(1.3);
        Assert.Equal(PerformanceOutcome.Completed, Outcome(c, second));
        Assert.Equal(default, c.Frame);
    }

    [Fact]
    public void ExplicitGazeCannotBeReplacedByIdleAttention()
    {
        PerformanceController c = new();
        c.Handle(new SetBehaviorStateCommand(BehaviorState.Idle));
        Guid id = Guid.NewGuid();
        c.Handle(new SetGazeTargetCommand(id, .8f, -.3f, 8));
        c.Update(4);
        Assert.Equal(.8f, c.Frame.LeftGazeX);
        Assert.Equal(-.3f, c.Frame.RightGazeY);
        Assert.Equal(PerformanceOutcome.Running, Outcome(c, id));
    }

    [Fact]
    public void DirectedLookLeadsDelayedFaceResponseAndSettles()
    {
        PerformanceController c = new();
        Guid id = Guid.NewGuid();
        c.Handle(new SetGazeTargetCommand(id, .8f, .3f, 1));
        c.Update(.075);
        Assert.True(c.Frame.LeftGazeX > .2);
        Assert.Equal(0, c.Frame.MotionX);
        c.Update(.4);
        Assert.True(c.Frame.MotionX > 0);
        c.Update(1.1);
        Assert.Equal(default, c.Frame);
        Assert.Equal(PerformanceOutcome.Completed, Outcome(c, id));
    }

    [Fact]
    public void StopCancelsAndSettlesWithoutRestartingIdle()
    {
        PerformanceController c = new();
        c.Handle(new SetBehaviorStateCommand(BehaviorState.Thinking));
        Guid id = Play(c, GestureId.Surprise);
        c.Update(.4);
        FacePose before = c.Frame;
        c.Handle(new StopCommand());
        Assert.Equal(before, c.Frame);
        Assert.Null(c.State);
        Assert.Equal(PerformanceOutcome.Cancelled, Outcome(c, id));
        c.Update(.25);
        Assert.Equal(default, c.Frame);
        c.Update(20);
        Assert.Equal(default, c.Frame);
    }

    [Fact]
    public void CancellingOneCompatibleGestureLeavesOtherRunning()
    {
        PerformanceController c = new();
        Guid wink = Play(c, GestureId.LeftWink), nod = Play(c, GestureId.Nod);
        c.Update(.1);
        c.Handle(new CancelPerformanceCommand(wink));
        c.Update(.25);
        Assert.Equal(0, c.Frame.LeftEyelidOpen);
        Assert.Equal(PerformanceOutcome.Running, Outcome(c, nod));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void SeededPerformanceMatchesAcrossFrameRates(int fps)
    {
        PerformanceController expected = new(17), actual = new(17);
        expected.Handle(new SetBehaviorStateCommand(BehaviorState.Idle));
        actual.Handle(new SetBehaviorStateCommand(BehaviorState.Idle));
        for (int second = 0; second < 30; second++)
        {
            expected.Update(1);
            for (int frame = 0; frame < fps; frame++) actual.Update(1d / fps);
            Assert.Equal(expected.Frame, actual.Frame);
        }
    }

    [Fact]
    public void IrregularFramesMatchAndInvalidElapsedIsIgnored()
    {
        PerformanceController a = new(23), b = new(23);
        a.Handle(new SetBehaviorStateCommand(BehaviorState.Thinking));
        b.Handle(new SetBehaviorStateCommand(BehaviorState.Thinking));
        for (int i = 0; i < 50; i++) { a.Update(.007); a.Update(.113); a.Update(.38); b.Update(.5); Assert.Equal(b.Frame, a.Frame); }
        a.Update(double.NaN); a.Update(double.PositiveInfinity); a.Update(-1);
        Assert.Equal(b.Frame, a.Frame);
    }

    [Fact]
    public void ZeroMotionSuppressesMotionAndTrembleWithoutSuppressingFace()
    {
        PerformanceController c = new();
        c.Handle(new SetMotionAmountCommand(0));
        Play(c, GestureId.Surprise, 1);
        c.Update(.4);
        FacePose pose = c.ComposePose(FacePose.Neutral with { Tremble = .5f });
        Assert.Equal(0, pose.MotionX); Assert.Equal(0, pose.MotionY); Assert.Equal(0, pose.MotionRoll); Assert.Equal(0, pose.Tremble);
        Assert.True(pose.JawOpen > 0);
    }

    [Fact]
    public void MotionClampsAndInterpolates()
    {
        FacePose pose = (FacePose.Neutral with { MotionX = 5, MotionY = -3, MotionRoll = float.NaN }).Clamp();
        Assert.Equal(1, pose.MotionX); Assert.Equal(-1, pose.MotionY); Assert.Equal(0, pose.MotionRoll);
        Assert.Equal(.5f, FacePose.Lerp(FacePose.Neutral, pose, .5f, FacePoseChannels.Motion).MotionX);
    }

    [Fact]
    public void SpeechOwnsOnlyItsMouthControls()
    {
        FacePose face = FacePose.Neutral with { LeftGazeX = .5f, LeftEyelidOpen = .2f, LeftMouthCorner = .6f, MotionRoll = .3f };
        FacePose talking = SpeechPoseCompositor.Compose(face, new(.7f, .2f, .9f, 1));
        Assert.Equal(.7f, talking.JawOpen); Assert.Equal(.9f, talking.MouthRoundness);
        Assert.Equal(face.LeftMouthCorner, talking.LeftMouthCorner);
        Assert.Equal(face.LeftGazeX, talking.LeftGazeX);
        Assert.Equal(face.LeftEyelidOpen, talking.LeftEyelidOpen);
        Assert.Equal(face.MotionRoll, talking.MotionRoll);
        Assert.Equal(face, SpeechPoseCompositor.Compose(face, new(.7f, .2f, .9f, 0)));
    }

    [Fact]
    public void QueueRejectionAndInvalidRequestsAreObservable()
    {
        PerformanceController c = new();
        BoundedAnimationCommandQueue queue = new(1);
        PerformanceCommandEndpoint endpoint = new(queue, c);
        Assert.True(endpoint.TryPost(new NextEmotionCommand()));
        Guid full = Guid.NewGuid();
        Assert.False(endpoint.TryPost(new PlayGestureCommand(full, GestureId.Nod)));
        Assert.Equal(PerformanceOutcome.Rejected, Outcome(c, full));
        Guid invalid = Play(c, GestureId.Nod, float.NaN);
        Assert.Equal(PerformanceOutcome.Rejected, Outcome(c, invalid));
        Guid gaze = Guid.NewGuid();
        c.Handle(new SetGazeTargetCommand(gaze, 2, 0));
        Assert.Equal(PerformanceOutcome.Rejected, Outcome(c, gaze));
        Assert.Equal(default, c.Frame);
    }

    [Fact]
    public void SnapshotIsImmutableAndDuplicateRequestsDoNotRetrigger()
    {
        PerformanceController c = new();
        Guid id = Play(c, GestureId.Nod);
        PerformanceSnapshot snapshot = c.Snapshot;
        c.Update(1.2);
        c.Handle(new PlayGestureCommand(id, GestureId.Nod));
        Assert.Equal(PerformanceOutcome.Running, snapshot.Requests.Single().Outcome);
        Assert.Equal(PerformanceOutcome.Completed, Outcome(c, id));
        Assert.Equal(default, c.Frame);
    }

    [Fact]
    public void DemoCanBeRepeatedWithIdenticalOutput()
    {
        PerformanceController c = new();
        List<FacePose> samples = [];
        c.Handle(new PlayPerformanceDemoCommand());
        for (int i = 0; i < 170; i++) { c.Update(.1); samples.Add(c.Frame); }
        c.Handle(new PlayPerformanceDemoCommand());
        for (int i = 0; i < samples.Count; i++) { c.Update(.1); Assert.Equal(samples[i], c.Frame); }
    }

    [Fact]
    public void CancelledSpeechPreparationCannotPlayLateSuccessOrFailure()
    {
        SpeechPreparationGate gate = new();
        long cancelled = gate.Begin();
        gate.Cancel();
        Assert.False(gate.Accept(cancelled));
        long current = gate.Begin();
        Assert.False(gate.Accept(cancelled));
        Assert.True(gate.Accept(current));
    }
}
