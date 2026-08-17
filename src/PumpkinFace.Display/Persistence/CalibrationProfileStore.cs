using System.Text.Json;
using System.Text.Json.Serialization;
using PumpkinFace.Core;

namespace PumpkinFace.Display.Persistence;

/// <summary>
/// Owns named calibration profiles and persists operator state using debounced,
/// atomic JSON writes. Mutations are synchronous and are intended to be invoked
/// by the Godot main thread; disk writes do not touch Godot objects.
/// </summary>
public sealed class CalibrationProfileStore : IDisposable, IAsyncDisposable
{
    public const string StateFileName = "application-state.json";
    public const string BackupFileName = "application-state.backup.json";

    private const int MaximumProfileNameLength = 64;
    private const string DefaultProfileName = "Default";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
    };

    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Func<ProjectionCalibration> _defaultCalibrationFactory;
    private readonly Func<ProjectionCalibration, ProjectionCalibration> _normalizeCalibration;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _saveDebounce;
    private readonly string _statePath;
    private readonly string _backupPath;
    private readonly string _temporaryPath;

    private ApplicationStateDocument _state;
    private CancellationTokenSource? _scheduledSaveCancellation;
    private long _revision;
    private long _persistedRevision;
    private bool _disposed;

    /// <summary>
    /// Creates a store rooted in Godot's user:// directory. Resolve this on the
    /// Godot main thread because ProjectSettings is an engine API.
    /// </summary>
    public CalibrationProfileStore(
        TimeSpan? saveDebounce = null,
        TimeProvider? timeProvider = null)
        : this(
            GodotUserDataPath.ResolveStoreDirectory(),
            () => ProjectionCalibration.Default,
            calibration => calibration.Normalize(),
            saveDebounce,
            timeProvider)
    {
    }

    /// <summary>
    /// Creates a store rooted in Godot's user:// directory with an injectable
    /// calibration policy, primarily for deterministic tests.
    /// </summary>
    public CalibrationProfileStore(
        Func<ProjectionCalibration> defaultCalibrationFactory,
        Func<ProjectionCalibration, ProjectionCalibration>? normalizeCalibration = null,
        TimeSpan? saveDebounce = null,
        TimeProvider? timeProvider = null)
        : this(
            GodotUserDataPath.ResolveStoreDirectory(),
            defaultCalibrationFactory,
            normalizeCalibration,
            saveDebounce,
            timeProvider)
    {
    }

    /// <summary>
    /// Creates a store at an explicit path. This overload is useful for tests and
    /// tooling that run without starting Godot.
    /// </summary>
    public CalibrationProfileStore(
        string storageDirectory,
        TimeSpan? saveDebounce = null,
        TimeProvider? timeProvider = null)
        : this(
            storageDirectory,
            () => ProjectionCalibration.Default,
            calibration => calibration.Normalize(),
            saveDebounce,
            timeProvider)
    {
    }

    /// <summary>
    /// Creates a store at an explicit path with injectable calibration policies.
    /// </summary>
    public CalibrationProfileStore(
        string storageDirectory,
        Func<ProjectionCalibration> defaultCalibrationFactory,
        Func<ProjectionCalibration, ProjectionCalibration>? normalizeCalibration = null,
        TimeSpan? saveDebounce = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        ArgumentNullException.ThrowIfNull(defaultCalibrationFactory);

        _defaultCalibrationFactory = defaultCalibrationFactory;
        _normalizeCalibration = normalizeCalibration ?? (calibration => calibration.Normalize());
        _saveDebounce = saveDebounce ?? TimeSpan.FromMilliseconds(400);
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_saveDebounce < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(saveDebounce),
                "The save debounce interval cannot be negative.");
        }

        _statePath = Path.Combine(storageDirectory, StateFileName);
        _backupPath = Path.Combine(storageDirectory, BackupFileName);
        _temporaryPath = Path.Combine(storageDirectory, $"{StateFileName}.tmp");
        _state = CreateDefaultState();
    }

    public event EventHandler<ApplicationStateDocument>? StateChanged;

    public event EventHandler<PersistenceFailureEventArgs>? PersistenceFailed;

    public ApplicationStateDocument State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public Exception? LastSaveError { get; private set; }

    public string StatePath => _statePath;

    public string BackupPath => _backupPath;

    /// <summary>
    /// Loads primary state, then its last-known-good backup. Invalid input never
    /// escapes this method; it is archived when possible and replaced by a valid
    /// recovered/default document.
    /// </summary>
    public PersistenceLoadResult Load()
    {
        ThrowIfDisposed();

        var primary = TryReadDocument(_statePath);
        if (primary.State is not null)
        {
            SetLoadedState(primary.State);

            if (primary.WasMigrated)
            {
                PersistLoadedState(primary.State, rotateBackup: true);
                return new PersistenceLoadResult(
                    primary.State,
                    PersistenceLoadSource.MigratedPrimary,
                    "Saved settings were upgraded to the current format.");
            }

            return new PersistenceLoadResult(primary.State, PersistenceLoadSource.Primary);
        }

        var backup = TryReadDocument(_backupPath);
        if (backup.State is not null)
        {
            var archived = ArchiveInvalidPrimary();
            SetLoadedState(backup.State);
            PersistLoadedState(backup.State, rotateBackup: false);

            var reason = primary.Error is null
                ? "The main settings file was missing; the recovery backup was restored."
                : "The main settings file was invalid; the recovery backup was restored.";

            return new PersistenceLoadResult(
                backup.State,
                PersistenceLoadSource.RecoveryBackup,
                reason,
                archived);
        }

        var defaults = CreateDefaultState();
        var invalidArchive = ArchiveInvalidPrimary();
        SetLoadedState(defaults);
        PersistLoadedState(defaults, rotateBackup: false);

        var warning = primary.Error is not null || backup.Error is not null
            ? "Saved settings could not be read. Safe defaults were loaded."
            : null;

        return new PersistenceLoadResult(
            defaults,
            PersistenceLoadSource.Defaults,
            warning,
            invalidArchive);
    }

    public CalibrationProfile CreateProfile(
        string? requestedName = null,
        ProjectionCalibration? calibration = default,
        bool select = true)
    {
        CalibrationProfile? created = null;

        Mutate(state =>
        {
            var now = _timeProvider.GetUtcNow();
            var name = MakeUniqueName(
                NormalizeName(requestedName, DefaultProfileName),
                state.Profiles,
                excludedId: null);

            created = new CalibrationProfile
            {
                SchemaVersion = CalibrationProfile.CurrentSchemaVersion,
                Id = Guid.NewGuid(),
                Name = name,
                Calibration = NormalizeCalibration(calibration ?? _defaultCalibrationFactory()),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            return state with
            {
                Profiles = [.. state.Profiles, created],
                SelectedProfileId = select ? created.Id : state.SelectedProfileId,
            };
        });

        return created!;
    }

    public CalibrationProfile RenameProfile(Guid profileId, string newName)
    {
        var normalizedName = NormalizeName(newName, fallback: null);
        CalibrationProfile? renamed = null;

        Mutate(state =>
        {
            EnsureProfileExists(state, profileId);
            EnsureNameIsAvailable(normalizedName, state.Profiles, profileId);

            var now = _timeProvider.GetUtcNow();
            var profiles = state.Profiles.Select(profile =>
            {
                if (profile.Id != profileId)
                {
                    return profile;
                }

                renamed = profile with
                {
                    Name = normalizedName,
                    UpdatedAtUtc = now,
                };
                return renamed;
            }).ToArray();

            return state with { Profiles = profiles };
        });

        return renamed!;
    }

    public CalibrationProfile DuplicateProfile(
        Guid profileId,
        string? requestedName = null,
        bool select = true)
    {
        CalibrationProfile? duplicate = null;

        Mutate(state =>
        {
            var source = EnsureProfileExists(state, profileId);
            var now = _timeProvider.GetUtcNow();
            var baseName = NormalizeName(requestedName, $"{source.Name} Copy");

            duplicate = new CalibrationProfile
            {
                SchemaVersion = CalibrationProfile.CurrentSchemaVersion,
                Id = Guid.NewGuid(),
                Name = MakeUniqueName(baseName, state.Profiles, excludedId: null),
                Calibration = NormalizeCalibration(source.Calibration),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            return state with
            {
                Profiles = [.. state.Profiles, duplicate],
                SelectedProfileId = select ? duplicate.Id : state.SelectedProfileId,
            };
        });

        return duplicate!;
    }

    public bool DeleteProfile(Guid profileId)
    {
        var deleted = false;

        Mutate(state =>
        {
            var index = Array.FindIndex(state.Profiles, profile => profile.Id == profileId);
            if (index < 0)
            {
                return state;
            }

            if (state.Profiles.Length == 1)
            {
                throw new InvalidOperationException("The final calibration profile cannot be deleted.");
            }

            var profiles = state.Profiles.Where(profile => profile.Id != profileId).ToArray();
            var selectedId = state.SelectedProfileId;
            if (selectedId == profileId)
            {
                selectedId = profiles[Math.Min(index, profiles.Length - 1)].Id;
            }

            deleted = true;
            return state with
            {
                Profiles = profiles,
                SelectedProfileId = selectedId,
            };
        });

        return deleted;
    }

    public CalibrationProfile ResetProfile(Guid profileId)
    {
        return UpdateCalibration(profileId, _defaultCalibrationFactory());
    }

    public CalibrationProfile SelectProfile(Guid profileId)
    {
        CalibrationProfile? selected = null;

        Mutate(state =>
        {
            selected = EnsureProfileExists(state, profileId);
            return state.SelectedProfileId == profileId
                ? state
                : state with { SelectedProfileId = profileId };
        });

        return selected!;
    }

    public CalibrationProfile UpdateSelectedCalibration(ProjectionCalibration calibration) =>
        UpdateCalibration(State.SelectedProfileId, calibration);

    public CalibrationProfile UpdateCalibration(Guid profileId, ProjectionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        var normalized = NormalizeCalibration(calibration);
        CalibrationProfile? updated = null;

        Mutate(state =>
        {
            EnsureProfileExists(state, profileId);
            var now = _timeProvider.GetUtcNow();
            var profiles = state.Profiles.Select(profile =>
            {
                if (profile.Id != profileId)
                {
                    return profile;
                }

                updated = profile with
                {
                    Calibration = normalized,
                    UpdatedAtUtc = now,
                };
                return updated;
            }).ToArray();

            return state with { Profiles = profiles };
        });

        return updated!;
    }

    public void RememberDisplay(int? displayIndex, string? displayName = null)
    {
        if (displayIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(displayIndex),
                "Display index cannot be negative.");
        }

        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? null
            : displayName.Trim();

        Mutate(state => state with
        {
            LastDisplayIndex = displayIndex,
            LastDisplayName = normalizedDisplayName,
        });
    }

    public void SetAutoplayEnabled(bool enabled)
    {
        Mutate(state => state.AutoplayEnabled == enabled
            ? state
            : state with { AutoplayEnabled = enabled });
    }

    /// <summary>
    /// Immediately persists the newest revision. Use this during a clean shutdown
    /// or before changing scenes; ordinary slider changes use the debounce window.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        ApplicationStateDocument snapshot;
        long revision;
        CancellationTokenSource? scheduledCancellation;
        lock (_gate)
        {
            scheduledCancellation = _scheduledSaveCancellation;
            _scheduledSaveCancellation = null;
            snapshot = _state;
            revision = _revision;
        }

        scheduledCancellation?.Cancel();
        scheduledCancellation?.Dispose();

        if (revision <= Volatile.Read(ref _persistedRevision))
        {
            return;
        }

        await WriteSnapshotAsync(snapshot, revision, rotateBackup: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        FlushAsync().GetAwaiter().GetResult();
        _disposed = true;
        _writeGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await FlushAsync().ConfigureAwait(false);
        _disposed = true;
        _writeGate.Dispose();
    }

    private void Mutate(Func<ApplicationStateDocument, ApplicationStateDocument> mutation)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(mutation);

        ApplicationStateDocument? changedState = null;
        lock (_gate)
        {
            var next = mutation(_state);
            if (ReferenceEquals(next, _state))
            {
                return;
            }

            _state = ValidateAndRepair(next);
            _revision++;
            changedState = _state;
            ScheduleSaveLocked(_state, _revision);
        }

        StateChanged?.Invoke(this, changedState);
    }

    private void ScheduleSaveLocked(ApplicationStateDocument snapshot, long revision)
    {
        _scheduledSaveCancellation?.Cancel();
        _scheduledSaveCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _scheduledSaveCancellation = cancellation;
        _ = SaveAfterDebounceAsync(snapshot, revision, cancellation.Token);
    }

    private async Task SaveAfterDebounceAsync(
        ApplicationStateDocument snapshot,
        long revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_saveDebounce, _timeProvider, cancellationToken).ConfigureAwait(false);
            await WriteSnapshotAsync(snapshot, revision, rotateBackup: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer mutation or explicit flush superseded this snapshot.
        }
        catch (Exception exception)
        {
            LastSaveError = exception;
            PersistenceFailed?.Invoke(this, new PersistenceFailureEventArgs(exception));
        }
    }

    private async Task WriteSnapshotAsync(
        ApplicationStateDocument snapshot,
        long revision,
        bool rotateBackup,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (revision <= Volatile.Read(ref _persistedRevision))
            {
                return;
            }

            await Task.Run(
                    () => WriteAtomic(snapshot, rotateBackup),
                    cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(ref _persistedRevision, revision);
            LastSaveError = null;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void PersistLoadedState(ApplicationStateDocument state, bool rotateBackup)
    {
        try
        {
            WriteAtomic(state, rotateBackup);
            Volatile.Write(ref _persistedRevision, _revision);
            LastSaveError = null;
        }
        catch (Exception exception)
        {
            LastSaveError = exception;
            PersistenceFailed?.Invoke(this, new PersistenceFailureEventArgs(exception));
        }
    }

    private void WriteAtomic(ApplicationStateDocument state, bool rotateBackup)
    {
        var directory = Path.GetDirectoryName(_statePath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        using (var stream = new FileStream(
                   _temporaryPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 16 * 1024,
                   FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        try
        {
            if (!File.Exists(_statePath))
            {
                File.Move(_temporaryPath, _statePath);
                return;
            }

            if (rotateBackup)
            {
                ReplaceWithBackup();
            }
            else
            {
                File.Move(_temporaryPath, _statePath, overwrite: true);
            }
        }
        finally
        {
            TryDelete(_temporaryPath);
        }
    }

    private void ReplaceWithBackup()
    {
        try
        {
            File.Replace(
                _temporaryPath,
                _statePath,
                _backupPath,
                ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            PortableReplaceWithBackup();
        }
        catch (IOException)
        {
            // Some filesystems do not implement replace semantics. Copying the
            // previous valid state first retains recovery, then rename the temp.
            PortableReplaceWithBackup();
        }
    }

    private void PortableReplaceWithBackup()
    {
        File.Copy(_statePath, _backupPath, overwrite: true);
        File.Move(_temporaryPath, _statePath, overwrite: true);
    }

    private ReadResult TryReadDocument(string path)
    {
        if (!File.Exists(path))
        {
            return new ReadResult(null, WasMigrated: false, Error: null);
        }

        try
        {
            var json = File.ReadAllText(path);
            using var parsed = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

            var version = ReadSchemaVersion(parsed.RootElement);
            if (version > ApplicationStateDocument.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Settings schema {version} is newer than supported schema " +
                    $"{ApplicationStateDocument.CurrentSchemaVersion}.");
            }

            var document = JsonSerializer.Deserialize<ApplicationStateDocument>(
                json,
                SerializerOptions)
                ?? throw new InvalidDataException("The settings document was empty.");

            var migrated = version < ApplicationStateDocument.CurrentSchemaVersion;
            if (migrated)
            {
                document = MigrateLegacySelection(document, parsed.RootElement);
            }

            document = ValidateAndRepair(document with
            {
                SchemaVersion = ApplicationStateDocument.CurrentSchemaVersion,
            });

            return new ReadResult(document, migrated, Error: null);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidDataException or
                InvalidOperationException or
                ArgumentException)
        {
            return new ReadResult(null, WasMigrated: false, exception);
        }
    }

    private ApplicationStateDocument MigrateLegacySelection(
        ApplicationStateDocument document,
        JsonElement root)
    {
        if (document.SelectedProfileId != Guid.Empty || document.Profiles.Length == 0)
        {
            return document;
        }

        var selectedName = TryGetString(root, "selectedProfileName");
        var selected = selectedName is null
            ? document.Profiles[0]
            : document.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Name, selectedName, StringComparison.OrdinalIgnoreCase))
              ?? document.Profiles[0];

        return document with { SelectedProfileId = selected.Id };
    }

    private ApplicationStateDocument ValidateAndRepair(ApplicationStateDocument document)
    {
        var now = _timeProvider.GetUtcNow();
        var sourceProfiles = document.Profiles ?? [];
        var repaired = new List<CalibrationProfile>(sourceProfiles.Length);
        var usedIds = new HashSet<Guid>();

        foreach (var source in sourceProfiles)
        {
            if (source is null)
            {
                continue;
            }

            if (source.SchemaVersion > CalibrationProfile.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Calibration profile schema {source.SchemaVersion} is newer than supported " +
                    $"schema {CalibrationProfile.CurrentSchemaVersion}.");
            }

            var id = source.Id;
            while (id == Guid.Empty || usedIds.Contains(id))
            {
                id = Guid.NewGuid();
            }

            usedIds.Add(id);

            ProjectionCalibration calibration;
            try
            {
                calibration = NormalizeCalibration(source.Calibration);
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                calibration = NormalizeCalibration(_defaultCalibrationFactory());
            }

            var baseName = NormalizeName(source.Name, DefaultProfileName);
            var uniqueName = MakeUniqueName(baseName, [.. repaired], excludedId: null);
            var created = source.CreatedAtUtc == default ? now : source.CreatedAtUtc;
            var updated = source.UpdatedAtUtc == default ? created : source.UpdatedAtUtc;

            repaired.Add(source with
            {
                SchemaVersion = CalibrationProfile.CurrentSchemaVersion,
                Id = id,
                Name = uniqueName,
                Calibration = calibration,
                CreatedAtUtc = created,
                UpdatedAtUtc = updated,
            });
        }

        if (repaired.Count == 0)
        {
            var defaultProfile = CreateDefaultProfile(DefaultProfileName);
            repaired.Add(defaultProfile);
        }

        var selectedId = repaired.Any(profile => profile.Id == document.SelectedProfileId)
            ? document.SelectedProfileId
            : repaired[0].Id;

        return document with
        {
            SchemaVersion = ApplicationStateDocument.CurrentSchemaVersion,
            SelectedProfileId = selectedId,
            LastDisplayIndex = document.LastDisplayIndex is >= 0
                ? document.LastDisplayIndex
                : null,
            LastDisplayName = string.IsNullOrWhiteSpace(document.LastDisplayName)
                ? null
                : document.LastDisplayName.Trim(),
            Profiles = [.. repaired],
        };
    }

    private ApplicationStateDocument CreateDefaultState()
    {
        var profile = CreateDefaultProfile(DefaultProfileName);
        return new ApplicationStateDocument
        {
            SchemaVersion = ApplicationStateDocument.CurrentSchemaVersion,
            SelectedProfileId = profile.Id,
            LastDisplayIndex = null,
            LastDisplayName = null,
            AutoplayEnabled = true,
            Profiles = [profile],
        };
    }

    private CalibrationProfile CreateDefaultProfile(string name)
    {
        var now = _timeProvider.GetUtcNow();
        return new CalibrationProfile
        {
            SchemaVersion = CalibrationProfile.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            Name = name,
            Calibration = NormalizeCalibration(_defaultCalibrationFactory()),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private ProjectionCalibration NormalizeCalibration(ProjectionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        return _normalizeCalibration(calibration)
            ?? throw new InvalidOperationException("Calibration normalization returned null.");
    }

    private void SetLoadedState(ApplicationStateDocument state)
    {
        CancellationTokenSource? scheduledCancellation;
        lock (_gate)
        {
            scheduledCancellation = _scheduledSaveCancellation;
            _scheduledSaveCancellation = null;
            _state = state;
            _revision++;
            _persistedRevision = 0;
        }

        scheduledCancellation?.Cancel();
        scheduledCancellation?.Dispose();
        StateChanged?.Invoke(this, state);
    }

    private string? ArchiveInvalidPrimary()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(_statePath)!;
            var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmssfff");
            var archive = Path.Combine(directory, $"application-state.invalid-{timestamp}.json");
            File.Move(_statePath, archive, overwrite: false);
            return archive;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (!TryGetProperty(root, "schemaVersion", out var property))
        {
            // Documents written before explicit versioning are schema zero.
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var version))
        {
            return version;
        }

        throw new InvalidDataException("The settings schema version is not a valid integer.");
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryGetProperty(
        JsonElement root,
        string propertyName,
        out JsonElement property)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The settings document root must be a JSON object.");
        }

        foreach (var candidate in root.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static CalibrationProfile EnsureProfileExists(
        ApplicationStateDocument state,
        Guid profileId)
    {
        return state.Profiles.FirstOrDefault(profile => profile.Id == profileId)
            ?? throw new KeyNotFoundException($"Calibration profile '{profileId}' does not exist.");
    }

    private static string NormalizeName(string? name, string? fallback)
    {
        var cleaned = name is null
            ? null
            : string.Concat(name.Where(character => !char.IsControl(character))).Trim();
        var normalized = string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A calibration profile name is required.", nameof(name));
        }

        return normalized.Length <= MaximumProfileNameLength
            ? normalized
            : normalized[..MaximumProfileNameLength].TrimEnd();
    }

    private static void EnsureNameIsAvailable(
        string name,
        IEnumerable<CalibrationProfile> profiles,
        Guid excludedId)
    {
        if (profiles.Any(profile =>
                profile.Id != excludedId &&
                string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"A calibration profile named '{name}' already exists.",
                nameof(name));
        }
    }

    private static string MakeUniqueName(
        string requested,
        IReadOnlyCollection<CalibrationProfile> profiles,
        Guid? excludedId)
    {
        var existing = profiles
            .Where(profile => profile.Id != excludedId)
            .Select(profile => profile.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(requested))
        {
            return requested;
        }

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var suffixText = $" ({suffix})";
            var prefixLength = Math.Max(1, MaximumProfileNameLength - suffixText.Length);
            var prefix = requested.Length <= prefixLength
                ? requested
                : requested[..prefixLength].TrimEnd();
            var candidate = prefix + suffixText;
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique calibration profile name.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort cleanup; a subsequent save uses FileMode.Create.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup; preserve the original exception if there was one.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record ReadResult(
        ApplicationStateDocument? State,
        bool WasMigrated,
        Exception? Error);
}
