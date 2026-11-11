using System.Data.SQLite;
using System.Globalization;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The Player XP rate and Gamebot XP rate options (owner 2026-11-10: back from the old repo). They are server
/// properties (xp_rate, bot_xp_rate) in the world database, like the launcher's other options, default 1x. The True
/// 1.65 content refresh does not write them, so the launcher's choice wins over true165-rules.json.
/// </summary>
internal static class XpRateSettings
{
    public const string PlayerKey = "xp_rate";
    public const string BotKey = "bot_xp_rate";
    public static readonly double[] Presets = [1, 2, 3, 5, 10];

    /// <summary>Server properties the launcher's Options own: the content refresh leaves their values alone.</summary>
    public static bool IsLauncherOwned(string key) =>
        string.Equals(key, PlayerKey, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, BotKey, StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(string key, double rate) =>
        (key == PlayerKey || key == BotKey) && Presets.Any(preset => Math.Abs(preset - rate) < 0.001);

    public static string Label(double rate) => Math.Abs(rate - 1) < 0.001 ? "1×  Original" : $"{Format(rate)}×";

    public static string Format(double rate) => rate.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A stored value, or 1x when it is missing or not a positive number.</summary>
    public static double Parse(object? value) =>
        value != null && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) && parsed > 0 ? parsed : 1;

    public static double Read(SQLiteConnection connection, string key)
    {
        using var exists = new SQLiteCommand("SELECT 1 FROM sqlite_master WHERE type='table' AND name='ServerProperty'", connection);
        if (exists.ExecuteScalar() == null)
            return 1;
        using var command = new SQLiteCommand("SELECT Value FROM ServerProperty WHERE `Key`=@key LIMIT 1", connection);
        command.Parameters.AddWithValue("@key", key);
        return Parse(command.ExecuteScalar());
    }

    /// <summary>Writes one rate. ServerProperty_ID is unique, so a new row gets its own id, not an empty one.</summary>
    public static void Write(SQLiteConnection connection, string key, double rate)
    {
        if (!IsValid(key, rate))
            throw new InvalidOperationException("Unsupported experience-rate selection.");
        string stored = Format(rate);
        string updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        using SQLiteTransaction transaction = connection.BeginTransaction();
        using var update = new SQLiteCommand("UPDATE ServerProperty SET Value=@value, LastTimeRowUpdated=@updated WHERE `Key`=@key", connection, transaction);
        update.Parameters.AddWithValue("@key", key);
        update.Parameters.AddWithValue("@value", stored);
        update.Parameters.AddWithValue("@updated", updated);
        if (update.ExecuteNonQuery() == 0)
        {
            using var insert = new SQLiteCommand(
                "INSERT INTO ServerProperty (Category, `Key`, Description, DefaultValue, Value, LastTimeRowUpdated, ServerProperty_ID) " +
                "VALUES ('rates', @key, @description, '1', @value, @updated, @id)", connection, transaction);
            insert.Parameters.AddWithValue("@key", key);
            insert.Parameters.AddWithValue("@description", key == PlayerKey
                ? "Player experience rate selected in the Offline DAoC launcher (hired companions use it too)."
                : "Autonomous gamebot experience rate selected in the Offline DAoC launcher.");
            insert.Parameters.AddWithValue("@value", stored);
            insert.Parameters.AddWithValue("@updated", updated);
            insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}

internal sealed record XpRateOption(double Multiplier, string Label)
{
    public override string ToString() => Label;
}
