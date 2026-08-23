using PumpkinFace.Core;
using PumpkinFace.Display.Animation;

namespace PumpkinFace.Display.Tests;

public sealed class ActionSceneControllerTests
{
    [Fact]
    public void Looking_VisitsBoundedTargetsAndRepeatsWhileSelected()
    {
        ActionSceneController controller = new(seed: 12345);
        controller.SetSelected(SceneId.Looking, true);

        bool moved = false;
        for (int step = 0; step < 300; step++)
        {
            controller.Update(0.05);
            moved |= controller.Frame.Gaze.Length() > 0.10f;
            Assert.InRange(controller.Frame.Gaze.X, -0.91f, 0.91f);
            Assert.InRange(controller.Frame.Gaze.Y, -0.73f, 0.73f);
        }

        Assert.True(moved);
        Assert.Contains(SceneId.Looking, controller.ActiveScenes);
    }

    [Fact]
    public void MultipleSelections_ComposeIndependentChannels()
    {
        ActionSceneController controller = new(seed: 31);
        controller.SetSelected(SceneId.Looking, true);
        controller.SetSelected(SceneId.Blinking, true);
        controller.SetSelected(SceneId.Talking, true);

        bool looked = false;
        bool blinked = false;
        bool talked = false;
        for (int step = 0; step < 60; step++)
        {
            controller.Update(0.05);
            looked |= controller.Frame.Gaze.Length() > 0.10f;
            blinked |= controller.Frame.EyelidOpen < 0.5f;
            talked |= controller.Frame.JawOpen > 0.10f;
        }

        Assert.True(looked);
        Assert.True(blinked);
        Assert.True(talked);
        Assert.Equal(3, controller.SelectedScenes.Count);
        Assert.Equal(3, controller.ActiveScenes.Count);
    }

    [Fact]
    public void DeselectingOneScene_LeavesOtherSelectionsRunning()
    {
        ActionSceneController controller = new(seed: 18);
        controller.SetSelected(SceneId.Looking, true);
        controller.SetSelected(SceneId.Talking, true);
        controller.Update(0.5);

        controller.SetSelected(SceneId.Talking, false);
        controller.Update(0.5);

        Assert.True(controller.IsSelected(SceneId.Looking));
        Assert.False(controller.IsSelected(SceneId.Talking));
        Assert.Equal(0f, controller.Frame.JawOpen);
        Assert.Contains(SceneId.Looking, controller.ActiveScenes);
    }

    [Fact]
    public void CandleSputter_ComposesWithPerformanceActions()
    {
        ActionSceneController controller = new(seed: 44);
        controller.SetSelected(SceneId.Talking, true);
        controller.SetSelected(SceneId.CandleSputter, true);

        bool talked = false;
        bool sputtered = false;
        for (int step = 0; step < 50; step++)
        {
            controller.Update(0.05);
            talked |= controller.Frame.JawOpen > 0.10f;
            sputtered |= Math.Abs(controller.Frame.LightingMultiplier - 1f) > 0.05f;
        }

        Assert.True(talked);
        Assert.True(sputtered);
    }

    [Fact]
    public void Talking_PerformsHappyHalloweenWithDistinctVisemeShapes()
    {
        ActionSceneController controller = new(seed: 12);
        controller.SetSelected(SceneId.Talking, true);

        bool sawClosedConsonant = false;
        bool sawWideVowel = false;
        bool sawRoundedO = false;
        for (int step = 0; step < 81; step++)
        {
            controller.Update(0.05);
            ActionSceneFrame frame = controller.Frame;
            Assert.True(frame.SpeechActive);
            sawClosedConsonant |= frame.JawOpen < 0.05f;
            sawWideVowel |= frame.MouthWidth > 0.85f && frame.JawOpen > 0.25f;
            sawRoundedO |= frame.MouthRoundness > 0.85f &&
                           frame.MouthWidth < 0.45f &&
                           frame.JawOpen > 0.30f;
        }

        Assert.True(sawClosedConsonant);
        Assert.True(sawWideVowel);
        Assert.True(sawRoundedO);
        Assert.Contains(SceneId.Talking, controller.ActiveScenes);

        controller.Update(0.05);

        Assert.DoesNotContain(SceneId.Talking, controller.ActiveScenes);
        Assert.False(controller.IsSelected(SceneId.Talking));
        Assert.Equal(ActionSceneFrame.Rest, controller.Frame);
    }

    [Fact]
    public void Talking_AcceptsAVisemePlanForACustomPhrase()
    {
        ActionSceneController controller = new(seed: 5);
        VisemeFrame[] phrase =
        [
            new(TimeSpan.Zero, Viseme.Silence, 1f),
            new(TimeSpan.FromSeconds(0.2), Viseme.Ah, 1f),
            new(TimeSpan.FromSeconds(0.5), Viseme.Oh, 1f),
            new(TimeSpan.FromSeconds(0.8), Viseme.Silence, 1f),
        ];
        controller.ConfigureSpeech(phrase, TimeSpan.FromSeconds(0.8));
        controller.SetSelected(SceneId.Talking, true);

        controller.Update(0.55);

        Assert.True(controller.Frame.SpeechActive);
        Assert.True(controller.Frame.MouthRoundness > 0.70f);

        controller.Update(0.30);

        Assert.False(controller.IsSelected(SceneId.Talking));
        Assert.Equal(ActionSceneFrame.Rest, controller.Frame);
    }

    [Fact]
    public void ExternallyTimedSpeech_EasesBackToTheExpressionBeforeClearing()
    {
        ActionSceneController controller = new(seed: 8) { ExternalSpeechCompletion = true };
        controller.SetSelected(SceneId.Talking, true);
        controller.Update(0.5);

        controller.BeginSpeechRelease();
        controller.Update(0.18);

        Assert.True(controller.IsSelected(SceneId.Talking));
        Assert.InRange(controller.Frame.SpeechBlend, 0.35f, 0.65f);
        Assert.False(controller.Frame.SpeechActive);

        controller.Update(0.20);

        Assert.False(controller.IsSelected(SceneId.Talking));
        Assert.Equal(ActionSceneFrame.Rest, controller.Frame);
    }

    [Fact]
    public void Stop_ClearsEverySelectionAndChannel()
    {
        ActionSceneController controller = new(seed: 77);
        controller.SetSelected(SceneId.Looking, true);
        controller.SetSelected(SceneId.Blinking, true);
        controller.Update(1.0);

        controller.Stop();

        Assert.Empty(controller.SelectedScenes);
        Assert.Empty(controller.ActiveScenes);
        Assert.Equal(ActionSceneFrame.Rest, controller.Frame);
    }

    [Fact]
    public void AutoplayStartsOneOrMoreActionsWithoutManualSelections()
    {
        ActionSceneController controller = new(seed: 9);
        controller.SetAutoplay(true);

        controller.Update(2.5);

        Assert.True(controller.AutoplayEnabled);
        Assert.NotEmpty(controller.ActiveScenes);
        Assert.Empty(controller.SelectedScenes);
    }

    [Fact]
    public void ManualSelectionDisablesAutoplayAndReplacesItsCombination()
    {
        ActionSceneController controller = new(seed: 9);
        controller.SetAutoplay(true);
        controller.Update(2.5);

        controller.SetSelected(SceneId.Talking, true);

        Assert.False(controller.AutoplayEnabled);
        Assert.Single(controller.ActiveScenes);
        Assert.True(controller.IsSelected(SceneId.Talking));
    }
}
