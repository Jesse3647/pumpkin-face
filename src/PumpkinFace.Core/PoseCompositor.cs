namespace PumpkinFace.Core;

/// <summary>Performance poses contain signed deltas, not absolute resting poses.</summary>
public static class PoseCompositor
{
    public static FacePose Compose(FacePose expression, FacePose delta, float motionAmount = 1f)
    {
        float motion = float.IsFinite(motionAmount) ? Math.Clamp(motionAmount, 0, 1) : 0;
        return (expression with
        {
            LeftGazeX = expression.LeftGazeX + delta.LeftGazeX,
            LeftGazeY = expression.LeftGazeY + delta.LeftGazeY,
            RightGazeX = expression.RightGazeX + delta.RightGazeX,
            RightGazeY = expression.RightGazeY + delta.RightGazeY,
            LeftEyelidOpen = expression.LeftEyelidOpen * (1 + delta.LeftEyelidOpen),
            RightEyelidOpen = expression.RightEyelidOpen * (1 + delta.RightEyelidOpen),
            LeftBrowTension = expression.LeftBrowTension + delta.LeftBrowTension,
            RightBrowTension = expression.RightBrowTension + delta.RightBrowTension,
            PupilSize = expression.PupilSize + delta.PupilSize,
            JawOpen = expression.JawOpen + delta.JawOpen,
            MouthWidth = expression.MouthWidth + delta.MouthWidth,
            MouthRoundness = expression.MouthRoundness + delta.MouthRoundness,
            LeftMouthCorner = expression.LeftMouthCorner + delta.LeftMouthCorner,
            RightMouthCorner = expression.RightMouthCorner + delta.RightMouthCorner,
            MotionX = delta.MotionX * motion,
            MotionY = delta.MotionY * motion,
            MotionRoll = delta.MotionRoll * motion,
        }).Clamp();
    }

    // This deliberately does not clamp: eyelid closures and asymmetric movements are signed deltas.
    internal static FacePose Mix(FacePose a, FacePose b, float t, PerformanceChannels c)
    {
        float M(float x, float y) => x + (y - x) * t;
        if (c.HasFlag(PerformanceChannels.Gaze)) a = a with {
            LeftGazeX = M(a.LeftGazeX, b.LeftGazeX), LeftGazeY = M(a.LeftGazeY, b.LeftGazeY),
            RightGazeX = M(a.RightGazeX, b.RightGazeX), RightGazeY = M(a.RightGazeY, b.RightGazeY) };
        if (c.HasFlag(PerformanceChannels.LeftLid)) a = a with { LeftEyelidOpen = M(a.LeftEyelidOpen, b.LeftEyelidOpen) };
        if (c.HasFlag(PerformanceChannels.RightLid)) a = a with { RightEyelidOpen = M(a.RightEyelidOpen, b.RightEyelidOpen) };
        if (c.HasFlag(PerformanceChannels.Brows)) a = a with {
            LeftBrowTension = M(a.LeftBrowTension, b.LeftBrowTension), RightBrowTension = M(a.RightBrowTension, b.RightBrowTension) };
        if (c.HasFlag(PerformanceChannels.Pupils)) a = a with { PupilSize = M(a.PupilSize, b.PupilSize) };
        if (c.HasFlag(PerformanceChannels.Mouth)) a = a with {
            JawOpen = M(a.JawOpen, b.JawOpen), MouthWidth = M(a.MouthWidth, b.MouthWidth),
            MouthRoundness = M(a.MouthRoundness, b.MouthRoundness),
            LeftMouthCorner = M(a.LeftMouthCorner, b.LeftMouthCorner), RightMouthCorner = M(a.RightMouthCorner, b.RightMouthCorner) };
        if (c.HasFlag(PerformanceChannels.Motion)) a = a with {
            MotionX = M(a.MotionX, b.MotionX), MotionY = M(a.MotionY, b.MotionY), MotionRoll = M(a.MotionRoll, b.MotionRoll) };
        return a;
    }
}

public readonly record struct SpeechMouthPose(float JawOpen, float Width, float Roundness, float Blend);
public static class SpeechPoseCompositor
{
    public static FacePose Compose(FacePose pose, SpeechMouthPose speech)
    {
        float t = float.IsFinite(speech.Blend) ? Math.Clamp(speech.Blend, 0, 1) : 0;
        return (pose with
        {
            JawOpen = pose.JawOpen + (speech.JawOpen - pose.JawOpen) * t,
            MouthWidth = pose.MouthWidth + (speech.Width - pose.MouthWidth) * t,
            MouthRoundness = pose.MouthRoundness + (speech.Roundness - pose.MouthRoundness) * t,
            SpeechBlend = t,
        }).Clamp();
    }
}
