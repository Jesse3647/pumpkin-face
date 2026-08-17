using Godot;

namespace PumpkinFace.Display.Rendering;

/// <summary>
/// Output-space guides which remain fixed while the face is calibrated.
/// </summary>
internal sealed partial class ProjectionGuides : Node2D
{
    private Vector2I _outputSize = new(1920, 1080);

    public Vector2I OutputSize
    {
        get => _outputSize;
        set
        {
            _outputSize = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Vector2 size = _outputSize;
        Vector2 center = size * 0.5f;
        Color primary = new(1f, 0.56f, 0.10f, 0.82f);
        Color secondary = new(1f, 0.75f, 0.28f, 0.48f);

        Rect2 safeArea = new(size * 0.04f, size * 0.92f);
        DrawRect(safeArea, secondary, false, 2f, true);

        float crossLength = Mathf.Min(size.X, size.Y) * 0.035f;
        DrawLine(center - Vector2.Right * crossLength, center + Vector2.Right * crossLength, primary, 2f, true);
        DrawLine(center - Vector2.Down * crossLength, center + Vector2.Down * crossLength, primary, 2f, true);
        DrawCircle(center, 5f, primary, false, 2f, true);

        float marker = Mathf.Min(size.X, size.Y) * 0.022f;
        DrawCornerMarker(safeArea.Position, Vector2.Right, Vector2.Down, marker, primary);
        DrawCornerMarker(safeArea.Position + new Vector2(safeArea.Size.X, 0f), Vector2.Left, Vector2.Down, marker, primary);
        DrawCornerMarker(safeArea.End, Vector2.Left, Vector2.Up, marker, primary);
        DrawCornerMarker(safeArea.Position + new Vector2(0f, safeArea.Size.Y), Vector2.Right, Vector2.Up, marker, primary);
    }

    private void DrawCornerMarker(Vector2 corner, Vector2 horizontal, Vector2 vertical, float length, Color color)
    {
        DrawLine(corner, corner + horizontal * length, color, 3f, true);
        DrawLine(corner, corner + vertical * length, color, 3f, true);
    }
}

/// <summary>
/// Face-space guides which follow calibration scaling, rotation, and offset.
/// </summary>
internal sealed partial class FaceDesignGuides : Node2D
{
    public override void _Draw()
    {
        Vector2 half = FaceRig.DesignSize * 0.5f;
        Rect2 bounds = new(-half, FaceRig.DesignSize);
        Color outline = new(0.33f, 0.82f, 1f, 0.68f);
        Color feature = new(0.33f, 0.82f, 1f, 0.42f);

        DrawRect(bounds, outline, false, 2f, true);
        DrawLine(new Vector2(-half.X, 0f), new Vector2(half.X, 0f), feature, 1.5f, true);
        DrawLine(new Vector2(0f, -half.Y), new Vector2(0f, half.Y), feature, 1.5f, true);
        DrawCircle(Vector2.Zero, 8f, outline, false, 2f, true);

        DrawRect(new Rect2(-610f, -300f, 490f, 310f), feature, false, 1.5f, true);
        DrawRect(new Rect2(120f, -300f, 490f, 310f), feature, false, 1.5f, true);
        DrawRect(new Rect2(-610f, 70f, 1220f, 450f), feature, false, 1.5f, true);

        DrawHandle(new Vector2(-half.X, -half.Y), outline);
        DrawHandle(new Vector2(half.X, -half.Y), outline);
        DrawHandle(new Vector2(half.X, half.Y), outline);
        DrawHandle(new Vector2(-half.X, half.Y), outline);
        DrawHandle(new Vector2(0f, -half.Y - 36f), outline);
        DrawLine(new Vector2(0f, -half.Y), new Vector2(0f, -half.Y - 36f), outline, 2f, true);
    }

    private void DrawHandle(Vector2 center, Color color)
    {
        DrawCircle(center, 9f, new Color(0f, 0f, 0f, 0.88f));
        DrawCircle(center, 9f, color, false, 2f, true);
    }
}
