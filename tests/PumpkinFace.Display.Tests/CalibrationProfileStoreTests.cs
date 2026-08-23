using System.Text.Json.Nodes;
using PumpkinFace.Core;
using PumpkinFace.Display.Persistence;

namespace PumpkinFace.Display.Tests;

public sealed class CalibrationProfileStoreTests
{
    private static readonly TimeSpan DisabledDebounce = TimeSpan.FromHours(1);

    [Fact]
    public async Task State_RoundTripsCalibrationAndApplicationSettings()
    {
        using var directory = new TemporaryDirectory();
        Guid profileId;

        await using (var store = CreateStore(directory.Path))
        {
            var initial = store.Load();
            Assert.Equal(PersistenceLoadSource.Defaults, initial.Source);

            var calibration = ProjectionCalibration.Default with
            {
                OffsetX = 0.14f,
                OffsetY = -0.22f,
                ScaleX = 1.31f,
                ScaleY = 0.82f,
                RotationDegrees = 7.5f,
                EyeSpacing = 1.18f,
                MouthOffsetX = -0.08f,
                MouthOffsetY = 0.17f,
                MouthScaleX = 1.25f,
                MouthScaleY = 0.76f,
                Brightness = 1.42f,
                Gamma = 0.91f,
                CandleBrightness = 1.75f,
                ShellThickness = 1.35f,
            };

            var profile = store.CreateProfile("Front porch", calibration);
            profileId = profile.Id;
            store.RememberDisplay(2, "Living room projector");
            store.SetAutoplayEnabled(false);
            await store.FlushAsync();
        }

        await using (var reloaded = CreateStore(directory.Path))
        {
            var result = reloaded.Load();

            Assert.Equal(PersistenceLoadSource.Primary, result.Source);
            Assert.Equal(profileId, reloaded.State.SelectedProfileId);
            Assert.Equal(2, reloaded.State.LastDisplayIndex);
            Assert.Equal("Living room projector", reloaded.State.LastDisplayName);
            Assert.False(reloaded.State.AutoplayEnabled);

            var profile = Assert.Single(
                reloaded.State.Profiles,
                candidate => candidate.Id == profileId);
            Assert.Equal("Front porch", profile.Name);
            Assert.Equal(0.14f, profile.Calibration.OffsetX);
            Assert.Equal(-0.22f, profile.Calibration.OffsetY);
            Assert.Equal(1.31f, profile.Calibration.ScaleX);
            Assert.Equal(0.82f, profile.Calibration.ScaleY);
            Assert.Equal(7.5f, profile.Calibration.RotationDegrees);
            Assert.Equal(1.18f, profile.Calibration.EyeSpacing);
            Assert.Equal(-0.08f, profile.Calibration.MouthOffsetX);
            Assert.Equal(0.17f, profile.Calibration.MouthOffsetY);
            Assert.Equal(1.25f, profile.Calibration.MouthScaleX);
            Assert.Equal(0.76f, profile.Calibration.MouthScaleY);
            Assert.Equal(1.42f, profile.Calibration.Brightness);
            Assert.Equal(0.91f, profile.Calibration.Gamma);
            Assert.Equal(1.75f, profile.Calibration.CandleBrightness);
            Assert.Equal(1.35f, profile.Calibration.ShellThickness);
            Assert.Equal(
                ApplicationStateDocument.CurrentSchemaVersion,
                reloaded.State.SchemaVersion);
            Assert.Equal(CalibrationProfile.CurrentSchemaVersion, profile.SchemaVersion);
        }
    }

    [Fact]
    public async Task ProfileOperations_MaintainNamesSelectionAndFinalProfileInvariant()
    {
        using var directory = new TemporaryDirectory();
        await using var store = CreateStore(directory.Path);
        store.Load();
        var defaultProfile = store.State.SelectedProfile;

        var wall = store.CreateProfile(
            "Wall",
            ProjectionCalibration.Default with { Brightness = 99f });
        Assert.Equal(wall.Id, store.State.SelectedProfileId);
        Assert.Equal(ProjectionCalibration.MaximumBrightness, wall.Calibration.Brightness);

        var duplicate = store.DuplicateProfile(wall.Id);
        Assert.Equal("Wall Copy", duplicate.Name);
        Assert.Equal(duplicate.Id, store.State.SelectedProfileId);

        var renamed = store.RenameProfile(duplicate.Id, "  Side window  ");
        Assert.Equal("Side window", renamed.Name);
        Assert.Throws<ArgumentException>(() => store.RenameProfile(wall.Id, "SIDE WINDOW"));

        store.UpdateCalibration(
            wall.Id,
            ProjectionCalibration.Default with { ScaleX = 1.8f, Gamma = 2f });
        Assert.NotEqual(ProjectionCalibration.Default, store.State.Profiles.Single(p => p.Id == wall.Id).Calibration);

        var reset = store.ResetProfile(wall.Id);
        Assert.Equal(ProjectionCalibration.Default, reset.Calibration);

        store.SelectProfile(wall.Id);
        Assert.True(store.DeleteProfile(wall.Id));
        Assert.Equal(duplicate.Id, store.State.SelectedProfileId);
        Assert.False(store.DeleteProfile(Guid.NewGuid()));

        Assert.True(store.DeleteProfile(defaultProfile.Id));
        Assert.Single(store.State.Profiles);
        Assert.Throws<InvalidOperationException>(() => store.DeleteProfile(duplicate.Id));

        await store.FlushAsync();
    }

    [Fact]
    public async Task CorruptPrimary_RestoresLastKnownGoodBackupAndArchivesBadFile()
    {
        using var directory = new TemporaryDirectory();
        Guid recoveredProfileId;

        await using (var store = CreateStore(directory.Path))
        {
            store.Load();
            var profile = store.CreateProfile("Recover me");
            recoveredProfileId = profile.Id;
            await store.FlushAsync();

            // A second complete write promotes the first user state to backup.
            store.SetAutoplayEnabled(false);
            await store.FlushAsync();
        }

        var primaryPath = System.IO.Path.Combine(
            directory.Path,
            CalibrationProfileStore.StateFileName);
        await File.WriteAllTextAsync(primaryPath, "{ definitely not valid json");

        await using var recovered = CreateStore(directory.Path);
        var result = recovered.Load();

        Assert.Equal(PersistenceLoadSource.RecoveryBackup, result.Source);
        Assert.NotNull(result.Warning);
        Assert.NotNull(result.ArchivedInvalidFile);
        Assert.True(File.Exists(result.ArchivedInvalidFile));
        Assert.Contains(recovered.State.Profiles, profile => profile.Id == recoveredProfileId);
        Assert.True(recovered.State.AutoplayEnabled);

        // Recovery immediately heals the primary document.
        await using var verification = CreateStore(directory.Path);
        Assert.Equal(PersistenceLoadSource.Primary, verification.Load().Source);
    }

    [Fact]
    public async Task InvalidPrimaryAndBackup_FallBackToPersistedSafeDefaults()
    {
        using var directory = new TemporaryDirectory();
        var primaryPath = System.IO.Path.Combine(
            directory.Path,
            CalibrationProfileStore.StateFileName);
        var backupPath = System.IO.Path.Combine(
            directory.Path,
            CalibrationProfileStore.BackupFileName);
        await File.WriteAllTextAsync(primaryPath, "[]");
        await File.WriteAllTextAsync(backupPath, "{\"schemaVersion\":999}");

        await using (var store = CreateStore(directory.Path))
        {
            var result = store.Load();

            Assert.Equal(PersistenceLoadSource.Defaults, result.Source);
            Assert.NotNull(result.Warning);
            Assert.Equal(ProjectionCalibration.Default, store.State.SelectedProfile.Calibration);
            Assert.True(store.State.AutoplayEnabled);
            Assert.Null(store.State.LastDisplayIndex);
            Assert.Single(store.State.Profiles);
        }

        await using var verification = CreateStore(directory.Path);
        Assert.Equal(PersistenceLoadSource.Primary, verification.Load().Source);
    }

    [Fact]
    public async Task LegacyDocument_MigratesNameSelectionAndKeepsOriginalAsBackup()
    {
        using var directory = new TemporaryDirectory();
        Guid legacyProfileId;

        await using (var store = CreateStore(directory.Path))
        {
            store.Load();
            legacyProfileId = store.CreateProfile("Legacy projector").Id;
            await store.FlushAsync();
        }

        var primaryPath = System.IO.Path.Combine(
            directory.Path,
            CalibrationProfileStore.StateFileName);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(primaryPath))!.AsObject();
        json["schemaVersion"] = 0;
        json.Remove("selectedProfileId");
        json["selectedProfileName"] = "Legacy projector";
        await File.WriteAllTextAsync(primaryPath, json.ToJsonString());

        await using var migrated = CreateStore(directory.Path);
        var result = migrated.Load();

        Assert.Equal(PersistenceLoadSource.MigratedPrimary, result.Source);
        Assert.Equal(legacyProfileId, migrated.State.SelectedProfileId);
        Assert.Equal(ApplicationStateDocument.CurrentSchemaVersion, migrated.State.SchemaVersion);
        Assert.All(
            migrated.State.Profiles,
            profile => Assert.Equal(CalibrationProfile.CurrentSchemaVersion, profile.SchemaVersion));

        var migratedJson = JsonNode.Parse(await File.ReadAllTextAsync(primaryPath))!.AsObject();
        Assert.Equal(
            ApplicationStateDocument.CurrentSchemaVersion,
            migratedJson["schemaVersion"]!.GetValue<int>());

        var backupPath = System.IO.Path.Combine(
            directory.Path,
            CalibrationProfileStore.BackupFileName);
        var legacyBackup = JsonNode.Parse(await File.ReadAllTextAsync(backupPath))!.AsObject();
        Assert.Equal(0, legacyBackup["schemaVersion"]!.GetValue<int>());
    }

    private static CalibrationProfileStore CreateStore(string path) =>
        new(path, DisabledDebounce);
}
