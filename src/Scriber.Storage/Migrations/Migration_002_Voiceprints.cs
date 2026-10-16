using Microsoft.Data.Sqlite;

namespace Scriber.Storage.Migrations;

/// <summary>
/// Migration 002: Adds voiceprints table for cross-session speaker identity (ADR-0010)
/// and links voiceprint_id in speakers table.
/// </summary>
public sealed class Migration_002_Voiceprints : ISchemaMigration
{
    public int Version => 2;
    public string Name => "002_Voiceprints";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;

        cmd.CommandText = """
            -- Voiceprints table
            CREATE TABLE IF NOT EXISTS voiceprints (
                id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                embedding BLOB NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_voiceprints_created ON voiceprints (created_at DESC);
        """;
        cmd.ExecuteNonQuery();

        // Check if voiceprint_id column exists on speakers before adding
        bool hasVoiceprintIdColumn = false;
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "PRAGMA table_info(speakers);";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                string colName = reader.GetString(1);
                if (string.Equals(colName, "voiceprint_id", StringComparison.OrdinalIgnoreCase))
                {
                    hasVoiceprintIdColumn = true;
                    break;
                }
            }
        }

        if (!hasVoiceprintIdColumn)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.Transaction = transaction;
            alterCmd.CommandText = "ALTER TABLE speakers ADD COLUMN voiceprint_id TEXT;";
            alterCmd.ExecuteNonQuery();
        }
    }
}
