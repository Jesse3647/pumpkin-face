using System.Text.Json.Serialization;

namespace PumpkinFace.Display.Persistence;

/// <summary>
/// Versioned on-disk state for the operator application. Keep this model free
/// of Godot objects so it can be serialized and tested without the engine.
/// </summary>
public sealed record ApplicationStateDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Guid SelectedProfileId { get; init; }

    public int? LastDisplayIndex { get; init; }

    /// <summary>
    /// A best-effort display label used to warn when display ordering changed.
    /// The index remains the authoritative selection.
    /// </summary>
    public string? LastDisplayName { get; init; }

    public bool AutoplayEnabled { get; init; } = true;

    public CalibrationProfile[] Profiles { get; init; } = [];

    [JsonIgnore]
    public CalibrationProfile SelectedProfile =>
        Profiles.First(profile => profile.Id == SelectedProfileId);
}
