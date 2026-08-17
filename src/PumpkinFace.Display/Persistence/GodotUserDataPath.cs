using Godot;

namespace PumpkinFace.Display.Persistence;

/// <summary>
/// Resolves storage beneath Godot's per-user application data directory.
/// Resolve once on the main thread, then ordinary System.IO can safely perform
/// the small atomic writes on a worker thread.
/// </summary>
public static class GodotUserDataPath
{
    public const string StoreDirectory = "user://pumpkin-face";

    public static string ResolveStoreDirectory() =>
        ProjectSettings.GlobalizePath(StoreDirectory);
}
