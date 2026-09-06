using Godot;

namespace PumpkinFace.Display.Rendering;

public static class MouthContourDeformer
{
    public static Vector2[] Round(
        Vector2[] points, Vector2 center, float amount, float topLimit)
    {
        if (amount <= 0) return points;
        // Match perimeter order, not polar angle: teeth and the concave smile can
        // double back around the center and would otherwise create crossing edges.
        int left = 0, right = 0;
        for (int i = 1; i < points.Length; i++)
        {
            if (points[i].X < points[left].X) left = i;
            if (points[i].X > points[right].X) right = i;
        }
        float rx = points.Max(p => Mathf.Abs(p.X - center.X)) * Mathf.Lerp(1, .55f, amount);
        float ry = Mathf.Min(points.Max(p => Mathf.Abs(p.Y - center.Y)), Mathf.Max(8, center.Y - topLimit));
        Vector2[] result = new Vector2[points.Length];
        MapArc(left, right, true);
        MapArc(right, left, false);
        return result;

        void MapArc(int start, int end, bool upper)
        {
            List<int> indices = [start];
            for (int i = (start + 1) % points.Length; i != end; i = (i + 1) % points.Length) indices.Add(i);
            indices.Add(end);
            float length = 0;
            for (int i = 1; i < indices.Count; i++) length += points[indices[i - 1]].DistanceTo(points[indices[i]]);
            float distance = 0;
            for (int i = 0; i < indices.Count; i++)
            {
                if (i > 0) distance += points[indices[i - 1]].DistanceTo(points[indices[i]]);
                float angle = (upper ? Mathf.Pi : 0) + Mathf.Pi * distance / Mathf.Max(1, length);
                Vector2 ellipse = center + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
                result[indices[i]] = points[indices[i]].Lerp(ellipse, amount);
            }
        }
    }

}
