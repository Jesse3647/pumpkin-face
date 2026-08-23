using Godot;

namespace PumpkinFace.Display.UI;

public enum PreviewEditKind
{
    Move,
    Scale,
    Rotate,
}

public readonly record struct PreviewEdit(PreviewEditKind Kind, Vector2 Delta, float Amount);

public readonly record struct PreviewTransformState(
    Vector2 Offset,
    Vector2 Scale,
    float RotationDegrees);

/// <summary>
/// Displays the exact projector framebuffer and supplies direct-manipulation
/// handles for the normalized calibration transform.
/// </summary>
public sealed partial class ProjectionPreview : Control
{
    private const float HandleRadius = 8f;
    private const float RotationHandleDistance = 28f;

    private Texture2D? _texture;
    private PreviewTransformState _transform = new(Vector2.Zero, Vector2.One, 0f);
    private DragMode _dragMode;
    private Vector2 _lastMouse;
    private Vector2 _dragCenter;
    private float _lastDistance;
    private float _lastAngle;

    public event Action<PreviewEdit>? TransformEdited;

    public event Action<Vector2>? OrbitDragged;

    public bool HandlesVisible { get; set; }

    public ProjectionPreview()
    {
        CustomMinimumSize = new Vector2(640, 360);
        MouseDefaultCursorShape = CursorShape.Cross;
        ClipContents = true;
    }

    public void SetPreviewTexture(Texture2D? texture)
    {
        _texture = texture;
        QueueRedraw();
    }

    public void SetTransformState(PreviewTransformState state)
    {
        _transform = state;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
        Rect2 content = GetContentRect();

        if (_texture is not null)
        {
            DrawTextureRect(_texture, content, false);
        }

        if (!HandlesVisible)
        {
            return;
        }

        Vector2[] corners = GetFaceCorners(content);
        Color outline = new("ffb347");
        for (int i = 0; i < corners.Length; i++)
        {
            DrawLine(corners[i], corners[(i + 1) % corners.Length], outline, 2f, true);
            DrawCircle(corners[i], HandleRadius, new Color(0.08f, 0.06f, 0.04f, 0.95f));
            DrawArc(corners[i], HandleRadius, 0, Mathf.Tau, 20, outline, 2f, true);
        }

        Vector2 topCenter = (corners[0] + corners[1]) * 0.5f;
        Vector2 center = GetFaceCenter(content);
        Vector2 outward = (topCenter - center).Normalized();
        Vector2 rotateHandle = topCenter + outward * RotationHandleDistance;
        DrawLine(topCenter, rotateHandle, outline, 2f, true);
        DrawCircle(rotateHandle, HandleRadius, outline);
        DrawCircle(center, 3f, outline);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button &&
            button.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            if (button.Pressed)
            {
                if (button.ButtonIndex == MouseButton.Right || !HandlesVisible)
                {
                    _dragMode = DragMode.Orbit;
                    _lastMouse = button.Position;
                }
                else
                {
                    BeginDrag(button.Position);
                }

                AcceptEvent();
            }
            else if (_dragMode != DragMode.None)
            {
                _dragMode = DragMode.None;
                AcceptEvent();
            }

            return;
        }

        if (@event is InputEventMouseMotion motion && _dragMode != DragMode.None)
        {
            ContinueDrag(motion.Position);
            AcceptEvent();
        }
    }

    private void BeginDrag(Vector2 mouse)
    {
        Rect2 content = GetContentRect();
        Vector2[] corners = GetFaceCorners(content);
        _dragCenter = GetFaceCenter(content);
        Vector2 topCenter = (corners[0] + corners[1]) * 0.5f;
        Vector2 rotateHandle = topCenter + (topCenter - _dragCenter).Normalized() * RotationHandleDistance;

        if (mouse.DistanceTo(rotateHandle) <= HandleRadius * 2f)
        {
            _dragMode = DragMode.Rotate;
            _lastAngle = (mouse - _dragCenter).Angle();
        }
        else if (corners.Any(corner => mouse.DistanceTo(corner) <= HandleRadius * 2f))
        {
            _dragMode = DragMode.Scale;
            _lastDistance = Mathf.Max(1f, mouse.DistanceTo(_dragCenter));
        }
        else if (IsInsideFace(mouse, corners))
        {
            _dragMode = DragMode.Move;
            _lastMouse = mouse;
        }
        else
        {
            _dragMode = DragMode.None;
        }
    }

    private void ContinueDrag(Vector2 mouse)
    {
        Rect2 content = GetContentRect();
        switch (_dragMode)
        {
            case DragMode.Move:
                {
                    Vector2 pixelDelta = mouse - _lastMouse;
                    _lastMouse = mouse;
                    Vector2 normalized = new(
                        pixelDelta.X / Mathf.Max(1f, content.Size.X),
                        pixelDelta.Y / Mathf.Max(1f, content.Size.Y));
                    TransformEdited?.Invoke(new PreviewEdit(PreviewEditKind.Move, normalized, 0f));
                    break;
                }
            case DragMode.Scale:
                {
                    float distance = Mathf.Max(1f, mouse.DistanceTo(_dragCenter));
                    float ratio = Mathf.Clamp(distance / _lastDistance, 0.8f, 1.25f);
                    _lastDistance = distance;
                    TransformEdited?.Invoke(new PreviewEdit(PreviewEditKind.Scale, Vector2.Zero, ratio));
                    break;
                }
            case DragMode.Rotate:
                {
                    float angle = (mouse - _dragCenter).Angle();
                    float delta = Mathf.RadToDeg(Mathf.AngleDifference(_lastAngle, angle));
                    _lastAngle = angle;
                    TransformEdited?.Invoke(new PreviewEdit(PreviewEditKind.Rotate, Vector2.Zero, delta));
                    break;
                }
            case DragMode.Orbit:
                {
                    Vector2 delta = mouse - _lastMouse;
                    _lastMouse = mouse;
                    if (delta != Vector2.Zero)
                    {
                        OrbitDragged?.Invoke(delta);
                    }

                    break;
                }
        }
    }

    private Rect2 GetContentRect()
    {
        Vector2 source = _texture?.GetSize() ?? new Vector2(16, 9);
        if (source.X <= 0 || source.Y <= 0 || Size.X <= 0 || Size.Y <= 0)
        {
            return new Rect2(Vector2.Zero, Size);
        }

        float scale = Mathf.Min(Size.X / source.X, Size.Y / source.Y);
        Vector2 fitted = source * scale;
        return new Rect2((Size - fitted) * 0.5f, fitted);
    }

    private Vector2 GetFaceCenter(Rect2 content) =>
        content.Position + content.Size * (new Vector2(0.5f, 0.5f) + _transform.Offset);

    private Vector2[] GetFaceCorners(Rect2 content)
    {
        Vector2 center = GetFaceCenter(content);
        Vector2 halfSize = content.Size * new Vector2(0.31f, 0.27f) * _transform.Scale;
        float angle = Mathf.DegToRad(_transform.RotationDegrees);
        Vector2[] local =
        [
            new(-halfSize.X, -halfSize.Y),
            new(halfSize.X, -halfSize.Y),
            new(halfSize.X, halfSize.Y),
            new(-halfSize.X, halfSize.Y),
        ];

        for (int i = 0; i < local.Length; i++)
        {
            cornersRotate(ref local[i], angle);
            local[i] += center;
        }

        return local;

        static void cornersRotate(ref Vector2 point, float radians)
        {
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            point = new Vector2(
                point.X * cosine - point.Y * sine,
                point.X * sine + point.Y * cosine);
        }
    }

    private static bool IsInsideFace(Vector2 point, IReadOnlyList<Vector2> corners)
    {
        bool inside = false;
        for (int i = 0, j = corners.Count - 1; i < corners.Count; j = i++)
        {
            Vector2 a = corners[i];
            Vector2 b = corners[j];
            bool intersects = ((a.Y > point.Y) != (b.Y > point.Y)) &&
                              point.X < (b.X - a.X) * (point.Y - a.Y) /
                              (b.Y - a.Y) + a.X;
            if (intersects)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private enum DragMode
    {
        None,
        Move,
        Scale,
        Rotate,
        Orbit,
    }
}
