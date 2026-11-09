using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DOL.GS
{
    /// <summary>Logging that never throws (the log queue is absent in unit tests).</summary>
    internal static class PopulationLog
    {
        public static void Info(DOL.Logging.Logger log, string message)
        {
            try { log.Info(message); } catch { }
        }

        public static void Warn(DOL.Logging.Logger log, string message)
        {
            try { log.Warn(message); } catch { }
        }

        public static void Error(DOL.Logging.Logger log, string message, Exception exception = null)
        {
            try
            {
                if (exception == null)
                    log.Error(message);
                else
                    log.Error(message, exception);
            }
            catch { }
        }
    }

    /// <summary>One table row as column name -> value (long, double, string, byte[] or null).</summary>
    public sealed class BotRow : Dictionary<string, object>
    {
        public BotRow() : base(StringComparer.OrdinalIgnoreCase) { }

        public string Text(string column) => TryGetValue(column, out object value) && value != null ? Convert.ToString(value) : null;

        public long Long(string column) =>
            TryGetValue(column, out object value) && value != null && value is not string ? Convert.ToInt64(value) :
            TryGetValue(column, out value) && value is string text && long.TryParse(text, out long parsed) ? parsed : 0;
    }

    /// <summary>
    /// A cold-archived bot: its offline_world_bots row, every Inventory row it owns (listings included), the unique item
    /// templates those rows use, and the relist / listing-hours rows of its listings. Stored as a versioned, compressed
    /// blob so a code update never forces a rebuild of the archive.
    /// </summary>
    public sealed class BotArchiveBlob
    {
        public const byte CurrentVersion = 1;
        private const byte BrotliCodec = 1;

        public BotRow Record { get; set; } = new();
        public List<BotRow> Items { get; set; } = new();
        public List<BotRow> Uniques { get; set; } = new();
        public List<BotRow> Relists { get; set; } = new();
        public List<BotRow> ListingHours { get; set; } = new();

        public byte[] Encode()
        {
            using var json = new MemoryStream();
            using (var writer = new Utf8JsonWriter(json))
            {
                writer.WriteStartObject();
                writer.WriteNumber("v", CurrentVersion);
                writer.WritePropertyName("record");
                WriteRow(writer, Record);
                WriteRows(writer, "items", Items);
                WriteRows(writer, "uniques", Uniques);
                WriteRows(writer, "relists", Relists);
                WriteRows(writer, "hours", ListingHours);
                writer.WriteEndObject();
            }

            using var packed = new MemoryStream();
            packed.WriteByte(CurrentVersion);
            packed.WriteByte(BrotliCodec);
            using (var brotli = new BrotliStream(packed, CompressionLevel.Optimal, leaveOpen: true))
                brotli.Write(json.GetBuffer(), 0, (int)json.Length);
            return packed.ToArray();
        }

        public static BotArchiveBlob Decode(byte[] data)
        {
            if (data == null || data.Length < 2)
                throw new InvalidDataException("Empty bot archive blob.");
            if (data[0] < 1 || data[0] > CurrentVersion)
                throw new InvalidDataException($"Bot archive blob version {data[0]} is newer than this server ({CurrentVersion}).");
            if (data[1] != BrotliCodec)
                throw new InvalidDataException($"Unknown bot archive codec {data[1]}.");

            using var input = new MemoryStream(data, 2, data.Length - 2);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var json = new MemoryStream();
            brotli.CopyTo(json);
            using JsonDocument document = JsonDocument.Parse(json.ToArray());
            JsonElement root = document.RootElement;
            // Version 1 is the only layout so far; later versions add fields, readers ignore what they don't know.
            return new BotArchiveBlob
            {
                Record = root.TryGetProperty("record", out JsonElement record) ? ReadRow(record) : new BotRow(),
                Items = ReadRows(root, "items"),
                Uniques = ReadRows(root, "uniques"),
                Relists = ReadRows(root, "relists"),
                ListingHours = ReadRows(root, "hours"),
            };
        }

        private static void WriteRows(Utf8JsonWriter writer, string name, List<BotRow> rows)
        {
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            foreach (BotRow row in rows)
                WriteRow(writer, row);
            writer.WriteEndArray();
        }

        private static void WriteRow(Utf8JsonWriter writer, BotRow row)
        {
            writer.WriteStartObject();
            foreach ((string column, object value) in row)
            {
                writer.WritePropertyName(column);
                switch (value)
                {
                    case null:
                    case DBNull:
                        writer.WriteNullValue();
                        break;
                    case string text:
                        writer.WriteStringValue(text);
                        break;
                    case byte[] bytes:
                        writer.WriteStartObject();
                        writer.WriteBase64String("$b", bytes);
                        writer.WriteEndObject();
                        break;
                    case double or float or decimal:
                        writer.WriteStartObject();
                        writer.WriteNumber("$r", Convert.ToDouble(value));
                        writer.WriteEndObject();
                        break;
                    case bool flag:
                        writer.WriteNumberValue(flag ? 1 : 0);
                        break;
                    case DateTime time:
                        writer.WriteStringValue(time.ToString("yyyy-MM-dd HH:mm:ss"));
                        break;
                    default:
                        writer.WriteNumberValue(Convert.ToInt64(value));
                        break;
                }
            }
            writer.WriteEndObject();
        }

        private static List<BotRow> ReadRows(JsonElement root, string name)
        {
            var rows = new List<BotRow>();
            if (root.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
                foreach (JsonElement row in array.EnumerateArray())
                    rows.Add(ReadRow(row));
            return rows;
        }

        private static BotRow ReadRow(JsonElement element)
        {
            var row = new BotRow();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                JsonElement value = property.Value;
                row[property.Name] = value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.TryGetInt64(out long whole) ? whole : value.GetDouble(),
                    JsonValueKind.Object when value.TryGetProperty("$b", out JsonElement bytes) => bytes.GetBytesFromBase64(),
                    JsonValueKind.Object when value.TryGetProperty("$r", out JsonElement real) => real.GetDouble(),
                    JsonValueKind.True => 1L,
                    JsonValueKind.False => 0L,
                    _ => value.GetRawText(),
                };
            }
            return row;
        }
    }

    /// <summary>Raw row access to the world database (copying whole rows keeps every column, present and future).</summary>
    public static class BotRowSql
    {
        public const string BotTable = "offline_world_bots";
        public const string InventoryTable = "Inventory";
        public const string UniqueTable = "ItemUnique";
        public const string RelistTable = "offline_true165_relists";
        public const string ListingHoursTable = "offline_true165_listing_hours";
        public const string GuildTable = "offline_world_bot_guild";
        public const string CommandTable = "offline_bot_commands";
        public const string HiredTable = "offline_hired_companions";

        private static readonly ConcurrentDictionary<(string Database, string Table), string[]> ColumnCache = new();

        public static SQLiteConnection Open(string connectionString)
        {
            var connection = new SQLiteConnection(connectionString);
            connection.Open();
            using var busy = connection.CreateCommand();
            busy.CommandText = "PRAGMA busy_timeout=5000;";
            busy.ExecuteNonQuery();
            return connection;
        }

        public static string[] Columns(SQLiteConnection connection, string table)
        {
            return ColumnCache.GetOrAdd((connection.FileName ?? connection.DataSource ?? string.Empty, table), _ =>
            {
                var columns = new List<string>();
                using var info = connection.CreateCommand();
                info.CommandText = $"PRAGMA table_info(`{table}`)";
                using SQLiteDataReader reader = info.ExecuteReader();
                while (reader.Read())
                    columns.Add(reader.GetString(1));
                return columns.ToArray();
            });
        }

        /// <summary>Forgets cached column lists (tests create tables after first use).</summary>
        public static void ClearColumnCache() => ColumnCache.Clear();

        public static bool HasTable(SQLiteConnection connection, string table) => Columns(connection, table).Length > 0;

        public static List<BotRow> Select(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            var rows = new List<BotRow>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach ((string name, object value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new BotRow();
                for (int index = 0; index < reader.FieldCount; index++)
                    row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                rows.Add(row);
            }
            return rows;
        }

        public static int Execute(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach ((string name, object value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return command.ExecuteNonQuery();
        }

        public static object Scalar(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach ((string name, object value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            object result = command.ExecuteScalar();
            return result is DBNull ? null : result;
        }

        /// <summary>Inserts a copied row, keeping only columns the table has today (columns added later keep their defaults).</summary>
        public static bool Insert(SQLiteConnection connection, SQLiteTransaction transaction, string table, BotRow row, bool orIgnore = false)
        {
            var known = new HashSet<string>(Columns(connection, table), StringComparer.OrdinalIgnoreCase);
            var columns = row.Keys.Where(known.Contains).ToArray();
            if (columns.Length == 0)
                return false;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var text = new StringBuilder(orIgnore ? "INSERT OR IGNORE INTO `" : "INSERT INTO `").Append(table).Append("` (");
            text.Append(string.Join(", ", columns.Select(column => $"`{column}`"))).Append(") VALUES (");
            text.Append(string.Join(", ", columns.Select((_, index) => $"@p{index}"))).Append(')');
            command.CommandText = text.ToString();
            for (int index = 0; index < columns.Length; index++)
                command.Parameters.AddWithValue($"@p{index}", row[columns[index]] ?? DBNull.Value);
            return command.ExecuteNonQuery() == 1;
        }

        public static BotRow ReadBot(SQLiteConnection connection, SQLiteTransaction transaction, long botId) =>
            Select(connection, transaction, $"SELECT * FROM `{BotTable}` WHERE BotId=@id", ("@id", botId)).FirstOrDefault();

        /// <summary>Reads everything a bot owns in the world database, for the cold archive.</summary>
        public static BotArchiveBlob ReadOwned(SQLiteConnection connection, SQLiteTransaction transaction, long botId)
        {
            BotRow record = ReadBot(connection, transaction, botId);
            if (record == null)
                return null;
            string owner = AutonomousBotEconomy.GetOwnerId(botId);
            var blob = new BotArchiveBlob
            {
                Record = record,
                Items = Select(connection, transaction, $"SELECT * FROM `{InventoryTable}` WHERE OwnerID=@o", ("@o", owner)),
            };
            if (HasTable(connection, UniqueTable))
                blob.Uniques = Select(connection, transaction,
                    $"SELECT u.* FROM `{UniqueTable}` u WHERE u.Id_nb IN (SELECT UTemplate_Id FROM `{InventoryTable}` WHERE OwnerID=@o AND UTemplate_Id IS NOT NULL AND UTemplate_Id<>'')",
                    ("@o", owner));
            if (HasTable(connection, RelistTable))
                blob.Relists = Select(connection, transaction,
                    $"SELECT r.* FROM `{RelistTable}` r WHERE r.InventoryId IN (SELECT Inventory_ID FROM `{InventoryTable}` WHERE OwnerID=@o)", ("@o", owner));
            if (HasTable(connection, ListingHoursTable))
                blob.ListingHours = Select(connection, transaction,
                    $"SELECT h.* FROM `{ListingHoursTable}` h WHERE h.InventoryId IN (SELECT Inventory_ID FROM `{InventoryTable}` WHERE OwnerID=@o)", ("@o", owner));
            return blob;
        }

        /// <summary>
        /// Deletes what a bot owns: relist and listing-hours rows of its items, the unique templates only its items use,
        /// its items (every slot, listings included), and optionally its record, guild row and pending commands.
        /// </summary>
        public static void DeleteOwned(SQLiteConnection connection, SQLiteTransaction transaction, long botId, bool record, bool guild,
            long keepCommandId = 0)
        {
            string owner = AutonomousBotEconomy.GetOwnerId(botId);
            if (HasTable(connection, RelistTable))
                Execute(connection, transaction,
                    $"DELETE FROM `{RelistTable}` WHERE InventoryId IN (SELECT Inventory_ID FROM `{InventoryTable}` WHERE OwnerID=@o)", ("@o", owner));
            if (HasTable(connection, ListingHoursTable))
                Execute(connection, transaction,
                    $"DELETE FROM `{ListingHoursTable}` WHERE InventoryId IN (SELECT Inventory_ID FROM `{InventoryTable}` WHERE OwnerID=@o)", ("@o", owner));
            if (HasTable(connection, UniqueTable))
                Execute(connection, transaction,
                    $"DELETE FROM `{UniqueTable}` WHERE Id_nb IN (SELECT UTemplate_Id FROM `{InventoryTable}` WHERE OwnerID=@o AND UTemplate_Id IS NOT NULL AND UTemplate_Id<>'') " +
                    $"AND NOT EXISTS (SELECT 1 FROM `{InventoryTable}` j WHERE j.UTemplate_Id=`{UniqueTable}`.Id_nb AND j.OwnerID<>@o)", ("@o", owner));
            Execute(connection, transaction, $"DELETE FROM `{InventoryTable}` WHERE OwnerID=@o", ("@o", owner));
            if (guild && HasTable(connection, GuildTable))
                Execute(connection, transaction, $"DELETE FROM `{GuildTable}` WHERE BotId=@id", ("@id", botId));
            if (record)
            {
                if (HasTable(connection, CommandTable))
                    Execute(connection, transaction,
                        $"DELETE FROM `{CommandTable}` WHERE BotId=@id AND State='Pending' AND CommandId<>@keep", ("@id", botId), ("@keep", keepCommandId));
                // Old auction tables from before the Realm Exchange (the launcher's purge clears them too).
                if (HasTable(connection, "offline_auction_escrow"))
                    Execute(connection, transaction, "DELETE FROM offline_auction_escrow WHERE BidderBotId=@id", ("@id", botId));
                if (HasTable(connection, "offline_auction_listings"))
                    Execute(connection, transaction, "DELETE FROM offline_auction_listings WHERE SellerBotId=@id OR HighBidderBotId=@id", ("@id", botId));
                Execute(connection, transaction, $"DELETE FROM `{BotTable}` WHERE BotId=@id", ("@id", botId));
            }
        }

        /// <summary>Puts an archived bot back into the world database (offline). Returns false if its record already exists.</summary>
        public static bool InsertOwned(SQLiteConnection connection, SQLiteTransaction transaction, BotArchiveBlob blob)
        {
            long botId = blob.Record.Long("BotId");
            if (botId <= 0 || ReadBot(connection, transaction, botId) != null)
                return false;
            var record = new BotRow();
            foreach ((string column, object value) in blob.Record)
                record[column] = value;
            record["IsOnline"] = 0L;
            if (!Insert(connection, transaction, BotTable, record))
                return false;
            if (HasTable(connection, UniqueTable))
                foreach (BotRow unique in blob.Uniques)
                    Insert(connection, transaction, UniqueTable, unique, orIgnore: true);
            foreach (BotRow item in blob.Items)
                Insert(connection, transaction, InventoryTable, item, orIgnore: true);
            if (HasTable(connection, RelistTable))
                foreach (BotRow relist in blob.Relists)
                    Insert(connection, transaction, RelistTable, relist, orIgnore: true);
            if (HasTable(connection, ListingHoursTable))
                foreach (BotRow hours in blob.ListingHours)
                    Insert(connection, transaction, ListingHoursTable, hours, orIgnore: true);
            return true;
        }
    }
}
