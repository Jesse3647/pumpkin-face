namespace PumpkinFace.Core;

[Flags]
public enum FacePoseChannels
{
    None = 0,
    Gaze = 1 << 0,
    Pupils = 1 << 1,
    Eyelids = 1 << 2,
    Brows = 1 << 3,
    Mouth = 1 << 4,
    Motion = 1 << 5,
    Lighting = 1 << 6,
    All = Gaze | Pupils | Eyelids | Brows | Mouth | Motion | Lighting,
}

/// <summary>
/// Renderer-independent, normalized controls for the deformable face rig.
/// Signed controls use -1..1; openness/amount controls use 0..1; lighting uses 0..2.
/// </summary>
public readonly record struct FacePose
{
    public float LeftGazeX { get; init; }

    public float LeftGazeY { get; init; }

    public float RightGazeX { get; init; }

    public float RightGazeY { get; init; }

    public float PupilSize { get; init; }

    public float LeftEyelidOpen { get; init; }

    public float RightEyelidOpen { get; init; }

    public float LeftBrowTension { get; init; }

    public float RightBrowTension { get; init; }

    public float JawOpen { get; init; }

    public float MouthWidth { get; init; }

    public float MouthRoundness { get; init; }

    public float SpeechBlend { get; init; }

    public float LeftMouthCorner { get; init; }

    public float RightMouthCorner { get; init; }

    public float Tremble { get; init; }

    public float LightingIntensity { get; init; }

    public static FacePose Neutral { get; } = new()
    {
        PupilSize = 0.55f,
        LeftEyelidOpen = 1f,
        RightEyelidOpen = 1f,
        MouthWidth = 0.5f,
        LightingIntensity = 1f,
    };

    /// <summary>
    /// Returns a finite pose constrained to the renderer's stable input ranges.
    /// </summary>
    public FacePose Clamp()
    {
        var fallback = Neutral;
        return new FacePose
        {
            LeftGazeX = ClampSigned(LeftGazeX, fallback.LeftGazeX),
            LeftGazeY = ClampSigned(LeftGazeY, fallback.LeftGazeY),
            RightGazeX = ClampSigned(RightGazeX, fallback.RightGazeX),
            RightGazeY = ClampSigned(RightGazeY, fallback.RightGazeY),
            PupilSize = ClampUnit(PupilSize, fallback.PupilSize),
            LeftEyelidOpen = ClampUnit(LeftEyelidOpen, fallback.LeftEyelidOpen),
            RightEyelidOpen = ClampUnit(RightEyelidOpen, fallback.RightEyelidOpen),
            LeftBrowTension = ClampSigned(LeftBrowTension, fallback.LeftBrowTension),
            RightBrowTension = ClampSigned(RightBrowTension, fallback.RightBrowTension),
            JawOpen = ClampUnit(JawOpen, fallback.JawOpen),
            MouthWidth = ClampUnit(MouthWidth, fallback.MouthWidth),
            MouthRoundness = ClampUnit(MouthRoundness, fallback.MouthRoundness),
            SpeechBlend = ClampUnit(SpeechBlend, fallback.SpeechBlend),
            LeftMouthCorner = ClampSigned(LeftMouthCorner, fallback.LeftMouthCorner),
            RightMouthCorner = ClampSigned(RightMouthCorner, fallback.RightMouthCorner),
            Tremble = ClampUnit(Tremble, fallback.Tremble),
            LightingIntensity = Clamp(LightingIntensity, 0f, 2f, fallback.LightingIntensity),
        };
    }

    /// <summary>
    /// Interpolates selected channel groups. Unselected channels retain values from
    /// <paramref name="from"/>. The resulting pose is always clamped.
    /// </summary>
    public static FacePose Lerp(
        FacePose from,
        FacePose to,
        float amount,
        FacePoseChannels channels = FacePoseChannels.All)
    {
        var start = from.Clamp();
        var end = to.Clamp();
        var t = float.IsFinite(amount) ? Math.Clamp(amount, 0f, 1f) : 0f;

        var result = start;
        if (channels.HasFlag(FacePoseChannels.Gaze))
        {
            result = result with
            {
                LeftGazeX = Mix(start.LeftGazeX, end.LeftGazeX, t),
                LeftGazeY = Mix(start.LeftGazeY, end.LeftGazeY, t),
                RightGazeX = Mix(start.RightGazeX, end.RightGazeX, t),
                RightGazeY = Mix(start.RightGazeY, end.RightGazeY, t),
            };
        }

        if (channels.HasFlag(FacePoseChannels.Pupils))
        {
            result = result with { PupilSize = Mix(start.PupilSize, end.PupilSize, t) };
        }

        if (channels.HasFlag(FacePoseChannels.Eyelids))
        {
            result = result with
            {
                LeftEyelidOpen = Mix(start.LeftEyelidOpen, end.LeftEyelidOpen, t),
                RightEyelidOpen = Mix(start.RightEyelidOpen, end.RightEyelidOpen, t),
            };
        }

        if (channels.HasFlag(FacePoseChannels.Brows))
        {
            result = result with
            {
                LeftBrowTension = Mix(start.LeftBrowTension, end.LeftBrowTension, t),
                RightBrowTension = Mix(start.RightBrowTension, end.RightBrowTension, t),
            };
        }

        if (channels.HasFlag(FacePoseChannels.Mouth))
        {
            result = result with
            {
                JawOpen = Mix(start.JawOpen, end.JawOpen, t),
                MouthWidth = Mix(start.MouthWidth, end.MouthWidth, t),
                MouthRoundness = Mix(start.MouthRoundness, end.MouthRoundness, t),
                SpeechBlend = Mix(start.SpeechBlend, end.SpeechBlend, t),
                LeftMouthCorner = Mix(start.LeftMouthCorner, end.LeftMouthCorner, t),
                RightMouthCorner = Mix(start.RightMouthCorner, end.RightMouthCorner, t),
            };
        }

        if (channels.HasFlag(FacePoseChannels.Motion))
        {
            result = result with { Tremble = Mix(start.Tremble, end.Tremble, t) };
        }

        if (channels.HasFlag(FacePoseChannels.Lighting))
        {
            result = result with
            {
                LightingIntensity = Mix(start.LightingIntensity, end.LightingIntensity, t),
            };
        }

        return result.Clamp();
    }

    private static float Mix(float start, float end, float amount) => start + ((end - start) * amount);

    private static float ClampSigned(float value, float fallback) => Clamp(value, -1f, 1f, fallback);

    private static float ClampUnit(float value, float fallback) => Clamp(value, 0f, 1f, fallback);

    private static float Clamp(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
