using System.Text.Json;
using PumpkinFace.Core;

namespace PumpkinFace.Core.Tests;

public sealed class ProjectionCalibrationTests
{
    [Fact]
    public void DefaultCalibration_IsValidAndIdentityLike()
    {
        var calibration = ProjectionCalibration.Default;

        Assert.True(calibration.IsValid(out var error), error);
        Assert.Same(calibration, calibration.Validate());
        Assert.Equal(0f, calibration.OffsetX);
        Assert.Equal(0f, calibration.OffsetY);
        Assert.Equal(1f, calibration.ScaleX);
        Assert.Equal(1f, calibration.ScaleY);
        Assert.Equal(1f, calibration.EyeSpacing);
        Assert.Equal(1f, calibration.MouthScaleX);
        Assert.Equal(1f, calibration.MouthScaleY);
        Assert.Equal(1f, calibration.Brightness);
        Assert.Equal(1f, calibration.Gamma);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-0.1f)]
    [InlineData(2.1f)]
    public void InvalidBrightness_IsRejected(float brightness)
    {
        var calibration = ProjectionCalibration.Default with { Brightness = brightness };

        Assert.False(calibration.IsValid(out var error));
        Assert.Contains(nameof(ProjectionCalibration.Brightness), error, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => calibration.Validate());
    }

    [Fact]
    public void Normalize_RepairsNonFiniteValuesAndClampsAllRanges()
    {
        var normalized = new ProjectionCalibration
        {
            OffsetX = -4f,
            OffsetY = 4f,
            ScaleX = 0f,
            ScaleY = 9f,
            RotationDegrees = 720f,
            EyeSpacing = float.NaN,
            MouthOffsetX = float.NegativeInfinity,
            MouthOffsetY = 5f,
            MouthScaleX = -1f,
            MouthScaleY = 4f,
            Brightness = 5f,
            Gamma = 0f,
        }.Normalize();

        Assert.Equal(ProjectionCalibration.MinimumOffset, normalized.OffsetX);
        Assert.Equal(ProjectionCalibration.MaximumOffset, normalized.OffsetY);
        Assert.Equal(ProjectionCalibration.MinimumScale, normalized.ScaleX);
        Assert.Equal(ProjectionCalibration.MaximumScale, normalized.ScaleY);
        Assert.Equal(ProjectionCalibration.MaximumRotationDegrees, normalized.RotationDegrees);
        Assert.Equal(ProjectionCalibration.Default.EyeSpacing, normalized.EyeSpacing);
        Assert.Equal(ProjectionCalibration.Default.MouthOffsetX, normalized.MouthOffsetX);
        Assert.Equal(ProjectionCalibration.MaximumOffset, normalized.MouthOffsetY);
        Assert.Equal(ProjectionCalibration.MinimumScale, normalized.MouthScaleX);
        Assert.Equal(ProjectionCalibration.MaximumScale, normalized.MouthScaleY);
        Assert.Equal(ProjectionCalibration.MaximumBrightness, normalized.Brightness);
        Assert.Equal(ProjectionCalibration.MinimumGamma, normalized.Gamma);
        Assert.True(normalized.IsValid(out var error), error);
        Assert.Equal(normalized, normalized.Clamp());
    }

    [Fact]
    public void Calibration_RoundTripsThroughJson()
    {
        var expected = ProjectionCalibration.Default with
        {
            OffsetX = 0.25f,
            OffsetY = -0.4f,
            ScaleX = 1.2f,
            ScaleY = 0.8f,
            RotationDegrees = 11f,
            EyeSpacing = 1.15f,
            MouthOffsetY = 0.2f,
            MouthScaleX = 1.3f,
            Brightness = 1.4f,
            Gamma = 1.1f,
        };

        var json = JsonSerializer.Serialize(expected);
        var actual = JsonSerializer.Deserialize<ProjectionCalibration>(json);

        Assert.Equal(expected, actual);
        Assert.True(actual!.IsValid(out var error), error);
    }
}
