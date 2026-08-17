namespace PumpkinFace.Core;

/// <summary>
/// A compact, renderer-independent set of mouth shapes for future speech animation.
/// </summary>
public enum Viseme
{
    Silence,
    Neutral,
    Ah,
    Eh,
    Ih,
    Oh,
    Ooh,
    Fv,
    L,
    Mbp,
    Wq,
    Th,
    ChJSh,
    DnSt,
    Kg,
}

/// <param name="Timestamp">Position on the audio playback timeline.</param>
/// <param name="Shape">Target mouth shape.</param>
/// <param name="Weight">Blend weight in the 0..1 range.</param>
public readonly record struct VisemeFrame(TimeSpan Timestamp, Viseme Shape, float Weight)
{
    public VisemeFrame Normalize() => new(
        Timestamp < TimeSpan.Zero ? TimeSpan.Zero : Timestamp,
        Enum.IsDefined(Shape) ? Shape : Viseme.Neutral,
        float.IsFinite(Weight) ? Math.Clamp(Weight, 0f, 1f) : 0f);
}

/// <summary>
/// Supplies the playback time heard at the output, rather than render-frame time,
/// allowing future visemes to remain synchronized with speech.
/// </summary>
public interface IAudioClock
{
    bool IsPlaying { get; }

    TimeSpan PlaybackPosition { get; }

    TimeSpan OutputLatency { get; }

    TimeSpan AudiblePosition => PlaybackPosition <= OutputLatency
        ? TimeSpan.Zero
        : PlaybackPosition - OutputLatency;
}
