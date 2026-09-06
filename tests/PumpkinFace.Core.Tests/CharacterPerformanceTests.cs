using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class CharacterPerformanceTests
{
    [Fact]
    public void SwitchingRetiresOldActionsAndKeepsOperatorSettings()
    {
        PerformanceController controller = new(42);
        controller.Handle(new SetBehaviorStateCommand(BehaviorState.Listening));
        controller.Handle(new SetMotionAmountCommand(.4f));
        Guid gesture = Guid.NewGuid(), gaze = Guid.NewGuid();
        controller.Handle(new PlayGestureCommand(gesture, GestureId.Nod));
        controller.Handle(new SetGazeTargetCommand(gaze, .7f, -.2f, 5));
        controller.Update(.3);
        FacePose before = controller.Frame;
        controller.Handle(new SelectCharacterCommand(CharacterCatalog.PipId));
        Assert.Equal(before, controller.Frame);
        Assert.Equal(CharacterCatalog.PipId, controller.Snapshot.CharacterId);
        Assert.Equal(BehaviorState.Listening, controller.State);
        Assert.Equal(.4f, controller.MotionAmount);
        Assert.All(controller.Snapshot.Requests, r => Assert.Equal(PerformanceOutcome.Cancelled, r.Outcome));
        controller.Update(.25);
        Assert.Equal(default, controller.Frame);
        controller.Handle(new SetBehaviorStateCommand(null));
        controller.Handle(new SelectCharacterCommand(CharacterCatalog.DefaultId));
        Assert.Null(controller.State);
    }

    [Fact]
    public void UnknownAndRepeatedSelectionsDoNotInterruptAnAction()
    {
        PerformanceController controller = new();
        Guid id = Guid.NewGuid();
        controller.Handle(new PlayGestureCommand(id, GestureId.CuriousTilt));
        controller.Update(.4);
        FacePose before = controller.Frame;
        controller.Handle(new SelectCharacterCommand("missing"));
        controller.Handle(new SelectCharacterCommand(CharacterCatalog.DefaultId));
        Assert.Equal(before, controller.Frame);
        Assert.Equal(PerformanceOutcome.Running, controller.Snapshot.Requests.Single().Outcome);
    }

    [Fact]
    public void PersonalitiesDifferDuringIdleButRespectIdenticalExplicitGestures()
    {
        var jack = new PerformanceController(42);
        var pip = new PerformanceController(42);
        pip.Handle(new SelectCharacterCommand(CharacterCatalog.PipId));
        foreach (var controller in new[] { jack, pip }) controller.Handle(new PlayGestureCommand(Guid.NewGuid(), GestureId.Nod));
        for (int i = 0; i < 120; i++)
        {
            jack.Update(1d / 60); pip.Update(1d / 60);
            Assert.Equal(jack.Frame, pip.Frame);
        }
        foreach (var controller in new[] { jack, pip }) controller.Handle(new SetBehaviorStateCommand(BehaviorState.Idle));
        jack.Update(1); pip.Update(1);
        Assert.NotEqual(jack.Frame.LeftGazeX, pip.Frame.LeftGazeX);
        Assert.True(Math.Abs(pip.Frame.LeftGazeX) < Math.Abs(jack.Frame.LeftGazeX));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)] [InlineData(0)]
    public void PipIdleRemainsDeterministicAcrossFrameIntervals(int fps)
    {
        var expected = new PerformanceController(91);
        var actual = new PerformanceController(91);
        foreach (var controller in new[] { expected, actual })
        {
            controller.Handle(new SelectCharacterCommand(CharacterCatalog.PipId));
            controller.Handle(new SetBehaviorStateCommand(BehaviorState.Idle));
        }
        double[] irregular = [.017, .041, .008, .023, .06];
        double elapsed = 0;
        for (int i = 0; elapsed < 24; i++)
        {
            double dt = Math.Min(24 - elapsed, fps == 0 ? irregular[i % irregular.Length] : 1d / fps);
            actual.Update(dt); elapsed += dt;
        }
        expected.Update(24);
        Assert.Equal(expected.Frame, actual.Frame);
    }
}
