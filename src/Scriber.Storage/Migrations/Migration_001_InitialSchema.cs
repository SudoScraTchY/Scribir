using Microsoft.Data.Sqlite;

namespace Scriber.Storage.Migrations;

/// <summary>
/// Migration 001: Creates base tables for Transcripts, Speakers, Segments, Words, and FTS5 search index.
/// </summary>
public sealed class Migration_001_InitialSchema : ISchemaMigration
{
    public int Version => 1;
    public string Name => "001_InitialSchema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;

        cmd.CommandText = """
            -- Transcripts table
            CREATE TABLE IF NOT EXISTS transcripts (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                created_at TEXT NOT NULL,
                language_code TEXT NOT NULL,
                audio_path TEXT NOT NULL,
                audio_duration_seconds REAL NOT NULL,
                audio_sample_rate INTEGER NOT NULL
            );

            -- Speakers table
            CREATE TABLE IF NOT EXISTS speakers (
                transcript_id TEXT NOT NULL,
                speaker_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                color_hex TEXT NOT NULL,
                PRIMARY KEY (transcript_id, speaker_id),
                FOREIGN KEY (transcript_id) REFERENCES transcripts (id) ON DELETE CASCADE
            );

            -- Segments table
            CREATE TABLE IF NOT EXISTS segments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                transcript_id TEXT NOT NULL,
                speaker_id TEXT NOT NULL,
                start_seconds REAL NOT NULL,
                end_seconds REAL NOT NULL,
                segment_index INTEGER NOT NULL,
                text TEXT NOT NULL,
                FOREIGN KEY (transcript_id) REFERENCES transcripts (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_segments_transcript ON segments (transcript_id, segment_index);

            -- Words table
            CREATE TABLE IF NOT EXISTS words (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                segment_id INTEGER NOT NULL,
                word_index INTEGER NOT NULL,
                text TEXT NOT NULL,
                start_seconds REAL NOT NULL,
                end_seconds REAL NOT NULL,
                confidence REAL NOT NULL,
                FOREIGN KEY (segment_id) REFERENCES segments (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_words_segment ON words (segment_id, word_index);

            -- FTS5 Full-Text Search virtual table
            CREATE VIRTUAL TABLE IF NOT EXISTS transcript_fts USING fts5(
                transcript_id UNINDEXED,
                segment_id UNINDEXED,
                speaker_id UNINDEXED,
                text,
                tokenize='unicode61'
            );
        """;

        cmd.ExecuteNonQuery();
    }
}
