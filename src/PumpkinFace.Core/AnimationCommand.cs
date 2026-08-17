namespace PumpkinFace.Core;

/// <summary>
/// Base type for commands accepted by the animation system. Future controllers
/// (local UI, HTTP, speech, or AI) should communicate through these commands.
/// </summary>
public abstract record AnimationCommand;

public sealed record PlaySceneCommand(SceneId Scene) : AnimationCommand;

public sealed record NextSceneCommand : AnimationCommand;

public sealed record SetAutoplayCommand(bool Enabled) : AnimationCommand;

public sealed record StopCommand : AnimationCommand;

public sealed record ApplyCalibrationCommand : AnimationCommand
{
    public ApplyCalibrationCommand(ProjectionCalibration calibration)
    {
        Calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
    }

    public ProjectionCalibration Calibration { get; }
}
