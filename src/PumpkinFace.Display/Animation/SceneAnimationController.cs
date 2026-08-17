using Godot;
using PumpkinFace.Core;
using GodotAnimation = Godot.Animation;

namespace PumpkinFace.Display.Animation;

/// <summary>
/// Builds the v1 AnimationPlayer clips and AnimationTree at runtime so the
/// authored channel layout remains explicit, editable, and speech-layer ready.
/// </summary>
public sealed partial class SceneAnimationController : Node
{
    private const string NeutralAnimation = "Neutral";
    private const string NeutralReferenceAnimation = "NeutralReference";
    private const string ExpressionRestState = "ExpressionRest";
    private const string BaselineClipNode = "NeutralBaseline";
    private const string BaselineRateNode = "NeutralBaselineRate";
    private const string ExpressionStateMachine = "Expressions";
    private const string ExpressionReferenceNode = "ExpressionReferencePose";
    private const string ExpressionDeltaNode = "ExpressionDelta";
    private const string ExpressionMixNode = "BaselinePlusExpression";
    private const string SpeechRestNode = "SpeechRest";
    private const string SpeechMixNode = "SpeechMouthLayer";
    private const string RetriggerStateSuffix = "Retrigger";

    private static readonly SceneId[] Scenes = Enum.GetValues<SceneId>();
    private static readonly string[] PoseProperties =
    [
        nameof(FacePoseDriver.LeftGazeX),
        nameof(FacePoseDriver.LeftGazeY),
        nameof(FacePoseDriver.RightGazeX),
        nameof(FacePoseDriver.RightGazeY),
        nameof(FacePoseDriver.PupilSize),
        nameof(FacePoseDriver.LeftEyelidOpen),
        nameof(FacePoseDriver.RightEyelidOpen),
        nameof(FacePoseDriver.LeftBrowTension),
        nameof(FacePoseDriver.RightBrowTension),
        nameof(FacePoseDriver.JawOpen),
        nameof(FacePoseDriver.MouthWidth),
        nameof(FacePoseDriver.MouthRoundness),
        nameof(FacePoseDriver.LeftMouthCorner),
        nameof(FacePoseDriver.RightMouthCorner),
        nameof(FacePoseDriver.Tremble),
        nameof(FacePoseDriver.LightingIntensity),
    ];
    private static readonly FacePose ExpressionReferencePose = new()
    {
        PupilSize = 0.55f,
        LeftEyelidOpen = 1f,
        RightEyelidOpen = 1f,
        JawOpen = 0.16f,
        MouthWidth = 0.52f,
        MouthRoundness = 0.12f,
        LightingIntensity = 0.94f,
    };

    private readonly Dictionary<string, GodotAnimation> _animations = [];
    private readonly Dictionary<string, AnimationNodeAnimation> _expressionClips = [];

    private FacePoseDriver? _driver;
    private AnimationPlayer? _player;
    private AnimationTree? _tree;
    private AnimationNodeStateMachinePlayback? _playback;
    private string _currentState = NeutralAnimation;
    private SceneId? _currentScene;
    private PendingSceneRequest? _pendingScene;
    private SceneId? _lastSnapshotScene;
    private TimeSpan _lastSnapshotPhaseElapsed;
    private long _lastHandledSceneStartRevision = -1;

    public FacePose CurrentPose => _driver?.ToPose() ?? FacePose.Neutral;

    public override void _Ready()
    {
        _driver = new FacePoseDriver { Name = "PoseDriver" };
        _player = new AnimationPlayer { Name = "AnimationPlayer" };
        _tree = new AnimationTree { Name = "AnimationTree" };
        AddChild(_driver);
        AddChild(_player);
        AddChild(_tree);

        BuildAnimationLibrary();
        BuildAnimationTree();
        StartNeutral(immediate: true);
    }

    public void ApplyTransition(SceneDirectorTransition transition)
    {
        switch (transition.Kind)
        {
            case SceneTransitionKind.SceneStarted when transition.Scene is { } scene:
                // Commands are drained in batches. Coalescing here means several
                // button presses in one render frame produce one transition to
                // the final requested scene instead of conflicting travel paths.
                _lastHandledSceneStartRevision = transition.Snapshot.Revision;
                _pendingScene = new PendingSceneRequest(scene, transition.Snapshot.Revision);
                break;
            case SceneTransitionKind.SceneCompleted:
            case SceneTransitionKind.Stopped:
                _pendingScene = null;
                StartNeutral(immediate: false);
                break;
        }
    }

    public void Synchronize(SceneDirectorSnapshot snapshot)
    {
        if (_tree is null || _playback is null)
        {
            return;
        }

        SceneId? expectedScene = snapshot.State == SceneDirectorState.Playing
            ? snapshot.CurrentScene
            : null;

        if (expectedScene is null)
        {
            _pendingScene = null;
            if (_currentScene is not null)
            {
                StartNeutral(immediate: false);
            }
        }
        else
        {
            bool elapsedRewound = expectedScene == _lastSnapshotScene &&
                snapshot.PhaseElapsed + TimeSpan.FromMilliseconds(1) < _lastSnapshotPhaseElapsed;
            bool missedSameSceneStart = elapsedRewound &&
                snapshot.Revision != _lastHandledSceneStartRevision;
            bool pendingMatches = _pendingScene is { } pending && pending.Scene == expectedScene;

            if (missedSameSceneStart || (_currentScene != expectedScene && !pendingMatches))
            {
                _lastHandledSceneStartRevision = snapshot.Revision;
                _pendingScene = new PendingSceneRequest(expectedScene.Value, snapshot.Revision);
            }

            TryStartPendingScene(snapshot);
        }

        _lastSnapshotScene = expectedScene;
        _lastSnapshotPhaseElapsed = snapshot.PhaseElapsed;
    }

    /// <summary>
    /// Selects an exact normalized point in a scene without advancing the engine
    /// clock. Used by the deterministic visual-capture harness.
    /// </summary>
    public void SetCaptureFrame(SceneId? scene, double normalizedProgress)
    {
        if (_tree is null || _driver is null)
        {
            throw new InvalidOperationException("The animation controller is not ready.");
        }

        _tree.Active = false;
        string animationName = scene?.ToString() ?? NeutralAnimation;
        double progress = Math.Clamp(normalizedProgress, 0d, 1d);
        GodotAnimation expression = scene is { }
            ? _animations[animationName]
            : _animations[NeutralReferenceAnimation];
        FacePose composed = ComposeCapturePose(
            _animations[NeutralAnimation],
            expression,
            progress);
        _driver.ApplyPose(composed);
        _currentState = animationName;
        _currentScene = scene;
    }

    private void TryStartPendingScene(SceneDirectorSnapshot snapshot)
    {
        if (_pendingScene is not { } pending || _playback is null)
        {
            return;
        }

        if (snapshot.State != SceneDirectorState.Playing ||
            snapshot.CurrentScene != pending.Scene ||
            snapshot.Revision < pending.Revision)
        {
            _pendingScene = null;
            return;
        }

        double fadeLength = _playback.GetFadingLength();
        double fadePosition = _playback.GetFadingPosition();
        if (fadeLength > 0d && fadePosition + 0.0001d < fadeLength)
        {
            return;
        }

        _pendingScene = null;
        StartSceneNow(pending.Scene, snapshot.PhaseRemaining);
    }

    private void StartSceneNow(SceneId scene, TimeSpan remainingDuration)
    {
        if (_tree is null || _playback is null)
        {
            return;
        }

        bool useRetriggerState = _currentScene == scene &&
            !_currentState.EndsWith(RetriggerStateSuffix, StringComparison.Ordinal);
        _currentState = SceneStateName(scene, useRetriggerState);
        _currentScene = scene;
        // When a request waited for an in-flight crossfade, stretching the full
        // normalized clip over the remaining scheduler time keeps its ending
        // synchronized without snapping or accumulating timing drift.
        double seconds = Math.Max(0.01, remainingDuration.TotalSeconds);
        _expressionClips[_currentState].TimelineLength = seconds;
        _playback.Travel(_currentState);
    }

    private void StartNeutral(bool immediate)
    {
        if (_tree is null || _playback is null)
        {
            return;
        }

        _currentState = NeutralAnimation;
        _currentScene = null;
        if (immediate)
        {
            _playback.Start(ExpressionRestState, true);
        }
        else
        {
            _playback.Travel(ExpressionRestState);
        }
    }

    private void BuildAnimationLibrary()
    {
        _animations[NeutralAnimation] = BuildNeutral();
        _animations[NeutralReferenceAnimation] = BuildNeutralReference();
        _animations[SceneId.Watchful.ToString()] = BuildWatchful();
        _animations[SceneId.Frightened.ToString()] = BuildFrightened();
        _animations[SceneId.Drowsy.ToString()] = BuildDrowsy();
        _animations[SceneId.Mischievous.ToString()] = BuildMischievous();
        _animations["SpeechRest"] = BuildSpeechRest();

        AnimationLibrary library = new();
        foreach ((string name, GodotAnimation animation) in _animations)
        {
            library.AddAnimation(name, animation);
        }

        _player!.AddAnimationLibrary(string.Empty, library);
    }

    private void BuildAnimationTree()
    {
        AnimationNodeStateMachine stateMachine = new();
        stateMachine.AddNode(
            ExpressionRestState,
            ClipNode(NeutralReferenceAnimation),
            new Vector2(0, 0));
        List<string> expressionStates = [ExpressionRestState];
        for (int index = 0; index < Scenes.Length; index++)
        {
            SceneId scene = Scenes[index];
            for (int variant = 0; variant < 2; variant++)
            {
                string stateName = SceneStateName(scene, variant == 1);
                AnimationNodeAnimation clip = ClipNode(scene.ToString());
                clip.UseCustomTimeline = true;
                clip.TimelineLength = 1.0;
                clip.StretchTimeScale = true;
                clip.LoopMode = GodotAnimation.LoopModeEnum.None;
                _expressionClips[stateName] = clip;
                expressionStates.Add(stateName);
                stateMachine.AddNode(stateName, clip, new Vector2(240 + variant * 260, index * 100));
            }
        }

        foreach (string from in expressionStates)
        {
            foreach (string to in expressionStates)
            {
                if (from == to)
                {
                    continue;
                }

                AnimationNodeStateMachineTransition transition = new()
                {
                    XfadeTime = 0.25f,
                    SwitchMode = AnimationNodeStateMachineTransition.SwitchModeEnum.Immediate,
                    AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Enabled,
                    Reset = true,
                };
                stateMachine.AddTransition(from, to, transition);
            }
        }

        AnimationNodeBlendTree tree = new();
        tree.AddNode(BaselineClipNode, ClipNode(NeutralAnimation), new Vector2(-900, -240));
        tree.AddNode(BaselineRateNode, new AnimationNodeTimeScale(), new Vector2(-680, -240));
        tree.AddNode(ExpressionStateMachine, stateMachine, new Vector2(-900, 0));
        tree.AddNode(
            ExpressionReferenceNode,
            ClipNode(NeutralReferenceAnimation),
            new Vector2(-900, 180));

        AnimationNodeSub2 expressionDelta = new();
        tree.AddNode(ExpressionDeltaNode, expressionDelta, new Vector2(-620, 0));

        AnimationNodeAdd2 expressionLayer = new();
        tree.AddNode(ExpressionMixNode, expressionLayer, new Vector2(-360, -120));
        tree.AddNode(SpeechRestNode, ClipNode(SpeechRestNode), new Vector2(-360, 160));

        AnimationNodeAdd2 speechLayer = new();
        speechLayer.SetFilterPath(new NodePath("PoseDriver:JawOpen"), true);
        speechLayer.SetFilterPath(new NodePath("PoseDriver:MouthWidth"), true);
        speechLayer.SetFilterPath(new NodePath("PoseDriver:MouthRoundness"), true);
        speechLayer.SetFilterPath(new NodePath("PoseDriver:LeftMouthCorner"), true);
        speechLayer.SetFilterPath(new NodePath("PoseDriver:RightMouthCorner"), true);
        speechLayer.FilterEnabled = true;
        tree.AddNode(SpeechMixNode, speechLayer, new Vector2(-120, 0));

        tree.ConnectNode(BaselineRateNode, 0, BaselineClipNode);
        tree.ConnectNode(ExpressionDeltaNode, 0, ExpressionStateMachine);
        tree.ConnectNode(ExpressionDeltaNode, 1, ExpressionReferenceNode);
        tree.ConnectNode(ExpressionMixNode, 0, BaselineRateNode);
        tree.ConnectNode(ExpressionMixNode, 1, ExpressionDeltaNode);
        tree.ConnectNode(SpeechMixNode, 0, ExpressionMixNode);
        tree.ConnectNode(SpeechMixNode, 1, SpeechRestNode);
        tree.ConnectNode("output", 0, SpeechMixNode);

        _tree!.AnimPlayer = _tree.GetPathTo(_player!);
        _tree.TreeRoot = tree;
        _tree.Deterministic = true;
        _tree.Active = true;
        _tree.Set($"parameters/{BaselineRateNode}/scale", 0.25f);
        _tree.Set($"parameters/{ExpressionDeltaNode}/sub_amount", 1f);
        _tree.Set($"parameters/{ExpressionMixNode}/add_amount", 1f);
        _tree.Set($"parameters/{SpeechMixNode}/add_amount", 0f);
        _playback = _tree.Get($"parameters/{ExpressionStateMachine}/playback")
            .As<AnimationNodeStateMachinePlayback>();
    }

    private static AnimationNodeAnimation ClipNode(string animationName) => new()
    {
        Animation = animationName,
    };

    private static string SceneStateName(SceneId scene, bool retrigger) =>
        retrigger ? $"{scene}{RetriggerStateSuffix}" : scene.ToString();

    private readonly record struct PendingSceneRequest(SceneId Scene, long Revision);

    private static GodotAnimation BuildNeutral()
    {
        GodotAnimation animation = NewLoopingAnimation(1.0);
        AddTrack(animation, nameof(FacePoseDriver.PupilSize), (0, 0.55f), (0.5, 0.57f), (1, 0.55f));
        AddTrack(animation, nameof(FacePoseDriver.LeftEyelidOpen), (0, 1f), (0.48, 0.96f), (0.52, 1f), (1, 1f));
        AddTrack(animation, nameof(FacePoseDriver.RightEyelidOpen), (0, 1f), (0.5, 0.95f), (0.55, 1f), (1, 1f));
        AddTrack(animation, nameof(FacePoseDriver.JawOpen), (0, 0.22f), (0.5, 0.28f), (1, 0.22f));
        AddTrack(animation, nameof(FacePoseDriver.MouthWidth), (0, 0.58f), (0.5, 0.61f), (1, 0.58f));
        AddTrack(animation, nameof(FacePoseDriver.MouthRoundness), (0, 0.10f), (0.5, 0.14f), (1, 0.10f));
        AddTrack(animation, nameof(FacePoseDriver.LightingIntensity), (0, 0.98f), (0.35, 1.05f), (0.7, 0.99f), (1, 0.98f));
        return CompletePoseTracks(animation);
    }

    private static GodotAnimation BuildNeutralReference()
    {
        GodotAnimation animation = NewLoopingAnimation(1.0);
        foreach (string property in PoseProperties)
        {
            float value = GetReferenceValue(property);
            AddTrack(animation, property, (0, value), (1, value));
        }

        return animation;
    }

    private static GodotAnimation BuildWatchful()
    {
        GodotAnimation animation = NewOneShotAnimation();
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftGazeX),
            (0, 0f),
            (0.08, 0.08f),
            (0.105, 0.08f),
            (0.12, -0.88f),
            (0.27, -0.88f),
            (0.30, -0.48f),
            (0.345, -0.48f),
            (0.36, 0.86f),
            (0.55, 0.86f),
            (0.58, 0.52f),
            (0.66, 0.52f),
            (0.68, 0.18f),
            (0.69, -0.12f),
            (0.78, -0.12f),
            (0.84, 0.08f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightGazeX),
            (0, 0f),
            (0.08, -0.06f),
            (0.115, -0.06f),
            (0.13, -0.48f),
            (0.15, -0.80f),
            (0.27, -0.80f),
            (0.31, -0.40f),
            (0.36, -0.40f),
            (0.375, 0.80f),
            (0.55, 0.80f),
            (0.585, 0.54f),
            (0.66, 0.54f),
            (0.68, 0.22f),
            (0.70, -0.08f),
            (0.78, -0.08f),
            (0.84, 0.08f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftGazeY),
            (0, 0f),
            (0.105, 0f),
            (0.12, -0.18f),
            (0.27, -0.18f),
            (0.30, -0.08f),
            (0.36, -0.08f),
            (0.38, 0.18f),
            (0.55, 0.18f),
            (0.60, 0.10f),
            (0.675, 0.10f),
            (0.69, -0.78f),
            (0.78, -0.78f),
            (0.84, -0.24f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightGazeY),
            (0, 0f),
            (0.115, 0f),
            (0.13, -0.10f),
            (0.27, -0.10f),
            (0.31, -0.12f),
            (0.375, -0.12f),
            (0.39, 0.12f),
            (0.55, 0.12f),
            (0.60, 0.08f),
            (0.68, 0.08f),
            (0.70, -0.72f),
            (0.78, -0.72f),
            (0.84, -0.22f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftEyelidOpen),
            (0, 1f),
            (0.08, 0.72f),
            (0.15, 0.92f),
            (0.515, 1f),
            (0.527, 0.05f),
            (0.536, 0.05f),
            (0.55, 1f),
            (0.575, 1f),
            (0.587, 0.07f),
            (0.596, 0.07f),
            (0.61, 1f),
            (0.69, 0.88f),
            (0.73, 1f),
            (0.87, 0.94f),
            (1, 1f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightEyelidOpen),
            (0, 1f),
            (0.08, 0.82f),
            (0.15, 0.96f),
            (0.518, 1f),
            (0.530, 0.07f),
            (0.539, 0.07f),
            (0.555, 1f),
            (0.580, 1f),
            (0.592, 0.09f),
            (0.601, 0.09f),
            (0.615, 1f),
            (0.69, 0.90f),
            (0.73, 1f),
            (0.87, 0.96f),
            (1, 1f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftBrowTension),
            (0, 0f),
            (0.08, -0.18f),
            (0.29, 0.12f),
            (0.47, -0.08f),
            (0.69, 0.56f),
            (0.84, 0.34f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightBrowTension),
            (0, 0f),
            (0.08, 0.22f),
            (0.29, -0.08f),
            (0.47, 0.16f),
            (0.69, -0.14f),
            (0.84, 0.18f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.PupilSize),
            (0, 0.55f),
            (0.08, 0.64f),
            (0.20, 0.56f),
            (0.47, 0.52f),
            (0.62, 0.57f),
            (0.69, 0.44f),
            (0.75, 0.70f),
            (0.84, 0.64f),
            (1, 0.55f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.JawOpen),
            (0, 0.16f),
            (0.08, 0.11f),
            (0.60, 0.16f),
            (0.69, 0.34f),
            (0.76, 0.26f),
            (0.84, 0.18f),
            (1, 0.16f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.MouthWidth),
            (0, 0.52f),
            (0.08, 0.46f),
            (0.60, 0.52f),
            (0.69, 0.38f),
            (0.76, 0.44f),
            (0.84, 0.66f),
            (1, 0.52f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.MouthRoundness),
            (0, 0.12f),
            (0.60, 0.12f),
            (0.69, 0.82f),
            (0.76, 0.48f),
            (0.84, 0.18f),
            (1, 0.12f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftMouthCorner),
            (0, 0f),
            (0.08, 0.16f),
            (0.60, 0.10f),
            (0.70, -0.10f),
            (0.84, 0.50f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightMouthCorner),
            (0, 0f),
            (0.08, 0.10f),
            (0.60, 0.10f),
            (0.70, -0.06f),
            (0.84, 0.38f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LightingIntensity),
            (0, 0.94f),
            (0.34, 1f),
            (0.56, 0.98f),
            (0.675, 0.98f),
            (0.70, 0.88f),
            (0.77, 1.15f),
            (0.86, 1.06f),
            (1, 0.94f));
        return CompletePoseTracks(animation);
    }

    private static GodotAnimation BuildFrightened()
    {
        GodotAnimation animation = NewOneShotAnimation();
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftGazeX),
            (0, 0f),
            (0.05, 0.12f),
            (0.065, 0.12f),
            (0.08, -0.76f),
            (0.16, -0.76f),
            (0.18, -0.48f),
            (0.23, -0.48f),
            (0.25, 0.18f),
            (0.34, 0.18f),
            (0.38, -0.08f),
            (0.58, 0.05f),
            (0.78, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightGazeX),
            (0, 0f),
            (0.05, 0.08f),
            (0.065, 0.08f),
            (0.08, -0.70f),
            (0.16, -0.70f),
            (0.18, -0.52f),
            (0.23, -0.52f),
            (0.25, 0.14f),
            (0.34, 0.14f),
            (0.38, -0.05f),
            (0.58, 0.04f),
            (0.78, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftGazeY),
            (0, 0f),
            (0.065, 0f),
            (0.08, -0.22f),
            (0.16, -0.22f),
            (0.25, 0.12f),
            (0.34, 0.12f),
            (0.42, -0.04f),
            (0.72, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightGazeY),
            (0, 0f),
            (0.065, 0f),
            (0.08, -0.18f),
            (0.16, -0.18f),
            (0.25, 0.10f),
            (0.34, 0.10f),
            (0.42, -0.03f),
            (0.72, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.PupilSize),
            (0, 0.55f),
            (0.07, 0.34f),
            (0.14, 0.72f),
            (0.24, 0.64f),
            (0.42, 0.60f),
            (0.67, 0.58f),
            (0.82, 0.62f),
            (1, 0.55f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftEyelidOpen),
            (0, 1f),
            (0.05, 0.62f),
            (0.065, 0.62f),
            (0.08, 1f),
            (0.18, 0.88f),
            (0.27, 1f),
            (0.37, 0.86f),
            (0.47, 0.98f),
            (0.66, 0.92f),
            (0.675, 0.92f),
            (0.687, 0.10f),
            (0.696, 0.10f),
            (0.711, 1f),
            (0.84, 0.94f),
            (1, 1f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightEyelidOpen),
            (0, 1f),
            (0.05, 0.66f),
            (0.065, 0.66f),
            (0.08, 1f),
            (0.18, 0.90f),
            (0.27, 1f),
            (0.37, 0.88f),
            (0.47, 0.98f),
            (0.66, 0.94f),
            (0.678, 0.94f),
            (0.690, 0.12f),
            (0.699, 0.12f),
            (0.714, 1f),
            (0.84, 0.95f),
            (1, 1f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftBrowTension),
            (0, 0f),
            (0.09, -0.34f),
            (0.27, -0.22f),
            (0.50, -0.14f),
            (0.74, 0.16f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightBrowTension),
            (0, 0f),
            (0.09, -0.30f),
            (0.27, -0.20f),
            (0.50, -0.12f),
            (0.74, -0.06f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.JawOpen),
            (0, 0.16f),
            (0.05, 0.10f),
            (0.11, 0.46f),
            (0.24, 0.26f),
            (0.33, 0.40f),
            (0.43, 0.22f),
            (0.52, 0.31f),
            (0.63, 0.16f),
            (0.76, 0.13f),
            (1, 0.16f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.MouthWidth),
            (0, 0.52f),
            (0.05, 0.44f),
            (0.11, 0.22f),
            (0.24, 0.28f),
            (0.43, 0.34f),
            (0.63, 0.48f),
            (0.76, 0.64f),
            (0.88, 0.57f),
            (1, 0.52f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.MouthRoundness),
            (0, 0.12f),
            (0.05, 0.18f),
            (0.11, 0.94f),
            (0.24, 0.78f),
            (0.43, 0.66f),
            (0.63, 0.28f),
            (0.76, 0.12f),
            (1, 0.12f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftMouthCorner),
            (0, 0f),
            (0.11, -0.08f),
            (0.52, -0.04f),
            (0.76, 0.46f),
            (0.88, 0.24f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightMouthCorner),
            (0, 0f),
            (0.11, -0.06f),
            (0.52, -0.03f),
            (0.76, 0.34f),
            (0.88, 0.18f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.Tremble),
            (0, 0f),
            (0.07, 0f),
            (0.09, 0.78f),
            (0.14, 0.20f),
            (0.18, 0f),
            (0.27, 0f),
            (0.29, 0.44f),
            (0.34, 0.08f),
            (0.37, 0f),
            (0.47, 0f),
            (0.49, 0.24f),
            (0.53, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LightingIntensity),
            (0, 0.94f),
            (0.05, 0.72f),
            (0.10, 1.24f),
            (0.20, 0.90f),
            (0.31, 1.14f),
            (0.43, 0.96f),
            (0.54, 1.08f),
            (0.66, 0.98f),
            (0.78, 1.05f),
            (1, 0.94f));
        return CompletePoseTracks(animation);
    }

    private static GodotAnimation BuildDrowsy()
    {
        GodotAnimation animation = NewOneShotAnimation();
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftGazeX),
            (0, 0f),
            (0.24, -0.16f),
            (0.48, 0.08f),
            (0.72, -0.10f),
            (0.84, 0f),
            (0.92, 0.08f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightGazeX),
            (0, 0f),
            (0.28, 0.12f),
            (0.52, -0.10f),
            (0.72, 0.08f),
            (0.84, 0f),
            (0.92, 0.05f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftGazeY),
            (0, 0f),
            (0.18, 0.28f),
            (0.40, 0.52f),
            (0.72, 0.72f),
            (0.80, 0.62f),
            (0.825, 0.62f),
            (0.84, -0.12f),
            (0.92, 0.08f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightGazeY),
            (0, 0f),
            (0.22, 0.22f),
            (0.44, 0.48f),
            (0.72, 0.66f),
            (0.80, 0.58f),
            (0.825, 0.58f),
            (0.84, -0.10f),
            (0.92, 0.06f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.PupilSize),
            (0, 0.55f),
            (0.34, 0.62f),
            (0.66, 0.68f),
            (0.80, 0.70f),
            (0.825, 0.70f),
            (0.84, 0.38f),
            (0.88, 0.66f),
            (0.94, 0.62f),
            (1, 0.55f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftEyelidOpen),
            (0, 1f),
            (0.12, 0.84f),
            (0.26, 0.44f),
            (0.48, 0.24f),
            (0.58, 0.12f),
            (0.72, 0.05f),
            (0.825, 0.08f),
            (0.84, 1f),
            (0.88, 0.76f),
            (0.92, 0.58f),
            (0.96, 0.92f),
            (1, 1f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightEyelidOpen),
            (0, 1f),
            (0.16, 0.90f),
            (0.30, 0.68f),
            (0.50, 0.30f),
            (0.60, 0.16f),
            (0.72, 0.09f),
            (0.825, 0.10f),
            (0.84, 1f),
            (0.88, 0.82f),
            (0.92, 0.94f),
            (1, 1f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftBrowTension),
            (0, 0f),
            (0.56, -0.10f),
            (0.80, -0.16f),
            (0.84, -0.34f),
            (0.92, 0.28f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightBrowTension),
            (0, 0f),
            (0.56, -0.08f),
            (0.80, -0.14f),
            (0.84, -0.30f),
            (0.92, -0.08f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.JawOpen),
            (0, 0.16f),
            (0.24, 0.20f),
            (0.36, 0.48f),
            (0.55, 0.68f),
            (0.66, 0.30f),
            (0.75, 0.17f),
            (0.825, 0.17f),
            (0.84, 0.36f),
            (0.88, 0.16f),
            (0.92, 0.13f),
            (1, 0.16f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.MouthWidth),
            (0, 0.52f),
            (0.24, 0.44f),
            (0.36, 0.32f),
            (0.55, 0.24f),
            (0.66, 0.38f),
            (0.75, 0.48f),
            (0.84, 0.30f),
            (0.92, 0.64f),
            (1, 0.52f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.MouthRoundness),
            (0, 0.12f),
            (0.24, 0.28f),
            (0.36, 0.72f),
            (0.55, 0.96f),
            (0.66, 0.62f),
            (0.75, 0.22f),
            (0.84, 0.82f),
            (0.88, 0.38f),
            (0.92, 0.16f),
            (1, 0.12f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftMouthCorner),
            (0, 0f),
            (0.55, -0.08f),
            (0.84, -0.10f),
            (0.92, 0.48f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightMouthCorner),
            (0, 0f),
            (0.55, -0.06f),
            (0.84, -0.08f),
            (0.92, 0.34f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.Tremble),
            (0, 0f),
            (0.83, 0f),
            (0.85, 0.30f),
            (0.89, 0.08f),
            (0.92, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LightingIntensity),
            (0, 0.94f),
            (0.34, 0.91f),
            (0.55, 0.80f),
            (0.72, 0.76f),
            (0.825, 0.78f),
            (0.84, 1.20f),
            (0.89, 1.05f),
            (0.94, 1.04f),
            (1, 0.94f));
        return CompletePoseTracks(animation);
    }

    private static GodotAnimation BuildMischievous()
    {
        GodotAnimation animation = NewOneShotAnimation();
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftEyelidOpen),
            (0, 1f),
            (0.08, 0.34f),
            (0.16, 0.72f),
            (0.28, 0.94f),
            (0.375, 0.94f),
            (0.389, 0.07f),
            (0.398, 0.07f),
            (0.415, 0.90f),
            (0.62, 0.90f),
            (0.645, 0.90f),
            (0.657, 0.06f),
            (0.666, 0.06f),
            (0.682, 0.94f),
            (0.84, 0.90f),
            (1, 1f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightEyelidOpen),
            (0, 1f),
            (0.08, 0.30f),
            (0.14, 0.08f),
            (0.34, 0.05f),
            (0.355, 0.05f),
            (0.37, 0.88f),
            (0.47, 0.95f),
            (0.67, 0.94f),
            (0.84, 0.88f),
            (1, 1f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftGazeX),
            (0, 0f),
            (0.135, 0f),
            (0.15, 0.68f),
            (0.25, 0.68f),
            (0.27, 0.34f),
            (0.34, 0.34f),
            (0.355, 0.02f),
            (0.43, -0.10f),
            (0.48, -0.10f),
            (0.52, 0f),
            (0.82, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightGazeX),
            (0, 0f),
            (0.34, 0.50f),
            (0.355, 0.50f),
            (0.37, -0.58f),
            (0.44, -0.58f),
            (0.46, 0.10f),
            (0.52, 0f),
            (0.82, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftGazeY),
            (0, 0f),
            (0.135, 0f),
            (0.15, 0.12f),
            (0.25, 0.12f),
            (0.34, 0f),
            (0.82, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightGazeY),
            (0, 0f),
            (0.355, 0f),
            (0.37, 0.10f),
            (0.44, 0.10f),
            (0.46, 0f),
            (0.82, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.PupilSize),
            (0, 0.55f),
            (0.16, 0.66f),
            (0.36, 0.62f),
            (0.47, 0.58f),
            (0.56, 0.62f),
            (0.82, 0.68f),
            (0.92, 0.61f),
            (1, 0.55f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftBrowTension),
            (0, 0f),
            (0.20, 0.26f),
            (0.40, -0.08f),
            (0.54, 0.20f),
            (0.82, 0.44f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightBrowTension),
            (0, 0f),
            (0.20, -0.08f),
            (0.40, 0.24f),
            (0.54, 0.08f),
            (0.82, 0.14f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.JawOpen),
            (0, 0.16f),
            (0.12, 0.11f),
            (0.36, 0.14f),
            (0.52, 0.20f),
            (0.67, 0.38f),
            (0.76, 0.46f),
            (0.84, 0.34f),
            (0.92, 0.22f),
            (1, 0.16f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.MouthWidth),
            (0, 0.52f),
            (0.12, 0.44f),
            (0.36, 0.54f),
            (0.52, 0.62f),
            (0.67, 0.88f),
            (0.76, 1f),
            (0.84, 0.94f),
            (0.92, 0.70f),
            (1, 0.52f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.MouthRoundness),
            (0, 0.12f),
            (0.36, 0.14f),
            (0.52, 0.10f),
            (0.76, 0.04f),
            (0.84, 0.08f),
            (1, 0.12f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LeftMouthCorner),
            (0, 0f),
            (0.18, 0.14f),
            (0.36, 0.34f),
            (0.52, 0.50f),
            (0.67, 0.82f),
            (0.76, 0.96f),
            (0.84, 0.86f),
            (0.92, 0.44f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.RightMouthCorner),
            (0, 0f),
            (0.18, 0.04f),
            (0.36, 0.16f),
            (0.52, 0.30f),
            (0.67, 0.56f),
            (0.76, 0.68f),
            (0.84, 0.62f),
            (0.92, 0.32f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.Tremble),
            (0, 0f),
            (0.66, 0f),
            (0.68, 0.12f),
            (0.72, 0f),
            (0.79, 0f),
            (0.81, 0.08f),
            (0.84, 0f),
            (1, 0f));
        AddTrack(
            animation,
            nameof(FacePoseDriver.LightingIntensity),
            (0, 0.94f),
            (0.10, 0.82f),
            (0.24, 0.86f),
            (0.40, 1.02f),
            (0.58, 1.10f),
            (0.76, 1.27f),
            (0.84, 1.20f),
            (0.94, 1.03f),
            (1, 0.94f));
        return CompletePoseTracks(animation);
    }

    private static GodotAnimation BuildSpeechRest()
    {
        GodotAnimation animation = NewLoopingAnimation(1);
        AddTrack(animation, nameof(FacePoseDriver.JawOpen), (0, 0f), (1, 0f));
        AddTrack(animation, nameof(FacePoseDriver.MouthWidth), (0, 0f), (1, 0f));
        AddTrack(animation, nameof(FacePoseDriver.MouthRoundness), (0, 0f), (1, 0f));
        AddTrack(animation, nameof(FacePoseDriver.LeftMouthCorner), (0, 0f), (1, 0f));
        AddTrack(animation, nameof(FacePoseDriver.RightMouthCorner), (0, 0f), (1, 0f));
        return animation;
    }

    private static GodotAnimation NewOneShotAnimation() => new()
    {
        Length = 1.0,
        LoopMode = Godot.Animation.LoopModeEnum.None,
    };

    private static GodotAnimation NewLoopingAnimation(double length) => new()
    {
        Length = length,
        LoopMode = Godot.Animation.LoopModeEnum.Linear,
    };

    private static GodotAnimation CompletePoseTracks(GodotAnimation animation)
    {
        foreach (string property in PoseProperties)
        {
            NodePath path = new($"PoseDriver:{property}");
            if (animation.FindTrack(path, GodotAnimation.TrackType.Value) >= 0)
            {
                continue;
            }

            float value = GetReferenceValue(property);
            AddTrack(animation, property, (0, value), (1, value));
        }

        return animation;
    }

    private static FacePose ComposeCapturePose(
        GodotAnimation baseline,
        GodotAnimation expression,
        double normalizedProgress)
    {
        float Composed(string property) =>
            SampleValue(baseline, property, normalizedProgress) +
            SampleValue(expression, property, normalizedProgress) -
            GetReferenceValue(property);

        return new FacePose
        {
            LeftGazeX = Composed(nameof(FacePoseDriver.LeftGazeX)),
            LeftGazeY = Composed(nameof(FacePoseDriver.LeftGazeY)),
            RightGazeX = Composed(nameof(FacePoseDriver.RightGazeX)),
            RightGazeY = Composed(nameof(FacePoseDriver.RightGazeY)),
            PupilSize = Composed(nameof(FacePoseDriver.PupilSize)),
            LeftEyelidOpen = Composed(nameof(FacePoseDriver.LeftEyelidOpen)),
            RightEyelidOpen = Composed(nameof(FacePoseDriver.RightEyelidOpen)),
            LeftBrowTension = Composed(nameof(FacePoseDriver.LeftBrowTension)),
            RightBrowTension = Composed(nameof(FacePoseDriver.RightBrowTension)),
            JawOpen = Composed(nameof(FacePoseDriver.JawOpen)),
            MouthWidth = Composed(nameof(FacePoseDriver.MouthWidth)),
            MouthRoundness = Composed(nameof(FacePoseDriver.MouthRoundness)),
            LeftMouthCorner = Composed(nameof(FacePoseDriver.LeftMouthCorner)),
            RightMouthCorner = Composed(nameof(FacePoseDriver.RightMouthCorner)),
            Tremble = Composed(nameof(FacePoseDriver.Tremble)),
            LightingIntensity = Composed(nameof(FacePoseDriver.LightingIntensity)),
        }.Clamp();
    }

    private static float SampleValue(GodotAnimation animation, string property, double time)
    {
        int track = animation.FindTrack(
            new NodePath($"PoseDriver:{property}"),
            GodotAnimation.TrackType.Value);
        return track < 0
            ? GetReferenceValue(property)
            : animation.ValueTrackInterpolate(track, time).AsSingle();
    }

    private static float GetReferenceValue(string property) => property switch
    {
        nameof(FacePoseDriver.LeftGazeX) => ExpressionReferencePose.LeftGazeX,
        nameof(FacePoseDriver.LeftGazeY) => ExpressionReferencePose.LeftGazeY,
        nameof(FacePoseDriver.RightGazeX) => ExpressionReferencePose.RightGazeX,
        nameof(FacePoseDriver.RightGazeY) => ExpressionReferencePose.RightGazeY,
        nameof(FacePoseDriver.PupilSize) => ExpressionReferencePose.PupilSize,
        nameof(FacePoseDriver.LeftEyelidOpen) => ExpressionReferencePose.LeftEyelidOpen,
        nameof(FacePoseDriver.RightEyelidOpen) => ExpressionReferencePose.RightEyelidOpen,
        nameof(FacePoseDriver.LeftBrowTension) => ExpressionReferencePose.LeftBrowTension,
        nameof(FacePoseDriver.RightBrowTension) => ExpressionReferencePose.RightBrowTension,
        nameof(FacePoseDriver.JawOpen) => ExpressionReferencePose.JawOpen,
        nameof(FacePoseDriver.MouthWidth) => ExpressionReferencePose.MouthWidth,
        nameof(FacePoseDriver.MouthRoundness) => ExpressionReferencePose.MouthRoundness,
        nameof(FacePoseDriver.LeftMouthCorner) => ExpressionReferencePose.LeftMouthCorner,
        nameof(FacePoseDriver.RightMouthCorner) => ExpressionReferencePose.RightMouthCorner,
        nameof(FacePoseDriver.Tremble) => ExpressionReferencePose.Tremble,
        nameof(FacePoseDriver.LightingIntensity) => ExpressionReferencePose.LightingIntensity,
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Unknown face pose property."),
    };

    private static void AddTrack(GodotAnimation animation, string property, params (double Time, float Value)[] keys)
    {
        int track = animation.AddTrack(Godot.Animation.TrackType.Value);
        animation.TrackSetPath(track, new NodePath($"PoseDriver:{property}"));
        animation.TrackSetInterpolationType(track, InterpolationFor(property));
        foreach ((double time, float value) in keys)
        {
            animation.TrackInsertKey(track, time, value);
        }
    }

    private static GodotAnimation.InterpolationType InterpolationFor(string property) => property switch
    {
        // Linear interpolation keeps the deliberately close key pairs below
        // crisp and prevents a cubic curve from drifting through a held look or
        // overshooting the stable range during a blink or tremble accent.
        nameof(FacePoseDriver.LeftGazeX) or
        nameof(FacePoseDriver.LeftGazeY) or
        nameof(FacePoseDriver.RightGazeX) or
        nameof(FacePoseDriver.RightGazeY) or
        nameof(FacePoseDriver.LeftEyelidOpen) or
        nameof(FacePoseDriver.RightEyelidOpen) or
        nameof(FacePoseDriver.Tremble) => GodotAnimation.InterpolationType.Linear,

        // Pupils, brows, mouth shapes, and light levels benefit from organic
        // ease-in/ease-out as the face settles around those sharper eye beats.
        _ => GodotAnimation.InterpolationType.Cubic,
    };
}
