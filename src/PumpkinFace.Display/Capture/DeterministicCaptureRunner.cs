using Godot;
using PumpkinFace.Core;
using PumpkinFace.Display.Animation;
using PumpkinFace.Display.Rendering;
using PumpkinFace.Display.UI;

namespace PumpkinFace.Display.Capture;

/// <summary>
/// Renders reproducible reference frames without requiring the operator UI.
/// Usage: godot-mono --path src/PumpkinFace.Display --
///        --capture-dir=/absolute/path [--compare-dir=/absolute/path]
/// </summary>
public sealed partial class DeterministicCaptureRunner : Node
{
    private static readonly CaptureFrame[] Frames =
    [
        new(EmotionId.Frightened, 0.16, "frightened-016.png"),
        new(EmotionId.Frightened, 0.42, "frightened-042.png"),
        new(EmotionId.Frightened, 0.82, "frightened-082.png"),
        new(EmotionId.Happy, 0.16, "happy-016.png"),
        new(EmotionId.Happy, 0.42, "happy-042.png"),
        new(EmotionId.Happy, 0.82, "happy-082.png"),
        new(EmotionId.Happy, 0.42, "happy-amount-025.png", 0.25f),
        new(EmotionId.Happy, 0.42, "shell-thickness-min.png",
            ShellThickness: ProjectionCalibration.MinimumShellThickness),
        new(EmotionId.Happy, 0.42, "shell-thickness-max.png",
            ShellThickness: ProjectionCalibration.MaximumShellThickness),
        new(EmotionId.Sad, 0.16, "sad-016.png"),
        new(EmotionId.Sad, 0.42, "sad-042.png"),
        new(EmotionId.Sad, 0.82, "sad-082.png"),
        new(EmotionId.Happy, 0.42, "scene-looking.png", 1f,
            new ActionSceneFrame(new Vector2(0.75f, -0.35f), 1f, 0f, 1f)),
        new(EmotionId.Happy, 0.42, "scene-blinking.png", 1f,
            new ActionSceneFrame(Vector2.Zero, 0.08f, 0f, 1f)),
        new(EmotionId.Happy, 0.42, "scene-talking.png", 1f,
            new ActionSceneFrame(Vector2.Zero, 1f, 0.67f, 1f, true, 0.34f, 0.92f, 1f)),
        new(EmotionId.Happy, 0.42, "scene-candle-sputter.png", 1f,
            new ActionSceneFrame(Vector2.Zero, 1f, 0f, 0.30f)),
        new(EmotionId.Happy, 0.42, "camera-orbit-left.png", 1f, null, new Vector2(35f, 0f)),
        new(EmotionId.Happy, 0.42, "camera-orbit-upper-right.png", 1f, null, new Vector2(-28f, 20f)),
        new(EmotionId.Happy, .42, "gesture-wink.png", Gesture: GestureId.LeftWink, PerformanceTime: .2),
        new(EmotionId.Happy, .42, "gesture-blink-closing.png", Gesture: GestureId.Blink, PerformanceTime: .035),
        new(EmotionId.Happy, .42, "gesture-blink-closed.png", Gesture: GestureId.Blink, PerformanceTime: .08),
        new(EmotionId.Happy, .42, "gesture-blink-opening.png", Gesture: GestureId.Blink, PerformanceTime: .18),
        new(EmotionId.Happy, .42, "gesture-curious.png", Gesture: GestureId.CuriousTilt, PerformanceTime: .65),
        new(EmotionId.Happy, .42, "gesture-surprise.png", Gesture: GestureId.Surprise, PerformanceTime: .45),
        new(EmotionId.Happy, .42, "gesture-delight.png", Gesture: GestureId.Delight, PerformanceTime: .6),
        new(EmotionId.Happy, .42, "pupil-small.png", Variant: "pupil-small"),
        new(EmotionId.Happy, .42, "pupil-large.png", Variant: "pupil-large"),
        new(EmotionId.Happy, .42, "asymmetric-brows-mouth.png", Variant: "asymmetry"),
        new(EmotionId.Happy, .42, "motion-limits.png", Variant: "motion"),
        new(EmotionId.Happy, .42, "motion-guides.png", Variant: "guides"),
        new(EmotionId.Happy, .42, "speech-with-wink.png", Gesture: GestureId.LeftWink, PerformanceTime: .2, Variant: "speech"),
        new(EmotionId.Happy, .42, "eyes-look-up.png", Variant: "eyes-up"),
        new(EmotionId.Happy, .42, "eyes-look-down.png", Variant: "eyes-down"),
        new(EmotionId.Happy, .42, "eyes-down-half-blink.png", Variant: "eyes-down-blink"),
        new(EmotionId.Sad, .42, "sad-eyes-closed.png", Gesture: GestureId.Blink, PerformanceTime: .08),
        new(EmotionId.Frightened, .42, "frightened-eyes-closed.png", Gesture: GestureId.Blink, PerformanceTime: .08),
        new(EmotionId.Happy, .42, "attention-with-tilt.png", PerformanceTime: 1.9, Variant: "attention"),
        new(EmotionId.Happy, .42, "attention-with-nod.png", PerformanceTime: 3.8, Variant: "attention"),
    ];

    private CaptureFrame[] _frames = Frames;
    private string _characterId = CharacterCatalog.DefaultId;
    private bool _captureOperator;

    private static CaptureFrame[] PerformanceSequence() =>
        new[] { "attention", "conversation", "surprise", "interruption" }
            .SelectMany(scenario => Enumerable.Range(0, 61).Select(index =>
                new CaptureFrame(EmotionId.Happy, .42, $"{scenario}-{index:D3}.png", .65f,
                    PerformanceTime: index / 10d, Variant: scenario))).ToArray();

    private static CaptureFrame[] AttentionSequence() => Enumerable.Range(0, 121)
        .Select(index => new CaptureFrame(EmotionId.Happy, .42, $"attention-{index:D3}.png", .65f,
            PerformanceTime: index / 20d, Variant: "attention")).ToArray();

    private static CaptureFrame[] GestureSequence() => new[] { "show-tilt", "show-nod", "show-shake" }
        .SelectMany(scenario => Enumerable.Range(0, 81).Select(index =>
            new CaptureFrame(EmotionId.Happy, .42, $"{scenario}-{index:D3}.png", .65f,
                PerformanceTime: index / 20d, Variant: scenario))).ToArray();

    private const int FramesToSettle = 3;
    private const int MaximumPostDrawWaitFrames = 300;
    private const int MaximumImageReadbackAttempts = 30;
    private const float MaximumRootMeanSquaredDifference = 0.035f;
    private const int VisualDifferenceExitCode = 2;
    private const int RendererUnavailableExitCode = 3;

    private FaceStage? _stage;
    private SceneAnimationController? _animations;
    private string? _captureDirectory;
    private string? _comparisonDirectory;
    private int _frameIndex;
    private int _settleFrames;
    private int _postDrawWaitFrames;
    private int _imageReadbackAttempts;
    private int _failures;
    private bool _configured;
    private bool _capturePending;
    private bool _subscribedToPostDraw;
    private bool _finished;

    public void Configure(
        FaceStage stage,
        SceneAnimationController animations,
        string captureDirectory,
        string? comparisonDirectory)
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        _animations = animations ?? throw new ArgumentNullException(nameof(animations));
        _captureDirectory = ResolvePath(captureDirectory);
        _comparisonDirectory = string.IsNullOrWhiteSpace(comparisonDirectory)
            ? null
            : ResolvePath(comparisonDirectory);

        if (!HasGpuBackedRenderer(out string rendererDescription))
        {
            FailAndQuit(
                "Deterministic capture requires a GPU-backed window, but Godot is using " +
                $"its dummy/headless renderer ({rendererDescription}). The dummy renderer " +
                "cannot read ViewportTexture pixels. Run the capture command without " +
                "'--headless' using the Godot .NET editor or executable.",
                RendererUnavailableExitCode);
            return;
        }

        if (OS.GetCmdlineUserArgs().Contains("--capture-performance-motion")) _frames = PerformanceSequence();
        if (OS.GetCmdlineUserArgs().Contains("--capture-attention-motion")) _frames = AttentionSequence();
        if (OS.GetCmdlineUserArgs().Contains("--capture-gesture-motion")) _frames = GestureSequence();
        string? requestedCharacter = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--capture-character="))?.Split('=', 2)[1];
        if (requestedCharacter is not null && !CharacterCatalog.IsKnown(requestedCharacter))
        {
            FailAndQuit($"Unknown capture character: {requestedCharacter}", VisualDifferenceExitCode);
            return;
        }
        _characterId = CharacterCatalog.Get(requestedCharacter).Id;
        _stage.SetCharacter(_characterId);
        if (OS.GetCmdlineUserArgs().Contains("--capture-character-idle"))
            _frames = Enumerable.Range(0, 121).Select(i => new CaptureFrame(EmotionId.Happy, .42,
                $"idle-{i:D3}.png", CharacterCatalog.Get(_characterId).DefaultEmotionAmount,
                PerformanceTime: i / 10d, Variant: "character-idle")).ToArray();
        Directory.CreateDirectory(_captureDirectory);
        _stage.Resize(new Vector2I(1280, 720));
        _stage.ShowGuides = false;
        _stage.AutoAdvanceAnimationTime = false;
        _captureOperator = OS.GetCmdlineUserArgs().Contains("--capture-operator");
        if (_captureOperator)
        {
            CharacterDefinition character = CharacterCatalog.Get(_characterId);
            _frames = [new(EmotionId.Happy, .42, "operator-character.png", character.DefaultEmotionAmount)];
            OperatorPanel panel = new();
            AddChild(panel);
            panel.Preview.SetPreviewTexture(_stage.Texture);
            panel.SetCharacter(character.Id);
            panel.SetEmotionAmount(character.DefaultEmotionAmount);
            panel.SetPerformanceState(BehaviorState.Idle, .65f);
            panel.SetStatus($"Meet {character.Name} — {character.Tagline}");
        }
        _configured = true;
        RenderingServer.FramePostDraw += OnFramePostDraw;
        _subscribedToPostDraw = true;
        PrepareFrame();
    }

    public override void _Process(double delta)
    {
        if (_finished || !_configured || _stage is null || _animations is null || _captureDirectory is null)
        {
            return;
        }

        _stage.SetPose(BuildCapturePose(_frames[_frameIndex]), _animations.CurrentPose);
        if (_capturePending)
        {
            if (++_postDrawWaitFrames > MaximumPostDrawWaitFrames)
            {
                FailAndQuit(
                    "The GPU renderer did not complete a frame for deterministic capture. " +
                    "Keep the capture window available and try again.",
                    RendererUnavailableExitCode);
            }

            return;
        }

        if (++_settleFrames < FramesToSettle)
        {
            return;
        }

        // Texture readback before frame_post_draw can return a stale or black
        // image. Request it now and perform the readback from the post-draw signal.
        _capturePending = true;
        _postDrawWaitFrames = 0;
    }

    public override void _ExitTree()
    {
        DisconnectPostDraw();
    }

    private void OnFramePostDraw()
    {
        if (_finished || !_capturePending)
        {
            return;
        }

        _capturePending = false;
        CaptureAttempt attempt = CaptureCurrentFrame();
        if (attempt == CaptureAttempt.Retry)
        {
            if (++_imageReadbackAttempts > MaximumImageReadbackAttempts)
            {
                FailAndQuit(
                    "The active GPU renderer repeatedly returned no framebuffer image. " +
                    "Make sure the capture window is visible and is not using Godot's " +
                    "dummy/headless renderer.",
                    RendererUnavailableExitCode);
            }
            else
            {
                _capturePending = true;
                _postDrawWaitFrames = 0;
            }

            return;
        }

        _imageReadbackAttempts = 0;
        _frameIndex++;
        if (_frameIndex >= _frames.Length)
        {
            Finish();
            return;
        }

        PrepareFrame();
    }

    private void PrepareFrame()
    {
        CaptureFrame frame = _frames[_frameIndex];
        _animations!.SetCaptureFrame(frame.Scene, frame.Progress);
        _stage!.SetCalibration(ProjectionCalibration.Default with
        {
            ShellThickness = frame.ShellThickness,
        });
        _stage!.EmotionAmount = frame.EmotionAmount;
        _stage.SetCameraOrbit(frame.CameraOrbitDegrees ?? Vector2.Zero);
        _stage!.AnimationTime = _frameIndex * 0.731 + frame.Progress * 4.0;
        _stage.SetPose(BuildCapturePose(frame), _animations.CurrentPose);
        _settleFrames = 0;
        _capturePending = false;
        _postDrawWaitFrames = 0;
    }

    private CaptureAttempt CaptureCurrentFrame()
    {
        CaptureFrame frame = _frames[_frameIndex];
        Image? image = (_captureOperator ? GetViewport().GetTexture() : _stage!.Texture).GetImage();
        if (image is null || image.IsEmpty())
        {
            image?.Dispose();
            return CaptureAttempt.Retry;
        }

        using (image)
        {
            string outputPath = Path.Combine(_captureDirectory!, frame.FileName);
            Error error = image.SavePng(outputPath);
            if (error != Error.Ok)
            {
                _failures++;
                GD.PushError($"Could not save capture {outputPath}: {error}");
                return CaptureAttempt.Captured;
            }

            if (_comparisonDirectory is null)
            {
                return CaptureAttempt.Captured;
            }

            string referencePath = Path.Combine(_comparisonDirectory, frame.FileName);
            if (!File.Exists(referencePath))
            {
                _failures++;
                GD.PushError($"Missing visual reference: {referencePath}");
                return CaptureAttempt.Captured;
            }

            Image? reference = Image.LoadFromFile(referencePath);
            if (reference is null || reference.IsEmpty())
            {
                reference?.Dispose();
                _failures++;
                GD.PushError($"Could not load visual reference: {referencePath}");
                return CaptureAttempt.Captured;
            }

            using (reference)
            {
                if (reference.GetSize() != image.GetSize())
                {
                    _failures++;
                    GD.PushError($"Capture size differs for {frame.FileName}.");
                    return CaptureAttempt.Captured;
                }

                Godot.Collections.Dictionary metrics = image.ComputeImageMetrics(reference, useLuma: true);
                float rootMeanSquared = metrics["root_mean_squared"].AsSingle();
                if (!float.IsFinite(rootMeanSquared) || rootMeanSquared > MaximumRootMeanSquaredDifference)
                {
                    _failures++;
                    GD.PushError(
                        $"Visual regression in {frame.FileName}: RMSE {rootMeanSquared:0.0000} " +
                        $"> {MaximumRootMeanSquaredDifference:0.0000}.");
                }
            }
        }

        return CaptureAttempt.Captured;
    }

    private FacePose BuildCapturePose(CaptureFrame frame)
    {
        FacePose expression = _animations!.CurrentPose;
        FacePose pose = expression with { JawOpen = 0 };
        _stage!.ShowGuides = frame.Variant == "guides";
        if (frame.Action is { } action)
        {
            pose = pose with
            {
                LeftGazeX = action.Gaze.X, RightGazeX = action.Gaze.X,
                LeftGazeY = action.Gaze.Y, RightGazeY = action.Gaze.Y,
                LeftEyelidOpen = action.EyelidOpen, RightEyelidOpen = action.EyelidOpen,
                LightingIntensity = pose.LightingIntensity * action.LightingMultiplier,
            };
            pose = SpeechPoseCompositor.Compose(pose,
                new(action.JawOpen, action.MouthWidth, action.MouthRoundness, action.SpeechBlend));
        }
        PerformanceController performance = new(42);
        performance.Handle(new SelectCharacterCommand(_characterId));
        if (frame.Gesture is { } gesture)
        {
            performance.Handle(new SetMotionAmountCommand(1));
            performance.Handle(new PlayGestureCommand(Guid.NewGuid(), gesture, 1));
            performance.Update(frame.PerformanceTime);
            pose = performance.ComposePose(pose);
        }
        switch (frame.Variant)
        {
            case "character-idle":
                performance.Handle(new SetBehaviorStateCommand(BehaviorState.Idle));
                performance.Update(frame.PerformanceTime);
                pose = performance.ComposePose(pose);
                break;
            case "show-tilt": case "show-nod": case "show-shake":
                GestureId shownGesture = frame.Variant == "show-tilt" ? GestureId.CuriousTilt :
                    frame.Variant == "show-nod" ? GestureId.Nod : GestureId.Shake;
                performance.Handle(new PlayGestureCommand(Guid.NewGuid(), shownGesture));
                performance.Update(Math.Max(0, frame.PerformanceTime - .3));
                pose = performance.ComposePose(pose);
                break;
            case "eyes-up": pose = pose with { LeftGazeY = -.8f, RightGazeY = -.8f }; break;
            case "eyes-down": pose = pose with { LeftGazeY = .8f, RightGazeY = .8f }; break;
            case "eyes-down-blink": pose = pose with { LeftGazeY = .8f, RightGazeY = .8f,
                LeftEyelidOpen = .5f, RightEyelidOpen = .5f }; break;
            case "pupil-small": pose = pose with { PupilSize = .2f }; break;
            case "pupil-large": pose = pose with { PupilSize = .95f, LeftGazeX = .9f, RightGazeX = .9f }; break;
            case "asymmetry": pose = pose with { LeftBrowTension = -.5f, RightBrowTension = .55f,
                LeftMouthCorner = .15f, RightMouthCorner = 1 }; break;
            case "motion": case "guides": pose = pose with { MotionX = 1, MotionY = -1, MotionRoll = 1 }; break;
            case "speech": pose = SpeechPoseCompositor.Compose(pose, new(.65f, .3f, .9f, 1)); break;
            case "attention": case "conversation": case "surprise": case "interruption":
                pose = SampleScenario(frame.Variant, frame.PerformanceTime, pose, _characterId);
                break;
        }
        return pose.Clamp();
    }

    private static FacePose SampleScenario(string scenario, double time, FacePose expression, string characterId)
    {
        PerformanceController controller = new(42);
        controller.Handle(new SelectCharacterCommand(characterId));
        List<(double Time, AnimationCommand Command)> beats = scenario switch
        {
            "attention" => [(0, new SetGazeTargetCommand(Guid.NewGuid(), .7f, -.2f, 5.5)),
                (1.4, new PlayGestureCommand(Guid.NewGuid(), GestureId.CuriousTilt)),
                (2.6, new PlayGestureCommand(Guid.NewGuid(), GestureId.Blink)),
                (3.5, new PlayGestureCommand(Guid.NewGuid(), GestureId.Nod))],
            "conversation" => [(0, new SetBehaviorStateCommand(BehaviorState.Listening)),
                (2, new SetBehaviorStateCommand(BehaviorState.Thinking)),
                (4, new SetBehaviorStateCommand(BehaviorState.Listening)),
                (4.4, new PlayGestureCommand(Guid.NewGuid(), GestureId.LeftWink))],
            "surprise" => [(0.6, new PlayGestureCommand(Guid.NewGuid(), GestureId.Surprise))],
            _ => [(0, new PlayGestureCommand(Guid.NewGuid(), GestureId.CuriousTilt)),
                (1, new StopCommand())],
        };
        double cursor = 0;
        foreach (var beat in beats.Where(beat => beat.Time <= time))
        {
            controller.Update(beat.Time - cursor);
            controller.Handle(beat.Command);
            cursor = beat.Time;
        }
        controller.Update(time - cursor);
        FacePose pose = controller.ComposePose(expression);
        double speechStart = scenario == "conversation" ? 4 : 0;
        if ((scenario == "conversation" && time >= 4) || (scenario == "interruption" && time < 1.36))
        {
            ActionSceneController speech = new(seed: 42);
            speech.ConfigureSpeech(SpeechPhrasePlanner.Plan("Hello there, little friend!", TimeSpan.FromSeconds(3)), TimeSpan.FromSeconds(3));
            speech.SetSelected(SceneId.Talking, true);
            speech.Update(Math.Min(time - speechStart, scenario == "interruption" ? 1 : 3));
            if (scenario == "interruption" && time >= 1) { speech.BeginSpeechRelease(); speech.Update(time - 1); }
            ActionSceneFrame mouth = speech.Frame;
            pose = SpeechPoseCompositor.Compose(pose, new(mouth.JawOpen, mouth.MouthWidth, mouth.MouthRoundness, mouth.SpeechBlend));
        }
        return pose;
    }

    private void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _configured = false;
        DisconnectPostDraw();
        GD.Print($"Captured {_frames.Length} deterministic frames to {_captureDirectory}.");
        GetTree().Quit(_failures == 0 ? 0 : VisualDifferenceExitCode);
    }

    private void FailAndQuit(string message, int exitCode)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _configured = false;
        _capturePending = false;
        DisconnectPostDraw();
        GD.PushError(message);
        GetTree().Quit(exitCode);
    }

    private void DisconnectPostDraw()
    {
        if (!_subscribedToPostDraw)
        {
            return;
        }

        RenderingServer.FramePostDraw -= OnFramePostDraw;
        _subscribedToPostDraw = false;
    }

    private static bool HasGpuBackedRenderer(out string description)
    {
        string display = DisplayServer.GetName();
        string driver = RenderingServer.GetCurrentRenderingDriverName();
        string adapter = RenderingServer.GetVideoAdapterName();
        description =
            $"display={ValueOrNone(display)}, driver={ValueOrNone(driver)}, adapter={ValueOrNone(adapter)}";

        // Some valid Compatibility drivers do not expose an adapter name. The
        // display and rendering-driver identities are the reliable preflight;
        // the bounded readback retries below remain the final capability check.
        return !string.Equals(display, "headless", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(driver, "dummy", StringComparison.OrdinalIgnoreCase);
    }

    private static string ValueOrNone(string value) =>
        string.IsNullOrWhiteSpace(value) ? "<none>" : $"'{value}'";

    private static string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.StartsWith("res://", StringComparison.Ordinal) ||
            path.StartsWith("user://", StringComparison.Ordinal))
        {
            return ProjectSettings.GlobalizePath(path);
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(path, ProjectSettings.GlobalizePath("res://"));
    }

    private readonly record struct CaptureFrame(
        EmotionId? Scene,
        double Progress,
        string FileName,
        float EmotionAmount = 1f,
        ActionSceneFrame? Action = null,
        Vector2? CameraOrbitDegrees = null,
        float ShellThickness = 1f,
        GestureId? Gesture = null,
        double PerformanceTime = 0,
        string? Variant = null);

    private enum CaptureAttempt
    {
        Captured,
        Retry,
    }
}
