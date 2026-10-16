using Microsoft.Data.Sqlite;

namespace Scriber.Storage.Migrations;

/// <summary>
/// Migration 003: Adds transcript_versions table for diff-aware version history (PH2-INT-005).
/// </summary>
public sealed class Migration_003_TranscriptVersions : ISchemaMigration
{
    public int Version => 3;
    public string Name => "003_TranscriptVersions";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;

        cmd.CommandText = """
            -- Transcript versions table for non-destructive re-transcription history
            CREATE TABLE IF NOT EXISTS transcript_versions (
                id TEXT PRIMARY KEY,
                transcript_id TEXT NOT NULL,
                version_number INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                note TEXT NOT NULL,
                content_json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_transcript_versions ON transcript_versions (transcript_id, version_number);
        """;
        cmd.ExecuteNonQuery();
    }
}
