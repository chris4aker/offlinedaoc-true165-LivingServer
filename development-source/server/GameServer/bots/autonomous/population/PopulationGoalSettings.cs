#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DOL.GS;

// Shared with the launcher (linked into its csproj like BotGoalSettings.cs): one owner for the
// population goals file, its limits and the server's status report.
// Files live in server\state-165\. The launcher may save goals while the server runs; the server
// notices the change and reports the applied Revision back in population-status.json.
public sealed record PopulationGoalSettings(int Version, long Revision, int Albion, int Midgard, int Hibernia,
    int Level50MaxPercent, int BracketFloorPercent, string Rotation, bool HornCallsArchived)
{
    public const string FileName = "population-goals.json";
    public const int CurrentVersion = 1;

    // Limits (plan 3.6 / owner answer 3).
    public const int MaxPerRealm = 5000;
    public const int MaxTotal = 15000;
    public const int WarnPerRealm = 2000;
    public const int WarnTotal = 6000;
    public const int ConfirmPerRealm = 3000;

    // Rotation pace names, slowest first.
    public const string RotationOff = "Off", RotationSlow = "Slow", RotationNormal = "Normal", RotationLively = "Lively";
    public static readonly IReadOnlyList<string> RotationValues = new[] { RotationOff, RotationSlow, RotationNormal, RotationLively };

    public static PopulationGoalSettings Default { get; } =
        new(CurrentVersion, 0, 500, 500, 500, 35, 4, RotationNormal, true);

    [JsonIgnore] public int Total => Albion + Midgard + Hibernia;

    public int GoalFor(eRealm realm) => realm switch
    {
        eRealm.Albion => Albion,
        eRealm.Midgard => Midgard,
        eRealm.Hibernia => Hibernia,
        _ => 0,
    };

    // The realm's plain name (eRealm.ToString() can return "_FirstPlayerRealm" for Albion).
    public static string RealmName(eRealm realm) => realm switch
    {
        eRealm.Albion => "Albion",
        eRealm.Midgard => "Midgard",
        eRealm.Hibernia => "Hibernia",
        _ => ((byte)realm).ToString(),
    };

    public PopulationGoalSettings WithGoal(eRealm realm, int goal) => realm switch
    {
        eRealm.Albion => this with { Albion = goal },
        eRealm.Midgard => this with { Midgard = goal },
        eRealm.Hibernia => this with { Hibernia = goal },
        _ => this,
    };

    // A new revision that is always higher than the old one (time based, so a deleted and
    // re-created file still moves forward).
    public PopulationGoalSettings WithNewRevision() =>
        this with { Revision = Math.Max(Revision + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) };

    // Rotation as a 0-3 step (Off, Slow, Normal, Lively). Unknown text counts as Normal.
    [JsonIgnore] public int RotationStep
    {
        get
        {
            for (int i = 0; i < RotationValues.Count; i++)
                if (string.Equals(RotationValues[i], Rotation, StringComparison.OrdinalIgnoreCase)) return i;
            return 2;
        }
    }

    // Null when valid, otherwise a plain-English reason.
    public string? Validate()
    {
        if (Version != CurrentVersion) return "Unsupported population goals version " + Version + ".";
        if (Revision < 0) return "Revision cannot be negative.";
        foreach (var (name, goal) in new[] { ("Albion", Albion), ("Midgard", Midgard), ("Hibernia", Hibernia) })
            if (goal < 0 || goal > MaxPerRealm) return name + " goal must be 0-" + MaxPerRealm.ToString("N0") + ".";
        if (Total > MaxTotal) return "All realms together may have at most " + MaxTotal.ToString("N0") + " bots.";
        if (Level50MaxPercent < 0 || Level50MaxPercent > 100) return "The level-50 cap must be 0-100%.";
        if (BracketFloorPercent < 0 || BracketFloorPercent > 20) return "The bracket floor must be 0-20%.";
        // Five lower brackets keep their floor, so floors plus the level-50 cap must fit in 100%.
        if (5 * BracketFloorPercent + Level50MaxPercent > 100)
            return "Five bracket floors plus the level-50 cap must not exceed 100% (5 x " + BracketFloorPercent
                + "% + " + Level50MaxPercent + "% = " + (5 * BracketFloorPercent + Level50MaxPercent) + "%).";
        bool knownRotation = false;
        foreach (var value in RotationValues)
            knownRotation |= string.Equals(value, Rotation, StringComparison.OrdinalIgnoreCase);
        if (!knownRotation) return "Rotation must be Off, Slow, Normal or Lively.";
        return null;
    }

    // Soft warnings (still valid): above 2,000 a realm or 6,000 in total.
    public IReadOnlyList<string> Warnings()
    {
        var list = new List<string>();
        foreach (var (name, goal) in new[] { ("Albion", Albion), ("Midgard", Midgard), ("Hibernia", Hibernia) })
            if (goal > WarnPerRealm) list.Add(name + " goal " + goal.ToString("N0") + " is above the tested " + WarnPerRealm.ToString("N0") + " a realm.");
        if (Total > WarnTotal) list.Add("Total " + Total.ToString("N0") + " is above the tested " + WarnTotal.ToString("N0") + " bots.");
        return list;
    }

    // True when a realm goal is above 3,000 and the user should confirm it.
    public bool NeedsConfirmation() => Albion > ConfirmPerRealm || Midgard > ConfirmPerRealm || Hibernia > ConfirmPerRealm;

    // About 0.2-0.35 MB per live bot (plan section 7).
    public static string MemoryEstimate(int liveBots)
    {
        double low = liveBots * 0.20 / 1024.0, high = liveBots * 0.35 / 1024.0;
        return low < 1 ? $"about {liveBots * 0.20:0}-{liveBots * 0.35:0} MB" : $"about {low:0.0}-{high:0.0} GB";
    }

    public static string PathIn(string stateDir) => Path.Combine(stateDir, FileName);
    public static bool Exists(string stateDir) => File.Exists(PathIn(stateDir));

    // Missing file: returns true with Default (error null). Bad or unreadable file: returns false with
    // Default and the reason in error; the server should then keep its last good settings.
    public static bool TryLoad(string stateDir, out PopulationGoalSettings settings, out string? error)
    {
        settings = Default;
        error = null;
        string path = PathIn(stateDir);
        if (!File.Exists(path)) return true;
        try
        {
            string text = PopulationFiles.ReadShared(path);
            var loaded = JsonSerializer.Deserialize<PopulationGoalSettings>(text, PopulationFiles.Json);
            if (loaded == null) { error = FileName + " is empty."; return false; }
            loaded = loaded with { Rotation = loaded.Rotation ?? RotationNormal };
            string? invalid = loaded.Validate();
            if (invalid != null) { error = invalid; return false; }
            settings = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = FileName + ": " + ex.Message;
            return false;
        }
    }

    // Atomic (temp file + move). Allowed while the server runs. Throws InvalidDataException when invalid.
    public static void Save(string stateDir, PopulationGoalSettings settings)
    {
        string? invalid = settings.Validate();
        if (invalid != null) throw new InvalidDataException(invalid);
        PopulationFiles.WriteAtomic(PathIn(stateDir), settings);
    }
}

// Written by the server's population scheduler every ~15 s; read by the launcher.
public sealed record PopulationStatusReport(int Protocol, DateTime UpdatedUtc, long AppliedRevision, string? Error,
    RealmPopulationStatus[] Realms, long SavedTotal, long SavedCap)
{
    public const string FileName = "population-status.json";
    public const int CurrentProtocol = 1;

    public RealmPopulationStatus? For(eRealm realm)
    {
        if (Realms == null) return null;
        foreach (var r in Realms) if (r != null && r.Realm == realm) return r;
        return null;
    }

    public static string PathIn(string stateDir) => Path.Combine(stateDir, FileName);

    // False when missing, unreadable or a different protocol (error says which).
    public static bool TryLoad(string stateDir, out PopulationStatusReport? report, out string? error)
    {
        report = null;
        error = null;
        string path = PathIn(stateDir);
        if (!File.Exists(path)) { error = "missing"; return false; }
        try
        {
            var loaded = JsonSerializer.Deserialize<PopulationStatusReport>(PopulationFiles.ReadShared(path), PopulationFiles.Json);
            if (loaded == null) { error = FileName + " is empty."; return false; }
            if (loaded.Protocol != CurrentProtocol) { error = "protocol " + loaded.Protocol; report = loaded; return false; }
            report = loaded with { Realms = loaded.Realms ?? Array.Empty<RealmPopulationStatus>() };
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = FileName + ": " + ex.Message;
            return false;
        }
    }

    // Atomic (temp file + move), for the server's status writer.
    public static void Save(string stateDir, PopulationStatusReport report) =>
        PopulationFiles.WriteAtomic(PathIn(stateDir), report);
}

public sealed record RealmPopulationStatus(eRealm Realm, int Goal, int Live, int LoggingIn, int LoggingOut,
    long Archived, int CreatedLastHour, int ArchivedLastHour, int ReturnedLastHour, int RetiredLastHour,
    BracketStatus[] Brackets);

// MaxLevel 50 with MinLevel 50 is the level-50 bracket. Floor = the minimum live count the scheduler keeps.
public sealed record BracketStatus(int MinLevel, int MaxLevel, int Live, long Archived, int Target, int Floor);

internal static class PopulationFiles
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new RealmNameConverter() },
    };

    // eRealm has duplicate values (Albion = _FirstPlayerRealm, Hibernia = _LastPlayerRealm), so ToString() and
    // JsonStringEnumConverter can write the wrong name. Write the realm names explicitly; read names or numbers.
    private sealed class RealmNameConverter : JsonConverter<eRealm>
    {
        public override eRealm Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number) return (eRealm)reader.GetByte();
            string? text = reader.GetString();
            if (string.Equals(text, "Albion", StringComparison.OrdinalIgnoreCase)) return eRealm.Albion;
            if (string.Equals(text, "Midgard", StringComparison.OrdinalIgnoreCase)) return eRealm.Midgard;
            if (string.Equals(text, "Hibernia", StringComparison.OrdinalIgnoreCase)) return eRealm.Hibernia;
            return Enum.TryParse(text, true, out eRealm parsed) ? parsed : eRealm.None;
        }

        public override void Write(Utf8JsonWriter writer, eRealm value, JsonSerializerOptions options) =>
            writer.WriteStringValue(PopulationGoalSettings.RealmName(value));
    }


    // Reads while another process may be replacing the file.
    internal static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static void WriteAtomic<T>(string path, T value)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, value, Json);
                file.Flush(true);
            }
            // A reader holding the old file open can make the replace fail for a moment; retry briefly.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temporary, fullPath, true); break; }
                catch (Exception) when (attempt < 5) { System.Threading.Thread.Sleep(20); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
