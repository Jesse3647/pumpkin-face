using Godot;
using PumpkinFace.Display.Rendering;

namespace PumpkinFace.Display.Tests;

public sealed class EyeContourDeformerTests
{
    private static IEnumerable<(Vector2[] Contour, Vector2 Pupil, Vector2 Catchlight)> Eyes()
    {
        foreach (var face in new[] { ReferenceFaceContours.Happy, ReferenceFaceContours.Sad, ReferenceFaceContours.Frightened,
            PipFaceContours.Happy, PipFaceContours.Sad, PipFaceContours.Frightened })
        {
            yield return (face.LeftEye, face.LeftPupil, face.LeftCatchlight);
            yield return (face.RightEye, face.RightPupil, face.RightCatchlight);
        }
    }

    [Fact]
    public void OpenEyesPreserveTheAuthoredOutline()
    {
        foreach (var eye in Eyes())
        {
            EyeGeometry result = EyeContourDeformer.Build(eye.Contour, eye.Pupil, eye.Catchlight, Vector2.One, 1, 0);
            for (int i = 0; i < eye.Contour.Length; i++) Assert.True(eye.Contour[i].DistanceTo(result.Aperture[i]) < .0001);
            Assert.True(eye.Pupil.DistanceTo(result.Pupil) < .0001);
        }
    }

    [Fact]
    public void BlinkingAndLidFollowDoNotMovePupilOrCatchlightAnchors()
    {
        foreach (var eye in Eyes())
        foreach (float intensity in new[] { .25f, .65f, 1f })
        {
            Vector2 scale = new(Mathf.Lerp(.92f, 1, intensity), Mathf.Lerp(.72f, 1, intensity));
            EyeGeometry rest = EyeContourDeformer.Build(eye.Contour, eye.Pupil, eye.Catchlight, scale, 1, 0);
            foreach (float gaze in new[] { -1f, 0, 1f })
            for (int phase = 0; phase <= 20; phase++)
            {
                EyeGeometry result = EyeContourDeformer.Build(eye.Contour, eye.Pupil, eye.Catchlight, scale, phase / 20f, gaze);
                Assert.Equal(rest.Pupil, result.Pupil);
                Assert.Equal(rest.Catchlight, result.Catchlight);
                Assert.InRange(result.Aperture.Length, 3, 96); // Shader aperture capacity.
                Assert.True(Area(result.Aperture) > .1);
                AssertSimple(result.Aperture, $"source={eye.Contour[0]}, intensity={intensity}, gaze={gaze}, open={phase / 20f}");
            }
        }
    }

    [Fact]
    public void UpperLidDoesMostOfTheClosingAndOpeningAreaShrinksMonotonically()
    {
        foreach (var eye in Eyes())
        {
            float previousArea = Area(eye.Contour);
            for (int phase = 1; phase <= 20; phase++)
            {
                EyeGeometry result = EyeContourDeformer.Build(eye.Contour, eye.Pupil, eye.Catchlight, Vector2.One, 1 - phase / 20f, 0);
                float area = Area(result.Aperture);
                Assert.True(area < previousArea + .1f);
                previousArea = area;
            }
            EyeGeometry closed = EyeContourDeformer.Build(eye.Contour, eye.Pupil, eye.Catchlight, Vector2.One, 0, 0);
            float upperTravel = closed.Aperture.Min(p => p.Y) - eye.Contour.Min(p => p.Y);
            float lowerTravel = eye.Contour.Max(p => p.Y) - closed.Aperture.Max(p => p.Y);
            Assert.True(upperTravel > lowerTravel * 2);
            Assert.True(Area(closed.Aperture) < Area(eye.Contour) * .02f);
        }
    }

    [Fact]
    public void VerticalGazeMovesTheLidsInTheSameDirectionWithStrongerUpperFollow()
    {
        foreach (var eye in Eyes())
        {
            EyeGeometry up = EyeContourDeformer.Build(eye.Contour, eye.Pupil, eye.Catchlight, Vector2.One, 1, -1);
            EyeGeometry down = EyeContourDeformer.Build(eye.Contour, eye.Pupil, eye.Catchlight, Vector2.One, 1, 1);
            Assert.True(up.Aperture.Min(p => p.Y) < eye.Contour.Min(p => p.Y));
            Assert.True(down.Aperture.Min(p => p.Y) > eye.Contour.Min(p => p.Y));
            float topTravel = down.Aperture.Min(p => p.Y) - up.Aperture.Min(p => p.Y);
            float bottomTravel = down.Aperture.Max(p => p.Y) - up.Aperture.Max(p => p.Y);
            Assert.True(topTravel > bottomTravel * 2);
        }
    }

    private static float Area(Vector2[] polygon)
    {
        float twice = 0;
        for (int i = 0; i < polygon.Length; i++) twice += polygon[i].Cross(polygon[(i + 1) % polygon.Length]);
        return Mathf.Abs(twice) * .5f;
    }

    private static void AssertSimple(Vector2[] polygon, string context)
    {
        static float Side(Vector2 p, Vector2 q, Vector2 r) => (q - p).Cross(r - p);
        for (int i = 0; i < polygon.Length; i++)
        for (int j = i + 2; j < polygon.Length; j++)
        {
            if (i == 0 && j == polygon.Length - 1) continue;
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length], c = polygon[j], d = polygon[(j + 1) % polygon.Length];
            Assert.False(Side(a, b, c) * Side(a, b, d) < -.01f && Side(c, d, a) * Side(c, d, b) < -.01f,
                $"{context}: edges {i}/{j}: {a}->{b} crosses {c}->{d}");
        }
    }
}
