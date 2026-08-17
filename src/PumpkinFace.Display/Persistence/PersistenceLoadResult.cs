namespace PumpkinFace.Display.Persistence;

public enum PersistenceLoadSource
{
    Primary,
    MigratedPrimary,
    RecoveryBackup,
    Defaults,
}

/// <summary>
/// Describes what was loaded so the operator UI can surface recovery warnings
/// without coupling persistence to a particular notification control.
/// </summary>
public sealed record PersistenceLoadResult(
    ApplicationStateDocument State,
    PersistenceLoadSource Source,
    string? Warning = null,
    string? ArchivedInvalidFile = null);

public sealed class PersistenceFailureEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
