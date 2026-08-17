namespace PumpkinFace.Core;

/// <summary>
/// Persistent projector alignment settings. Offsets are normalized to the output
/// dimensions so profiles remain useful at different resolutions.
/// </summary>
public sealed record ProjectionCalibration
{
    public const float MinimumOffset = -1f;
    public const float MaximumOffset = 1f;
    public const float MinimumScale = 0.25f;
    public const float MaximumScale = 3f;
    public const float MinimumRotationDegrees = -180f;
    public const float MaximumRotationDegrees = 180f;
    public const float MinimumEyeSpacing = 0.25f;
    public const float MaximumEyeSpacing = 2f;
    public const float MinimumBrightness = 0f;
    public const float MaximumBrightness = 2f;
    public const float MinimumGamma = 0.25f;
    public const float MaximumGamma = 4f;

    public float OffsetX { get; init; }

    public float OffsetY { get; init; }

    public float ScaleX { get; init; } = 1f;

    public float ScaleY { get; init; } = 1f;

    public float RotationDegrees { get; init; }

    public float EyeSpacing { get; init; } = 1f;

    public float MouthOffsetX { get; init; }

    public float MouthOffsetY { get; init; }

    public float MouthScaleX { get; init; } = 1f;

    public float MouthScaleY { get; init; } = 1f;

    public float Brightness { get; init; } = 1f;

    public float Gamma { get; init; } = 1f;

    public static ProjectionCalibration Default { get; } = new();

    /// <summary>
    /// Replaces non-finite values with defaults and clamps all fields to supported ranges.
    /// </summary>
    public ProjectionCalibration Normalize()
    {
        var fallback = Default;
        return this with
        {
            OffsetX = Clamp(OffsetX, MinimumOffset, MaximumOffset, fallback.OffsetX),
            OffsetY = Clamp(OffsetY, MinimumOffset, MaximumOffset, fallback.OffsetY),
            ScaleX = Clamp(ScaleX, MinimumScale, MaximumScale, fallback.ScaleX),
            ScaleY = Clamp(ScaleY, MinimumScale, MaximumScale, fallback.ScaleY),
            RotationDegrees = Clamp(
                RotationDegrees,
                MinimumRotationDegrees,
                MaximumRotationDegrees,
                fallback.RotationDegrees),
            EyeSpacing = Clamp(EyeSpacing, MinimumEyeSpacing, MaximumEyeSpacing, fallback.EyeSpacing),
            MouthOffsetX = Clamp(MouthOffsetX, MinimumOffset, MaximumOffset, fallback.MouthOffsetX),
            MouthOffsetY = Clamp(MouthOffsetY, MinimumOffset, MaximumOffset, fallback.MouthOffsetY),
            MouthScaleX = Clamp(MouthScaleX, MinimumScale, MaximumScale, fallback.MouthScaleX),
            MouthScaleY = Clamp(MouthScaleY, MinimumScale, MaximumScale, fallback.MouthScaleY),
            Brightness = Clamp(Brightness, MinimumBrightness, MaximumBrightness, fallback.Brightness),
            Gamma = Clamp(Gamma, MinimumGamma, MaximumGamma, fallback.Gamma),
        };
    }

    public ProjectionCalibration Clamp() => Normalize();

    public bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
    {
        error = ValidateField(nameof(OffsetX), OffsetX, MinimumOffset, MaximumOffset)
            ?? ValidateField(nameof(OffsetY), OffsetY, MinimumOffset, MaximumOffset)
            ?? ValidateField(nameof(ScaleX), ScaleX, MinimumScale, MaximumScale)
            ?? ValidateField(nameof(ScaleY), ScaleY, MinimumScale, MaximumScale)
            ?? ValidateField(
                nameof(RotationDegrees),
                RotationDegrees,
                MinimumRotationDegrees,
                MaximumRotationDegrees)
            ?? ValidateField(nameof(EyeSpacing), EyeSpacing, MinimumEyeSpacing, MaximumEyeSpacing)
            ?? ValidateField(nameof(MouthOffsetX), MouthOffsetX, MinimumOffset, MaximumOffset)
            ?? ValidateField(nameof(MouthOffsetY), MouthOffsetY, MinimumOffset, MaximumOffset)
            ?? ValidateField(nameof(MouthScaleX), MouthScaleX, MinimumScale, MaximumScale)
            ?? ValidateField(nameof(MouthScaleY), MouthScaleY, MinimumScale, MaximumScale)
            ?? ValidateField(nameof(Brightness), Brightness, MinimumBrightness, MaximumBrightness)
            ?? ValidateField(nameof(Gamma), Gamma, MinimumGamma, MaximumGamma);

        return error is null;
    }

    /// <summary>
    /// Returns this instance when valid; otherwise throws with a field-specific message.
    /// </summary>
    public ProjectionCalibration Validate()
    {
        if (!IsValid(out var error))
        {
            throw new ArgumentOutOfRangeException(nameof(ProjectionCalibration), error);
        }

        return this;
    }

    private static string? ValidateField(string name, float value, float minimum, float maximum)
    {
        if (!float.IsFinite(value))
        {
            return $"{name} must be finite.";
        }

        return value < minimum || value > maximum
            ? $"{name} must be between {minimum} and {maximum}."
            : null;
    }

    private static float Clamp(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
