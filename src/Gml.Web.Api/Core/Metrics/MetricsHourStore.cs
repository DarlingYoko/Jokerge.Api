using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Gml.Web.Api.Core.Metrics;

/// <summary>
///     Hourly aggregates in their own small SQLite file (not the EF database, so no migration is needed for
///     throw-away telemetry). One row per hour, the bucket as JSON; rows are upserted, so writing the current hour
///     every few minutes is safe.
/// </summary>
public sealed class MetricsHourStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    private readonly string _connectionString;

    public MetricsHourStore(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            // No pooled handle keeps the file locked between the rare writes.
            Pooling = false
        }.ToString();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS MetricsHours (HourStartTicks INTEGER PRIMARY KEY, Payload TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    public string DatabasePath { get; }

    public IReadOnlyList<MetricsBucket> Load(DateTime sinceUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT HourStartTicks, Payload FROM MetricsHours WHERE HourStartTicks >= $since ORDER BY HourStartTicks";
        command.Parameters.AddWithValue("$since", sinceUtc.ToUniversalTime().Ticks);

        var result = new List<MetricsBucket>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var bucket = JsonSerializer.Deserialize<MetricsBucket>(reader.GetString(1), JsonOptions);
            if (bucket is null)
                continue;

            bucket.StartUtc = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
            result.Add(bucket);
        }

        return result;
    }

    public void Save(IEnumerable<MetricsBucket> hours)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        foreach (var hour in hours)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO MetricsHours (HourStartTicks, Payload) VALUES ($start, $payload) " +
                "ON CONFLICT(HourStartTicks) DO UPDATE SET Payload = excluded.Payload";
            command.Parameters.AddWithValue("$start", hour.StartUtc.ToUniversalTime().Ticks);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(hour, JsonOptions));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public int Prune(DateTime beforeUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MetricsHours WHERE HourStartTicks < $before";
        command.Parameters.AddWithValue("$before", beforeUtc.ToUniversalTime().Ticks);
        return command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
