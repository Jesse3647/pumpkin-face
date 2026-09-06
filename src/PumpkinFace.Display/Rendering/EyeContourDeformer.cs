using Godot;

namespace PumpkinFace.Display.Rendering;

internal readonly record struct EyeGeometry(Vector2[] Aperture, Vector2 Pupil, Vector2 Catchlight);

/// <summary>Moves lid boundaries across the opening without moving the eyeball beneath them.</summary>
internal static class EyeContourDeformer
{
    public static EyeGeometry Build(Vector2[] contour, Vector2 pupil, Vector2 catchlight,
        Vector2 scale, float openness, float gazeY)
    {
        Vector2 center = contour.Aggregate(Vector2.Zero, (sum, p) => sum + p) / contour.Length;
        Vector2 Scale(Vector2 p) => center + (p - center) * scale;
        Vector2[] opening = contour.Select(Scale).ToArray();
        return new(AnimateLids(opening, openness, gazeY), Scale(pupil), Scale(catchlight));
    }

    private static Vector2[] AnimateLids(Vector2[] opening, float openness, float gazeY)
    {
        float closure = 1 - Mathf.Clamp(openness, 0, 1);
        gazeY = Mathf.Clamp(gazeY, -1, 1);
        if (closure == 0 && gazeY == 0) return opening;

        float top = opening.Min(p => p.Y), bottom = opening.Max(p => p.Y);
        float height = Mathf.Max(1, bottom - top);
        float left = opening.Min(p => p.X), right = opening.Max(p => p.X);
        float centerX = (left + right) * .5f, halfWidth = Mathf.Max(1, (right - left) * .5f);
        // Both lids follow vertical attention, with a stronger response from the upper lid.
        // Only the aperture changes; pupil/catchlight anchors are never passed through this transform.
        Vector2[] followed = opening.Select(p => p + new Vector2(0,
            gazeY * height * Mathf.Lerp(.08f, .025f, (p.Y - top) / height))).ToArray();
        if (closure == 0) return followed;

        float Curve(float x) => -.04f * height * Mathf.Pow((x - centerX) / halfWidth, 2);
        // Flatten the gently curved lid seam to clip in a simple coordinate system.
        Vector2[] flattened = followed.Select(p => new Vector2(p.X, p.Y - Curve(p.X))).ToArray();
        float minimum = flattened.Min(p => p.Y), maximum = flattened.Max(p => p.Y);
        float seam = Mathf.Lerp(minimum, maximum, .78f);
        float gap = Mathf.Max(.5f, height * .004f);
        float upper = Mathf.Lerp(minimum, seam - gap * .5f, closure);
        float lower = Mathf.Lerp(maximum, seam + gap * .5f, closure);
        // A concave carving can split when a lid crosses a notch. Keep the largest
        // visible opening, rather than connecting separate islands with crossing edges.
        Vector2[] clipped = Clip(flattened, upper, true)
            .SelectMany(part => Clip(part, lower, false))
            .OrderByDescending(Area).FirstOrDefault() ?? [];
        List<Vector2> result = [];
        for (int i = 0; i < clipped.Length; i++)
        {
            Vector2 a = clipped[i], b = clipped[(i + 1) % clipped.Length];
            // Subdivide newly exposed lid edges so the curved seam remains curved after unwarping.
            bool onLid = Mathf.Abs(a.Y - b.Y) < .001f &&
                (Mathf.Abs(a.Y - upper) < .001f || Mathf.Abs(a.Y - lower) < .001f);
            int segments = onLid ? Math.Max(1, (int)Math.Ceiling(Mathf.Abs(b.X - a.X) / 12)) : 1;
            for (int j = 0; j < segments; j++)
            {
                Vector2 p = a.Lerp(b, (float)j / segments);
                result.Add(new Vector2(p.X, p.Y + Curve(p.X)));
            }
        }
        return result.ToArray();
    }

    private static IEnumerable<Vector2[]> Clip(Vector2[] polygon, float boundary, bool keepBelow)
    {
        bool Inside(Vector2 p) => keepBelow ? p.Y >= boundary : p.Y <= boundary;
        int outside = Array.FindIndex(polygon, p => !Inside(p));
        if (outside < 0) return [polygon];
        List<List<Vector2>> chains = [];
        List<Vector2>? chain = null;
        for (int offset = 1; offset <= polygon.Length; offset++)
        {
            Vector2 previous = polygon[(outside + offset - 1) % polygon.Length];
            Vector2 current = polygon[(outside + offset) % polygon.Length];
            if (Inside(current) != Inside(previous))
            {
                float t = (boundary - previous.Y) / (current.Y - previous.Y);
                Vector2 intersection = new(Mathf.Lerp(previous.X, current.X, t), boundary);
                if (Inside(current)) chain = [intersection];
                else
                {
                    chain!.Add(intersection);
                    chains.Add(chain);
                    chain = null;
                }
            }
            if (Inside(current)) chain!.Add(current);
        }
        // Pair adjacent boundary crossings to reconnect only intervals inside the
        // source polygon. A single convex-style clipping pass would bridge islands.
        var ends = chains.SelectMany((points, index) => new[] {
            (X: points[0].X, Chain: index, Start: true),
            (X: points[^1].X, Chain: index, Start: false) }).OrderBy(end => end.X).ToArray();
        Dictionary<int, int> next = [];
        for (int i = 0; i + 1 < ends.Length; i += 2)
        {
            var a = ends[i]; var b = ends[i + 1];
            if (a.Start != b.Start) next[a.Start ? b.Chain : a.Chain] = a.Start ? a.Chain : b.Chain;
        }
        HashSet<int> visited = [];
        List<Vector2[]> result = [];
        for (int i = 0; i < chains.Count; i++)
        {
            if (visited.Contains(i)) continue;
            List<Vector2> part = [];
            int current = i;
            while (visited.Add(current))
            {
                part.AddRange(chains[current]);
                if (!next.TryGetValue(current, out current)) break;
            }
            if (part.Count >= 3 && Area(part.ToArray()) > .01f) result.Add(part.ToArray());
        }
        return result;
    }

    private static float Area(Vector2[] polygon)
    {
        float twice = 0;
        for (int i = 0; i < polygon.Length; i++) twice += polygon[i].Cross(polygon[(i + 1) % polygon.Length]);
        return Mathf.Abs(twice) * .5f;
    }
}
