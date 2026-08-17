using PumpkinFace.Core;

namespace PumpkinFace.Display.Persistence;

/// <summary>
/// A named, durable projection calibration. IDs are used by commands and UI
/// controls so a profile can be renamed without breaking the current selection.
/// </summary>
public sealed record CalibrationProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required ProjectionCalibration Calibration { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
