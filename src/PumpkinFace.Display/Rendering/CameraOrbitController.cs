using Godot;

namespace PumpkinFace.Display.Rendering;

/// <summary>
/// Converts preview drags into a bounded camera orbit and returns the camera to
/// its front view after a five-second idle period.
/// </summary>
public sealed class CameraOrbitController
{
    public const double ReturnDelaySeconds = 5.0;
    private const float ReturnSpeedDegreesPerSecond = 120f;

    private double _idleSeconds;

    public Vector2 Degrees { get; private set; }

    public bool IsReturning => _idleSeconds > ReturnDelaySeconds && Degrees != Vector2.Zero;

    public void SetDegrees(Vector2 degrees)
    {
        if (!degrees.IsFinite())
        {
            return;
        }

        Degrees = new Vector2(
            Mathf.Clamp(degrees.X, -60f, 60f),
            Mathf.Clamp(degrees.Y, -38f, 38f));
        _idleSeconds = 0d;
    }

    public void Drag(Vector2 pixelDelta)
    {
        if (!pixelDelta.IsFinite())
        {
            return;
        }

        SetDegrees(new Vector2(
            Degrees.X + pixelDelta.X * 0.24f,
            Degrees.Y - pixelDelta.Y * 0.20f));
    }

    public bool Update(double elapsedSeconds)
    {
        Vector2 previous = Degrees;
        _idleSeconds += Math.Max(0d, elapsedSeconds);
        if (_idleSeconds <= ReturnDelaySeconds || Degrees == Vector2.Zero)
        {
            return false;
        }

        Degrees = Degrees.MoveToward(
            Vector2.Zero,
            ReturnSpeedDegreesPerSecond * (float)Math.Max(0d, elapsedSeconds));
        if (Degrees.LengthSquared() < 0.0001f)
        {
            Degrees = Vector2.Zero;
        }

        return Degrees != previous;
    }
}
