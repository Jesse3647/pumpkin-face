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
        new(null, 0.50, "neutral-050.png"),
        new(SceneId.Watchful, 0.16, "watchful-016.png"),
        new(SceneId.Watchful, 0.52, "watchful-052.png"),
        new(SceneId.Watchful, 0.80, "watchful-080.png"),
        new(SceneId.Frightened, 0.16, "frightened-016.png"),
        new(SceneId.Frightened, 0.42, "frightened-042.png"),
        new(SceneId.Frightened, 0.82, "frightened-082.png"),
        new(SceneId.Drowsy, 0.32, "drowsy-032.png"),
        new(SceneId.Drowsy, 0.62, "drowsy-062.png"),
        new(SceneId.Drowsy, 0.91, "drowsy-091.png"),
        new(SceneId.Mischievous, 0.28, "mischievous-028.png"),
        new(SceneId.Mischievous, 0.58, "mischievous-058.png"),
        new(SceneId.Mischievous, 0.82, "mischievous-082.png"),
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

        _stage.SetPose(_animations.CurrentPose);
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
        _stage!.AnimationTime = _frameIndex * 0.731 + frame.Progress * 4.0;
        _stage.SetPose(_animations.CurrentPose);
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

    private readonly record struct CaptureFrame(SceneId? Scene, double Progress, string FileName);

    private enum CaptureAttempt
    {
        Captured,
        Retry,
    }
}
