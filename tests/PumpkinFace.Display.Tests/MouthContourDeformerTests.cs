using Godot;
using PumpkinFace.Display.Rendering;

namespace PumpkinFace.Display.Tests;

public sealed class MouthContourDeformerTests
{
    [Fact]
    public void RoundedAuthoredMouthsRemainSimpleAndClearTheNose()
    {
        foreach (ReferenceFaceShape face in new[] { ReferenceFaceContours.Happy, ReferenceFaceContours.Sad, ReferenceFaceContours.Frightened })
        foreach (float scaleY in new[] { .15f, .65f, 1f, 1.43f })
        {
            Vector2 center = face.Mouth.Aggregate(Vector2.Zero, (sum, p) => sum + p) / face.Mouth.Length;
            Vector2[] source = face.Mouth.Select(p => center + (p - center) * new Vector2(.8f, scaleY)).ToArray();
            float topLimit = face.Nose.Max(p => p.Y) + 24;
            for (int step = 0; step <= 20; step++)
            {
                float amount = step / 20f;
                Vector2[] result = MouthContourDeformer.Round(source, center, amount, topLimit);
                Assert.Equal(source.Length, result.Length);
                for (int i = 0; i < result.Length; i++)
                for (int j = i + 2; j < result.Length; j++)
                {
                    if (i == 0 && j == result.Length - 1) continue;
                    Assert.False(Crosses(result[i], result[(i + 1) % result.Length], result[j], result[(j + 1) % result.Length]),
                        $"Contour crosses at amount {amount}, scale {scaleY}, edges {i}/{j}");
                }
                if (step == 0) Assert.Equal(source, result);
                if (step == 20) Assert.True(result.Min(p => p.Y) >= topLimit - .01);
            }
        }
    }

    private static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static float Side(Vector2 p, Vector2 q, Vector2 r) => (q - p).Cross(r - p);
        return Side(a, b, c) * Side(a, b, d) < -.01f && Side(c, d, a) * Side(c, d, b) < -.01f;
    }
}
