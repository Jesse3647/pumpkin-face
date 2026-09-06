namespace PumpkinFace.Core;

/// <summary>A small authored scene sampled from audible playback time, including silent holds.</summary>
public static class PlayfulScarePerformance
{
    public const double Duration = 13.5;
    private const PerformanceChannels All = (PerformanceChannels)127;
    private readonly record struct Beat(double Time, FacePose Delta);
    private static readonly FacePose SideLook = new() { LeftGazeX = .48f, RightGazeX = .48f, LeftGazeY = -.08f, RightGazeY = -.08f };
    private static readonly FacePose Curious = new() { MotionRoll = .24f, LeftBrowTension = -.36f, RightBrowTension = .16f, LeftMouthCorner = -.18f };
    private static readonly FacePose Plotting = new() { LeftEyelidOpen = -.30f, RightEyelidOpen = -.19f, LeftBrowTension = .20f, RightBrowTension = -.18f, LeftMouthCorner = -.32f, MotionRoll = -.10f };
    private static readonly FacePose Windup = new() { LeftEyelidOpen = -.60f, RightEyelidOpen = -.48f, LeftBrowTension = .30f, RightBrowTension = .22f, MouthWidth = -.18f, LeftMouthCorner = -.40f, RightMouthCorner = -.40f, MotionY = .13f, MotionRoll = -.08f };
    private static readonly FacePose Scare = new() { LeftBrowTension = -.72f, RightBrowTension = -.64f, PupilSize = -.12f, JawOpen = .52f, MouthWidth = -.30f, MouthRoundness = .83f, LeftMouthCorner = -.65f, RightMouthCorner = -.60f, MotionY = -.15f };
    private static readonly FacePose Sheepish = new() { LeftGazeX = -.14f, RightGazeX = -.14f, LeftGazeY = .16f, RightGazeY = .16f, LeftBrowTension = -.32f, RightBrowTension = .10f, LeftMouthCorner = -.25f, MotionRoll = .16f };
    private static readonly FacePose Amused = new() { LeftEyelidOpen = -.22f, RightEyelidOpen = -.32f, LeftBrowTension = -.18f, RightBrowTension = -.08f, LeftMouthCorner = -.08f, MotionRoll = .12f };
    private static readonly Beat[] Beats =
    [
        new(0, default), new(.40, default), new(.53, SideLook), new(.78, SideLook),
        new(.92, default), new(1.65, default),
        new(2.05, Curious), new(3.18, Curious), new(3.70, Plotting), new(4.55, Plotting),
        new(5.25, Windup), new(5.68, Windup),
        // Eyes register before the mouth opens, then a small overshoot settles.
        new(5.82, Windup with { LeftEyelidOpen = 0, RightEyelidOpen = 0, LeftBrowTension = -.65f, RightBrowTension = -.55f }),
        new(6.00, Scare), new(6.34, Scare with { MotionY = -.06f }),
        new(6.58, Scare with { JawOpen = .14f, MouthRoundness = .32f, MotionY = 0 }),
        new(6.78, Sheepish), new(6.90, Sheepish),
        new(7.00, Sheepish with { LeftGazeX = 0, RightGazeX = 0, LeftGazeY = 0, RightGazeY = 0 }),
        new(7.16, Amused), new(8.84, Amused), new(10.70, Amused with { LeftEyelidOpen = -.10f, RightEyelidOpen = -.16f }), new(11.10, Amused with { MotionRoll = .04f }),
        new(11.65, Amused with { MotionRoll = .04f }), new(12.90, default), new(Duration, default),
    ];

    public static FacePose Sample(double seconds, FacePose expression, float motionAmount = .65f)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds >= Duration) return expression.Clamp();
        int next = 1;
        while (next < Beats.Length - 1 && Beats[next].Time < seconds) next++;
        Beat a = Beats[next - 1], b = Beats[next];
        FacePose delta = PoseCompositor.Mix(a.Delta, b.Delta, Smooth((seconds - a.Time) / (b.Time - a.Time)), All);
        float blink = Pulse(seconds, 1.31, 1.37, 1.41, 1.60);
        float wink = Pulse(seconds, 11.18, 11.28, 11.49, 11.78);
        delta = delta with {
            LeftEyelidOpen = Math.Min(delta.LeftEyelidOpen, -Math.Max(blink, wink)),
            RightEyelidOpen = Math.Min(delta.RightEyelidOpen, -Pulse(seconds, 1.33, 1.39, 1.43, 1.62)) };
        FacePose pose = PoseCompositor.Compose(expression, delta, motionAmount);
        foreach (JackVocalTrack.Cue cue in JackVocalTrack.Cues)
        {
            double local = seconds - cue.Start;
            double length = (cue.Energy.Length - 1) / 100d;
            if (local < -.08 || local > length + .12) continue;
            double sample = Math.Clamp(local * 100, 0, cue.Energy.Length - 1);
            int index = (int)sample;
            float energy = cue.Energy[index] + (cue.Energy[Math.Min(index + 1, cue.Energy.Length - 1)] - cue.Energy[index]) * (float)(sample - index);
            float blend = Smooth((local + .08) / .08) * (1 - Smooth((local - length) / .12));
            float jaw = .06f + energy * .70f;
            pose = SpeechPoseCompositor.Compose(pose, new(jaw, cue.Width, cue.Roundness, blend));
            if (cue.Kind == JackVocalTrack.Reaction.Laugh)
                pose = pose with { MotionY = pose.MotionY - energy * .055f * Math.Clamp(motionAmount, 0, 1),
                    LeftEyelidOpen = pose.LeftEyelidOpen * (1 - energy * .16f) };
        }
        return pose.Clamp();
    }

    public static string BeatLabel(double seconds) => seconds switch
    {
        < 1.65 => "Noticing you", < 3.4 => "A curious thought", < 5.68 => "Planning a little scare",
        < 6.65 => "Boo!", < 7.0 => "Was that scary?", < 8.84 => "Laughing it off", < 10.8 => "Just kidding", _ => "A friendly wink",
    };
    private static float Smooth(double value) { float t = (float)Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
    private static float Pulse(double t, double start, double peak, double hold, double end) =>
        Smooth((t - start) / (peak - start)) * (1 - Smooth((t - hold) / (end - hold)));
}
