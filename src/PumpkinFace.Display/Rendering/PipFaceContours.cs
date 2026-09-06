using Godot;

namespace PumpkinFace.Display.Rendering;

/// <summary>
/// Smooth, hand-shaped contours for Pip. Matching vertex order across expressions
/// preserves the existing expression crossfades, carving, eyelids, and speech pipeline.
/// </summary>
internal static class PipFaceContours
{
    public static ReferenceFaceShape Happy { get; } = Build(0);
    public static ReferenceFaceShape Frightened { get; } = Build(-1);
    public static ReferenceFaceShape Sad { get; } = Build(1);

    private static ReferenceFaceShape Build(int emotion)
    {
        Vector2 left = new(-184, -128), right = new(184, -122);
        return new ReferenceFaceShape(
            Eye(left, new(114, 139), emotion, -1),
            Eye(right, new(108, 133), emotion, 1),
            Ellipse(new(0, 54), new(31, 23), 16), Mouth(emotion),
            left + new Vector2(10, 12), right + new Vector2(-10, 12),
            left + new Vector2(-9, -13), right + new Vector2(-29, -13),
            emotion == -1 ? 53 : 65, 18);
    }

    private static Vector2[] Eye(Vector2 center, Vector2 radius, int emotion, int side)
    {
        Vector2[] points = Ellipse(center, radius, 48);
        return points.Select(point =>
        {
            Vector2 local = point - center;
            // A bean-like lower cheek and a raised inner corner when worried.
            float lower = Mathf.Max(0, local.Y / radius.Y);
            float sadness = emotion == 1 ? 1 : 0;
            return center + new Vector2(local.X * (1 - .08f * lower),
                local.Y * (emotion == -1 ? 1.06f : 1) + sadness * side * local.X * .32f);
        }).ToArray();
    }

    private static Vector2[] Mouth(int emotion)
    {
        List<Vector2> points = [];
        for (int i = 0; i <= 32; i++) points.Add(Point(-1 + i / 16f, false));
        for (int i = 31; i > 0; i--) points.Add(Point(-1 + i / 16f, true));
        return points.ToArray();

        Vector2 Point(float u, bool bottom)
        {
            float arc = MathF.Sqrt(MathF.Max(0, 1 - u * u));
            if (emotion == -1) return new Vector2(u * 79, 231 + (bottom ? 97 : -97) * arc);
            if (emotion == 1) return new Vector2(u * 161, 247 - 76 * arc + (bottom ? 39 * arc : 0));
            // A smaller, toothless, asymmetric smile; raised corners leave space below the nose.
            return new Vector2(u * 226, 161 - 9 * u + (bottom ? 135 : 47) * arc);
        }
    }

    private static Vector2[] Ellipse(Vector2 center, Vector2 radius, int count) =>
        Enumerable.Range(0, count).Select(i =>
        {
            float angle = Mathf.Pi + Mathf.Tau * i / count;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }).ToArray();
}
