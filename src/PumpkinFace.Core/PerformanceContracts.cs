namespace PumpkinFace.Core;

public enum GestureId { Blink, LeftWink, RightWink, CuriousTilt, Nod, Shake, Surprise, Delight }
public enum BehaviorState { Idle, Listening, Thinking }
public enum PerformanceOutcome { Running, Completed, Cancelled, Rejected }

[Flags]
public enum PerformanceChannels
{
    None = 0, Gaze = 1, LeftLid = 2, RightLid = 4, Brows = 8,
    Pupils = 16, Mouth = 32, Motion = 64,
}

/// <summary>Maximum whole-face travel as a fraction of the design canvas, and roll in degrees.</summary>
public static class PerformanceMotionLimits
{
    public const float HorizontalFraction = .04f;
    public const float VerticalFraction = .05f;
    public const float RollDegrees = 12f;
}

public sealed record GestureDefinition(GestureId Id, string Label, PerformanceChannels Channels,
    double Duration, double Anticipation, double ActionEnd, double SettleEnd)
{
    public float DefaultIntensity => 0.65f;
    public float MinimumIntensity => 0f;
    public float MaximumIntensity => 1f;
}

public static class GestureCatalog
{
    public static IReadOnlyList<GestureDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new GestureDefinition(GestureId.Blink, "Blink", PerformanceChannels.LeftLid | PerformanceChannels.RightLid, .28, 0, .10, .28),
        new GestureDefinition(GestureId.LeftWink, "Left wink", PerformanceChannels.LeftLid, .65, .04, .30, .65),
        new GestureDefinition(GestureId.RightWink, "Right wink", PerformanceChannels.RightLid, .65, .04, .30, .65),
        new GestureDefinition(GestureId.CuriousTilt, "Curious tilt", PerformanceChannels.Motion | PerformanceChannels.Brows, 1.8, .16, 1.1, 1.8),
        new GestureDefinition(GestureId.Nod, "Nod", PerformanceChannels.Motion, 1.0, .12, .65, 1.0),
        new GestureDefinition(GestureId.Shake, "Shake", PerformanceChannels.Motion, 1.2, .12, .85, 1.2),
        new GestureDefinition(GestureId.Surprise, "Surprise", PerformanceChannels.Brows | PerformanceChannels.Pupils | PerformanceChannels.Mouth | PerformanceChannels.Motion, 1.5, .10, .70, 1.5),
        new GestureDefinition(GestureId.Delight, "Delight", PerformanceChannels.Brows | PerformanceChannels.Mouth | PerformanceChannels.Motion, 1.8, .15, 1.05, 1.8),
    });
    public static GestureDefinition Get(GestureId id) => All.First(item => item.Id == id);
}

public abstract record PerformanceRequestCommand(Guid RequestId) : AnimationCommand;
public sealed record PlayGestureCommand(Guid RequestId, GestureId Gesture, float Intensity = .65f)
    : PerformanceRequestCommand(RequestId);
/// <summary>Screen coordinates: positive X is right, positive Y is down; both -1..1.</summary>
public sealed record SetGazeTargetCommand(Guid RequestId, float X, float Y, double HoldSeconds = 2)
    : PerformanceRequestCommand(RequestId);
public sealed record SetBehaviorStateCommand(BehaviorState? State) : AnimationCommand;
public sealed record SetMotionAmountCommand(float Amount) : AnimationCommand;
public sealed record CancelPerformanceCommand(Guid RequestId) : AnimationCommand;
public sealed record PlayPerformanceDemoCommand : AnimationCommand;
public sealed record PerformanceStatus(Guid RequestId, PerformanceOutcome Outcome, string? Reason = null);
public sealed record PerformanceSnapshot(BehaviorState? State, float MotionAmount,
    IReadOnlyList<PerformanceStatus> Requests, string CharacterId = CharacterCatalog.DefaultId);
public interface IPerformanceStatusSource { PerformanceSnapshot Snapshot { get; } }

/// <summary>Reports admission failures as well as execution outcomes without calling Godot.</summary>
public sealed class PerformanceCommandEndpoint(IAnimationCommandSink queue, PerformanceController controller)
    : IAnimationCommandSink, IPerformanceStatusSource
{
    public PerformanceSnapshot Snapshot => controller.Snapshot;
    public bool TryPost(AnimationCommand command)
    {
        if (queue.TryPost(command)) return true;
        if (command is PerformanceRequestCommand request)
            controller.Reject(request.RequestId, "The control queue is full.");
        return false;
    }
}
