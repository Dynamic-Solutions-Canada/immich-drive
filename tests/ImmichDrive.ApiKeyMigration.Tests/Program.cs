using System.Text;
using System.Text.Json;
using ImmichDrive.Models;
using ImmichDrive.Services;
using WinUIAppKit.Settings;

namespace ImmichDrive.ApiKeyMigration.Tests;

/// <summary>
/// Checks the one-time move of the Immich API key from settings.json into the protected store
/// (every combination of key present or absent in each place, plus store and settings write
/// failures) and ApiKeyStore's rule that a key is never dropped while the store is failing. Each case runs in fresh temporary folders. Exits nonzero on any failure.
/// </summary>
internal static class Program
{
    private const string Key = "test-key-0123456789";
    private const string OtherKey = "older-key-in-store";

    private static int _failures;

    private static int Main()
    {
        Run("no settings file and no store: nothing to migrate", NoSettingsNoStore);
        Run("key in settings, no store: migrated and stripped", KeyInSettingsNoStore);
        Run("no key in settings, key in store: already migrated", KeyInStoreOnly);
        Run("key in both: settings wins and replaces the store", KeyInBoth);
        Run("empty key field in settings: treated as no key", EmptyKeyField);
        Run("unparseable settings: left untouched", UnparseableSettings);
        Run("store write throws: settings untouched, key kept", StoreWriteThrows);
        Run("store reads back a different value: settings untouched", StoreReadBackMismatch);
        Run("settings rewrite fails: key safe in store, no temp files", SettingsRewriteFails);
        Run("store exists but will not decrypt: reported, not deleted", StoreUnreadable);
        Run("running twice is a no-op the second time", Idempotent);
        Run("pending key, same key, store still failing: keeps carrying it", PendingSameKeyStillFailing);
        Run("pending key, new key, store still failing: carries the new key", PendingNewKeyStillFailing);
        Run("pending key, store recovers: pending cleared", PendingStoreRecovers);
        Run("no pending key, store failing: not carried, reported", NoPendingStoreFailing);
        Run("empty key: store and pending state cleared", EmptyKeyClears);
        Run("unreadable store, new key saved: unreadable flag cleared", UnreadableThenNewKey);
        Run("settings store keeps the key while the protected store refuses it", SettingsStoreCarriesPendingKey);
        Run("settings store drops the key once it is safely stored", SettingsStoreDropsKeyAfterSave);
        Run("version 1 migration moves window placement and keeps the key", MigrationToVersion1);

        Console.WriteLine(_failures == 0 ? "ApiKeyMigration: PASS" : $"ApiKeyMigration: FAIL ({_failures})");
        return _failures == 0 ? 0 : 1;
    }

    // ── Cases ────────────────────────────────────────────────────────

    private static void NoSettingsNoStore(Sandbox box)
    {
        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);
        Check(result.Outcome == ApiKeyMigrationOutcome.NoKey, $"outcome {result.Outcome}");
        Check(result.ApiKey == "", "no key returned");
        Check(!File.Exists(box.SettingsPath), "settings.json not created");
        Check(!box.StoreFileExists, "store not created");
    }

    private static void KeyInSettingsNoStore(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(Key));

        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(result.Outcome == ApiKeyMigrationOutcome.Migrated, $"outcome {result.Outcome}");
        Check(result.ApiKey == Key, "key returned for this session");
        Check(box.StoredKey() == Key, "store holds the key");
        Check(!box.SettingsHasKeyField(), "settings.json no longer has the key field");
        Check(!File.ReadAllText(box.SettingsPath).Contains(Key), "key text absent from settings.json");
        Check(box.SettingsString("ServerUrl") == "https://photos.example.com", "other fields kept");
        Check(box.SettingsInt("LayoutVersion") == 3, "numeric fields kept");
        Check(box.LeftoverFiles().Length == 0, $"no backup or temp files: {string.Join(", ", box.LeftoverFiles())}");
    }

    private static void KeyInStoreOnly(Sandbox box)
    {
        box.WriteSettings(SettingsWithoutKey());
        box.SeedStore(Key);
        string before = File.ReadAllText(box.SettingsPath);

        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(result.Outcome == ApiKeyMigrationOutcome.AlreadyMigrated, $"outcome {result.Outcome}");
        Check(result.ApiKey == Key, "stored key returned");
        Check(File.ReadAllText(box.SettingsPath) == before, "settings.json not rewritten");
    }

    private static void KeyInBoth(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(Key));
        box.SeedStore(OtherKey);

        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(result.Outcome == ApiKeyMigrationOutcome.Migrated, $"outcome {result.Outcome}");
        Check(result.ApiKey == Key, "settings key wins");
        Check(box.StoredKey() == Key, "store replaced with the settings key");
        Check(!box.SettingsHasKeyField(), "settings.json stripped");
    }

    private static void EmptyKeyField(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(""));

        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(result.Outcome == ApiKeyMigrationOutcome.NoKey, $"outcome {result.Outcome}");
        Check(!box.StoreFileExists, "no empty key stored");
    }

    private static void UnparseableSettings(Sandbox box)
    {
        const string broken = "{ \"ApiKey\": \"" + Key + "\", ";
        box.WriteSettings(broken);

        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(result.Outcome == ApiKeyMigrationOutcome.NoKey, $"outcome {result.Outcome}");
        Check(File.ReadAllText(box.SettingsPath) == broken, "broken file left as it was");
    }

    private static void StoreWriteThrows(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(Key));
        string before = File.ReadAllText(box.SettingsPath);
        box.Protector.FailProtect = true;

        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(result.Outcome == ApiKeyMigrationOutcome.StoreWriteFailed, $"outcome {result.Outcome}");
        Check(result.ApiKey == Key, "key still returned, so the drive stays connected");
        Check(File.ReadAllText(box.SettingsPath) == before, "settings.json byte-for-byte unchanged");
        Check(box.LoggedErrors > 0, "failure reported to the log hook");
    }

    private static void StoreReadBackMismatch(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(Key));
        string before = File.ReadAllText(box.SettingsPath);
        box.Protector.CorruptOnUnprotect = true;

        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(result.Outcome == ApiKeyMigrationOutcome.StoreWriteFailed, $"outcome {result.Outcome}");
        Check(File.ReadAllText(box.SettingsPath) == before, "settings.json unchanged");
    }

    private static void SettingsRewriteFails(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(Key));
        string before = File.ReadAllText(box.SettingsPath);
        box.MakeSettingsFolderReadOnly();
        if (box.SettingsFolderIsWritable())
        {
            Check(false, "settings folder still writable; run this harness as a non-root user");
            return;
        }

        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(result.Outcome == ApiKeyMigrationOutcome.SettingsRewriteFailed, $"outcome {result.Outcome}");
        Check(result.ApiKey == Key, "key returned");
        Check(box.StoredKey() == Key, "key safe in the store");
        Check(File.ReadAllText(box.SettingsPath) == before, "settings.json unchanged");
        Check(box.LeftoverFiles().Length == 0, "no temp files left");
    }

    private static void StoreUnreadable(Sandbox box)
    {
        box.WriteSettings(SettingsWithoutKey());
        box.SeedStore(Key);
        box.Protector.FailUnprotect = true;

        var result = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(result.Outcome == ApiKeyMigrationOutcome.StoredKeyUnreadable, $"outcome {result.Outcome}");
        Check(result.ApiKey == "", "no key returned");
        Check(box.StoreFileExists, "unreadable file kept; deleting it is the user's choice, not a read's");
    }

    private static void Idempotent(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(Key));
        ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);
        string afterFirst = File.ReadAllText(box.SettingsPath);

        var second = ImmichDrive.Services.ApiKeyMigration.Run(box.SettingsPath, box.Store);

        Check(second.Outcome == ApiKeyMigrationOutcome.AlreadyMigrated, $"outcome {second.Outcome}");
        Check(second.ApiKey == Key, "key still returned");
        Check(File.ReadAllText(box.SettingsPath) == afterFirst, "settings.json not touched again");
    }

    // ── ApiKeyStore save rules ───────────────────────────────────────

    /// <summary>Leaves ApiKeyStore in the "startup migration failed" state with <see cref="Key"/> pending.</summary>
    private static void StartWithFailedMigration(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(Key));
        box.Protector.FailProtect = true;
        ApiKeyStore.Initialize(box.SettingsPath, box.Store);
        Check(ApiKeyStore.PendingLegacyKey == Key, "setup: key pending after failed migration");
    }

    private static void PendingSameKeyStillFailing(Sandbox box)
    {
        StartWithFailedMigration(box);

        bool saved = ApiKeyStore.TrySet(Key);

        Check(!saved, "reported as not saved");
        Check(ApiKeyStore.PendingLegacyKey == Key, "settings.json keeps carrying the key");
    }

    private static void PendingNewKeyStillFailing(Sandbox box)
    {
        StartWithFailedMigration(box);

        bool saved = ApiKeyStore.TrySet(OtherKey);

        Check(!saved, "reported as not saved");
        Check(ApiKeyStore.ApiKey == OtherKey, "new key used this session");
        Check(ApiKeyStore.PendingLegacyKey == OtherKey, "new key carried, so neither key is lost");
    }

    private static void PendingStoreRecovers(Sandbox box)
    {
        StartWithFailedMigration(box);
        box.Protector.FailProtect = false;

        bool saved = ApiKeyStore.TrySet(Key);

        Check(saved, "saved");
        Check(ApiKeyStore.PendingLegacyKey is null, "no longer carried in settings.json");
        Check(box.StoredKey() == Key, "store holds the key");
    }

    private static void NoPendingStoreFailing(Sandbox box)
    {
        box.WriteSettings(SettingsWithoutKey());
        ApiKeyStore.Initialize(box.SettingsPath, box.Store);
        box.Protector.FailProtect = true;

        bool saved = ApiKeyStore.TrySet(Key);

        Check(!saved, "reported as not saved");
        Check(ApiKeyStore.ApiKey == Key, "key used this session");
        Check(ApiKeyStore.PendingLegacyKey is null, "never written to settings.json");
        Check(box.LoggedWarnings > 0, "warning logged");
    }

    private static void EmptyKeyClears(Sandbox box)
    {
        StartWithFailedMigration(box);
        box.Protector.FailProtect = false;
        box.SeedStore(Key);

        bool saved = ApiKeyStore.TrySet("");

        Check(saved, "clearing succeeds");
        Check(ApiKeyStore.ApiKey == "", "no key");
        Check(ApiKeyStore.PendingLegacyKey is null, "nothing carried");
        Check(!box.StoreFileExists, "store file removed");
    }

    private static void UnreadableThenNewKey(Sandbox box)
    {
        box.WriteSettings(SettingsWithoutKey());
        box.SeedStore(Key);
        box.Protector.FailUnprotect = true;
        ApiKeyStore.Initialize(box.SettingsPath, box.Store);
        Check(ApiKeyStore.StoredKeyUnreadable, "setup: unreadable");
        box.Protector.FailUnprotect = false;

        bool saved = ApiKeyStore.TrySet(OtherKey);

        Check(saved, "saved");
        Check(!ApiKeyStore.StoredKeyUnreadable, "flag cleared");
        Check(box.StoredKey() == OtherKey, "store replaced");
    }

    // ── The kit's settings store around the key (SettingsManager's sequence) ──

    /// <summary>
    /// Mirrors the fields of UserSettings this sequence touches: the server URL and the nullable legacy
    /// key field. SettingsManager itself needs WinUI, so the harness drives the same store and steps.
    /// </summary>
    internal sealed class MiniSettings
    {
        public string ServerUrl { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName(StoredApiKey.LegacySettingsField)]
        public string? PendingLegacyApiKey { get; set; }
    }

    /// <summary>The startup order: key migration on the raw file, then the store load, then the reconcile save.</summary>
    private static MiniSettings LaunchLikeTheApp(Sandbox box)
    {
        ApiKeyStore.Initialize(box.SettingsPath, box.Store);
        var store = new JsonSettingsStore<MiniSettings>(box.SettingsPath, ImmichDrive.Classes.Settings.SettingsMigrations.Steps);
        var settings = store.Load().Settings;
        if (settings.PendingLegacyApiKey != ApiKeyStore.PendingLegacyKey)
        {
            settings.PendingLegacyApiKey = ApiKeyStore.PendingLegacyKey;
            store.Save(settings);
        }

        return settings;
    }

    private static void SettingsStoreCarriesPendingKey(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(Key));
        box.Protector.FailProtect = true;

        LaunchLikeTheApp(box);

        Check(ImmichDrive.Services.ApiKeyMigration.ReadLegacyKey(box.SettingsPath) == Key, "key still in settings.json after the store rewrote it");
        Check(box.SettingsInt("SchemaVersion") == 1, "file upgraded to version 1");

        // Next launch, the store works again: the key moves and leaves settings.json.
        box.Protector.FailProtect = false;
        LaunchLikeTheApp(box);
        Check(box.StoredKey() == Key, "key in the store on the next launch");
        Check(!box.SettingsHasKeyField(), "key gone from settings.json");
    }

    private static void SettingsStoreDropsKeyAfterSave(Sandbox box)
    {
        box.WriteSettings(SettingsWithKey(Key));
        box.Protector.FailProtect = true;
        LaunchLikeTheApp(box);

        // The user saves the key while the store works again; the next settings save drops the field.
        box.Protector.FailProtect = false;
        Check(ApiKeyStore.TrySet(Key), "saved");
        var store = new JsonSettingsStore<MiniSettings>(box.SettingsPath, ImmichDrive.Classes.Settings.SettingsMigrations.Steps);
        var settings = store.Load().Settings;
        settings.PendingLegacyApiKey = ApiKeyStore.PendingLegacyKey;
        store.Save(settings);

        Check(!box.SettingsHasKeyField(), "key gone from settings.json");
        Check(box.SettingsString("ServerUrl") == "https://photos.example.com", "other fields kept");
    }

    private static void MigrationToVersion1(Sandbox box)
    {
        box.WriteSettings("""{"ServerUrl":"https://photos.example.com","ApiKey":"k","Startup":false,"SettingsWindowX":10,"SettingsWindowY":20,"SettingsWindowWidth":800,"SettingsWindowHeight":600}""");
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(box.SettingsPath))!.AsObject();

        ImmichDrive.Classes.Settings.SettingsMigrations.Steps[0](root);

        Check(root["SettingsWindowPlacement"]?["Width"]?.GetValue<int>() == 800, "placement width");
        Check(root["SettingsWindowPlacement"]?["Y"]?.GetValue<int>() == 20, "placement y");
        Check(!root.ContainsKey("SettingsWindowX") && !root.ContainsKey("Startup"), "old fields removed");
        Check(root["ApiKey"]?.GetValue<string>() == "k", "legacy key kept for the key migration");
    }

    // ── Fixtures ─────────────────────────────────────────────────────

    private static string SettingsWithKey(string key) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["ServerUrl"] = "https://photos.example.com",
        [StoredApiKey.LegacySettingsField] = key,
        ["Connected"] = true,
        ["LayoutVersion"] = 3,
    });

    private static string SettingsWithoutKey() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["ServerUrl"] = "https://photos.example.com",
        ["Connected"] = true,
        ["LayoutVersion"] = 3,
    });

    // ── Runner ───────────────────────────────────────────────────────

    private static string _currentCase = "";

    private static void Run(string name, Action<Sandbox> test)
    {
        _currentCase = name;
        int failuresBefore = _failures;
        using var box = new Sandbox();
        try
        {
            test(box);
            if (_failures == failuresBefore)
            {
                Console.WriteLine($"  ok    {name}");
            }
        }
        catch (Exception ex)
        {
            Check(false, $"threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Check(bool condition, string what)
    {
        if (condition)
        {
            return;
        }

        _failures++;
        Console.Error.WriteLine($"  FAIL  {_currentCase}: {what}");
    }
}

/// <summary>
/// Fresh temporary folders for one case, with the protected store pointed at its own folder (kept
/// separate from settings.json so the settings folder can be made read-only on its own).
/// </summary>
internal sealed class Sandbox : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"immichdrive-apikey-{Guid.NewGuid():N}");
    private readonly string _settingsDir;
    private readonly string _storeDir;

    public FakeProtector Protector { get; } = new();

    /// <summary>The protected store under test, created by the constructor.</summary>
    public ProtectedStore Store { get; }

    public int LoggedErrors { get; private set; }

    public int LoggedWarnings { get; private set; }

    public Sandbox()
    {
        _settingsDir = Path.Combine(_root, "settings");
        _storeDir = Path.Combine(_root, "store");
        Directory.CreateDirectory(_settingsDir);
        Directory.CreateDirectory(_storeDir);

        // The app's own store settings (file name and entropy), over a temporary folder and a fake
        // protector, so the harness exercises the real CredentialStore and the kit's ProtectedStore.
        Store = CredentialStore.Create(() => _storeDir, Protector, (_, _) => LoggedErrors++);
        ApiKeyStore.LogInfo = null;
        ApiKeyStore.LogWarning = _ => LoggedWarnings++;
    }

    public string SettingsPath => Path.Combine(_settingsDir, "settings.json");

    public bool StoreFileExists => Store.Exists(StoredApiKey.StoreName);

    public void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

    /// <summary>Puts a key in the store directly, as an earlier successful migration would have.</summary>
    public void SeedStore(string key)
    {
        bool failUnprotect = Protector.FailUnprotect;
        Protector.FailUnprotect = false;
        Store.Save(StoredApiKey.StoreName, new StoredApiKey { ApiKey = key });
        Protector.FailUnprotect = failUnprotect;
    }

    public string? StoredKey() => Store.Load<StoredApiKey>(StoredApiKey.StoreName)?.ApiKey;

    public bool SettingsHasKeyField()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        return doc.RootElement.TryGetProperty(StoredApiKey.LegacySettingsField, out _);
    }

    public string? SettingsString(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        return doc.RootElement.GetProperty(name).GetString();
    }

    public int SettingsInt(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        return doc.RootElement.GetProperty(name).GetInt32();
    }

    /// <summary>Anything in either folder other than settings.json and the one protected file.</summary>
    public string[] LeftoverFiles()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "settings.json",
            $"protected-{StoredApiKey.StoreName}.dat",
        };

        return Directory.GetFiles(_settingsDir)
            .Concat(Directory.GetFiles(_storeDir))
            .Select(Path.GetFileName)
            .Where(name => !expected.Contains(name!))
            .ToArray()!;
    }

    public void MakeSettingsFolderReadOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_settingsDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Probe, because root ignores folder permissions and would make the case meaningless.</summary>
    public bool SettingsFolderIsWritable()
    {
        string probe = Path.Combine(_settingsDir, ".probe");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_settingsDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Temporary folder; leaving it behind does not affect other cases.
        }
    }
}

/// <summary>Reversible stand-in for DPAPI with switches for each failure the migration must survive.</summary>
internal sealed class FakeProtector : IDataProtector
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("FAKE");

    public bool FailProtect { get; set; }

    public bool FailUnprotect { get; set; }

    public bool CorruptOnUnprotect { get; set; }

    public byte[] Protect(byte[] plain)
    {
        if (FailProtect)
        {
            throw new InvalidOperationException("simulated protect failure");
        }

        return Marker.Concat(plain.Select(b => (byte)(b ^ 0x5A))).ToArray();
    }

    public byte[] Unprotect(byte[] cipher)
    {
        if (FailUnprotect)
        {
            throw new InvalidOperationException("simulated unprotect failure");
        }

        byte[] plain = cipher.Skip(Marker.Length).Select(b => (byte)(b ^ 0x5A)).ToArray();
        if (CorruptOnUnprotect)
        {
            return Encoding.UTF8.GetBytes("{\"ApiKey\":\"not-what-was-written\"}");
        }

        return plain;
    }
}
