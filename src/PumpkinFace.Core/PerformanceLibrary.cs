namespace PumpkinFace.Core;

public sealed record PerformanceDefinition(string Id, string Title, string Description, double Duration, string? AudioFile,
    string AudioLabel = "voice");

/// <summary>Small, independent performances. Every clip starts and finishes at the supplied expression.</summary>
public static class PerformanceLibrary
{
    public const string SongId = "your-song";
    public static IReadOnlyList<PerformanceDefinition> All { get; } = Array.AsReadOnly<PerformanceDefinition>([
        new("little-scare", "A little scare", "A curious look, Boo! and a laugh", 13.5, "jack-playful-scare.wav"),
        new("boo", "Boo!", "A quick windup and a recorded Boo!", 2.7, "boo.wav"),
        new("chuckle", "Just kidding", "A warm, recorded chuckle", 3.5, "chuckle.wav"),
        new("wink", "Our little secret", "A crooked smile and a silent wink", 3, null),
        new("vader", "Vader’s entrance", "Breathing, a red lightsaber ignition and reflected glow, then a slow nod", VaderEntrancePerformance.Duration, "vader-entrance.wav", "saber + breath"),
        new("organ", "Haunted organ", "A theatrical sway to a spooky organ", 20, "haunted-organ.wav", "music"),
    ]);

    public static PerformanceDefinition? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    public static FacePose Sample(string id, double time, FacePose expression, float motion = .65f,
        double songDuration = 30, float tempo = 120)
    {
        double duration = id == SongId ? songDuration : Find(id)?.Duration ?? 0;
        if (!double.IsFinite(time) || !double.IsFinite(duration) || time <= 0 || time >= duration)
            return expression.Clamp();
        if (id == "little-scare") return PlayfulScarePerformance.Sample(time, expression, motion);
        if (id == "vader") return VaderEntrancePerformance.Sample(time, expression, motion);

        FacePose pose = id switch
        {
            // Cap the source time before the next vocal reaction starts.
            "boo" => PlayfulScarePerformance.Sample(Math.Min(6.9, 5 + time), expression, motion),
            "chuckle" => PlayfulScarePerformance.Sample(7 + Math.Max(0, time - .3), expression, motion),
            "wink" => PlayfulScarePerformance.Sample(10.5 + time, expression, motion),
            "organ" => Dance(time, expression, motion, 72),
            SongId => Dance(time, expression, motion, tempo),
            _ => expression,
        };
        float blend = Smooth(time / .3) * Smooth((duration - time) / .6);
        return FacePose.Lerp(expression, pose, blend).Clamp();
    }

    private static FacePose Dance(double time, FacePose expression, float motion, float tempo)
    {
        double beat = time * (float.IsFinite(tempo) ? Math.Clamp(tempo, 40, 200) : 120) / 60;
        float sway = (float)Math.Sin(beat * Math.PI);
        float lift = (float)(.5 - .5 * Math.Cos(beat * Math.Tau));
        double blinkTime = time % 4.7;
        float blink = Smooth((blinkTime - 3.8) / .08) * Smooth((4.12 - blinkTime) / .20);
        return PoseCompositor.Compose(expression, new FacePose {
            MotionRoll = sway * .22f, MotionX = sway * .045f, MotionY = -lift * .065f,
            LeftGazeX = sway * .12f, RightGazeX = sway * .12f,
            LeftEyelidOpen = -Math.Max(blink, .12f * lift),
            RightEyelidOpen = -Math.Max(blink, .20f * lift),
            LeftBrowTension = -.12f * lift, RightBrowTension = -.22f * lift,
            LeftMouthCorner = .08f * lift, RightMouthCorner = .14f * lift,
        }, motion);
    }

    private static float Smooth(double value)
    {
        float t = (float)Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
