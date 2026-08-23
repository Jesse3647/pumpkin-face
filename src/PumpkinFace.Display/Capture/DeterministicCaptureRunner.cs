using Godot;
using PumpkinFace.Core;
using PumpkinFace.Display.Animation;
using PumpkinFace.Display.Rendering;

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
            new ActionSceneFrame(Vector2.Zero, 1f, 0.48f, 1f)),
        new(EmotionId.Happy, 0.42, "scene-candle-sputter.png", 1f,
            new ActionSceneFrame(Vector2.Zero, 1f, 0f, 0.30f)),
        new(EmotionId.Happy, 0.42, "camera-orbit-left.png", 1f, null, new Vector2(35f, 0f)),
        new(EmotionId.Happy, 0.42, "camera-orbit-upper-right.png", 1f, null, new Vector2(-28f, 20f)),
    ];

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

        Directory.CreateDirectory(_captureDirectory);
        _stage.Resize(new Vector2I(1280, 720));
        _stage.ShowGuides = false;
        _stage.AutoAdvanceAnimationTime = false;
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

        _stage.SetPose(BuildCapturePose(Frames[_frameIndex]));
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
        if (_frameIndex >= Frames.Length)
        {
            Finish();
            return;
        }

        PrepareFrame();
    }

    private void PrepareFrame()
    {
        CaptureFrame frame = Frames[_frameIndex];
        _animations!.SetCaptureFrame(frame.Scene, frame.Progress);
        _stage!.SetCalibration(ProjectionCalibration.Default with
        {
            ShellThickness = frame.ShellThickness,
        });
        _stage!.EmotionAmount = frame.EmotionAmount;
        _stage.SetCameraOrbit(frame.CameraOrbitDegrees ?? Vector2.Zero);
        _stage!.AnimationTime = _frameIndex * 0.731 + frame.Progress * 4.0;
        _stage.SetPose(BuildCapturePose(frame));
        _settleFrames = 0;
        _capturePending = false;
        _postDrawWaitFrames = 0;
    }

    private CaptureAttempt CaptureCurrentFrame()
    {
        CaptureFrame frame = Frames[_frameIndex];
        Image? image = _stage!.Texture.GetImage();
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
        FacePose pose = _animations!.CurrentPose;
        ActionSceneFrame action = frame.Action ?? ActionSceneFrame.Rest;
        return pose with
        {
            LeftGazeX = action.Gaze.X,
            LeftGazeY = action.Gaze.Y,
            RightGazeX = action.Gaze.X,
            RightGazeY = action.Gaze.Y,
            LeftEyelidOpen = action.EyelidOpen,
            RightEyelidOpen = action.EyelidOpen,
            JawOpen = action.JawOpen,
            LightingIntensity = pose.LightingIntensity * action.LightingMultiplier,
        };
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
        GD.Print($"Captured {Frames.Length} deterministic frames to {_captureDirectory}.");
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
        float ShellThickness = 1f);

    private enum CaptureAttempt
    {
        Captured,
        Retry,
    }
}
