using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Scriber.Core.Domain;
using Scriber.Storage.Migrations;

namespace Scriber.Storage;

/// <summary>
/// SQLite implementation of ITranscriptRepository with FTS5 search and schema migrations.
/// Implements PH1-STORE-001.
/// </summary>
public sealed class SqliteTranscriptRepository : ITranscriptRepository, IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _managedConnection;
    private bool _initialized;
    private bool _disposed;

    public SqliteTranscriptRepository(string databasePathOrConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePathOrConnectionString);

        if (databasePathOrConnectionString.Contains('='))
        {
            _connectionString = databasePathOrConnectionString;
        }
        else
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(databasePathOrConnectionString));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePathOrConnectionString,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ConnectionString;
        }

        // Keep a single open connection for in-memory databases to persist data across operations
        if (_connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            _managedConnection = new SqliteConnection(_connectionString);
            _managedConnection.Open();
        }
    }

    public SqliteTranscriptRepository(SqliteConnection existingConnection)
    {
        ArgumentNullException.ThrowIfNull(existingConnection);
        _managedConnection = existingConnection;
        _connectionString = existingConnection.ConnectionString;
        if (_managedConnection.State != ConnectionState.Open)
        {
            _managedConnection.Open();
        }
    }

    private SqliteConnection CreateOrGetConnection()
    {
        if (_managedConnection != null)
        {
            EnsureInitialized(_managedConnection);
            return _managedConnection;
        }

        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        EnsureInitialized(conn);
        return conn;
    }

    private void EnsureInitialized(SqliteConnection conn)
    {
        if (_initialized) return;

        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
            pragmaCmd.ExecuteNonQuery();
        }

        DatabaseMigrator.Migrate(conn);
        _initialized = true;
    }

    public async Task SaveAsync(Transcript transcript, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ct.ThrowIfCancellationRequested();

        var conn = CreateOrGetConnection();
        bool ownsConnection = _managedConnection == null;

        try
        {
            using var tx = conn.BeginTransaction();

            // 1. Delete existing if updating
            string idStr = transcript.Id.ToString();
            using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = "DELETE FROM transcripts WHERE id = $id; DELETE FROM transcript_fts WHERE transcript_id = $id;";
                delCmd.Parameters.AddWithValue("$id", idStr);
                await delCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // 2. Insert Transcript
            using (var insertTransCmd = conn.CreateCommand())
            {
                insertTransCmd.Transaction = tx;
                insertTransCmd.CommandText = """
                    INSERT INTO transcripts (id, title, created_at, language_code, audio_path, audio_duration_seconds, audio_sample_rate)
                    VALUES ($id, $title, $created_at, $lang, $path, $duration, $rate);
                """;
                insertTransCmd.Parameters.AddWithValue("$id", idStr);
                insertTransCmd.Parameters.AddWithValue("$title", transcript.Title);
                insertTransCmd.Parameters.AddWithValue("$created_at", transcript.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
                insertTransCmd.Parameters.AddWithValue("$lang", transcript.LanguageCode);
                insertTransCmd.Parameters.AddWithValue("$path", transcript.Audio.LocalPath);
                insertTransCmd.Parameters.AddWithValue("$duration", transcript.Audio.Duration.TotalSeconds);
                insertTransCmd.Parameters.AddWithValue("$rate", transcript.Audio.SampleRate);
                await insertTransCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // 3. Insert Speakers
            foreach (var spk in transcript.Speakers)
            {
                using var insertSpkCmd = conn.CreateCommand();
                insertSpkCmd.Transaction = tx;
                insertSpkCmd.CommandText = """
                    INSERT INTO speakers (transcript_id, speaker_id, display_name, color_hex, voiceprint_id)
                    VALUES ($tid, $sid, $name, $color, $vpid);
                """;
                insertSpkCmd.Parameters.AddWithValue("$tid", idStr);
                insertSpkCmd.Parameters.AddWithValue("$sid", spk.Id);
                insertSpkCmd.Parameters.AddWithValue("$name", spk.DisplayName);
                insertSpkCmd.Parameters.AddWithValue("$color", spk.ColorHex);
                insertSpkCmd.Parameters.AddWithValue("$vpid", (object?)spk.VoiceprintId?.ToString() ?? DBNull.Value);
                await insertSpkCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // 4. Insert Segments and Words
            for (int segIdx = 0; segIdx < transcript.Segments.Count; segIdx++)
            {
                var seg = transcript.Segments[segIdx];
                string segText = string.Join(" ", seg.Words.Select(w => w.Text)).Trim();

                long segmentRowId;
                using (var insertSegCmd = conn.CreateCommand())
                {
                    insertSegCmd.Transaction = tx;
                    insertSegCmd.CommandText = """
                        INSERT INTO segments (transcript_id, speaker_id, start_seconds, end_seconds, segment_index, text)
                        VALUES ($tid, $sid, $start, $end, $idx, $text);
                        SELECT last_insert_rowid();
                    """;
                    insertSegCmd.Parameters.AddWithValue("$tid", idStr);
                    insertSegCmd.Parameters.AddWithValue("$sid", seg.SpeakerId);
                    insertSegCmd.Parameters.AddWithValue("$start", seg.Start.TotalSeconds);
                    insertSegCmd.Parameters.AddWithValue("$end", seg.End.TotalSeconds);
                    insertSegCmd.Parameters.AddWithValue("$idx", segIdx);
                    insertSegCmd.Parameters.AddWithValue("$text", segText);

                    var result = await insertSegCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    segmentRowId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
                }

                // Insert into FTS
                using (var insertFtsCmd = conn.CreateCommand())
                {
                    insertFtsCmd.Transaction = tx;
                    insertFtsCmd.CommandText = """
                        INSERT INTO transcript_fts (transcript_id, segment_id, speaker_id, text)
                        VALUES ($tid, $segid, $sid, $text);
                    """;
                    insertFtsCmd.Parameters.AddWithValue("$tid", idStr);
                    insertFtsCmd.Parameters.AddWithValue("$segid", segmentRowId);
                    insertFtsCmd.Parameters.AddWithValue("$sid", seg.SpeakerId);
                    insertFtsCmd.Parameters.AddWithValue("$text", segText);
                    await insertFtsCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // Insert Words
                for (int wIdx = 0; wIdx < seg.Words.Count; wIdx++)
                {
                    var word = seg.Words[wIdx];
                    using var insertWordCmd = conn.CreateCommand();
                    insertWordCmd.Transaction = tx;
                    insertWordCmd.CommandText = """
                        INSERT INTO words (segment_id, word_index, text, start_seconds, end_seconds, confidence)
                        VALUES ($segid, $widx, $text, $start, $end, $conf);
                    """;
                    insertWordCmd.Parameters.AddWithValue("$segid", segmentRowId);
                    insertWordCmd.Parameters.AddWithValue("$widx", wIdx);
                    insertWordCmd.Parameters.AddWithValue("$text", word.Text);
                    insertWordCmd.Parameters.AddWithValue("$start", word.Start.TotalSeconds);
                    insertWordCmd.Parameters.AddWithValue("$end", word.End.TotalSeconds);
                    insertWordCmd.Parameters.AddWithValue("$conf", word.Confidence);
                    await insertWordCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            tx.Commit();
        }
        finally
        {
            if (ownsConnection)
            {
                conn.Dispose();
            }
        }
    }

    public async Task<Transcript?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = CreateOrGetConnection();
        bool ownsConnection = _managedConnection == null;

        try
        {
            string idStr = id.ToString();

            // 1. Load Transcript header
            string? title = null;
            DateTimeOffset createdAt = default;
            string? languageCode = null;
            string? audioPath = null;
            double audioDurationSec = 0;
            int audioRate = 16000;

            using (var transCmd = conn.CreateCommand())
            {
                transCmd.CommandText = """
                    SELECT title, created_at, language_code, audio_path, audio_duration_seconds, audio_sample_rate
                    FROM transcripts WHERE id = $id;
                """;
                transCmd.Parameters.AddWithValue("$id", idStr);
                using var reader = await transCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return null;

                title = reader.GetString(0);
                createdAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
                languageCode = reader.GetString(2);
                audioPath = reader.GetString(3);
                audioDurationSec = reader.GetDouble(4);
                audioRate = reader.GetInt32(5);
            }

            // 2. Load Speakers
            var speakers = new List<Speaker>();
            using (var spkCmd = conn.CreateCommand())
            {
                spkCmd.CommandText = "SELECT speaker_id, display_name, color_hex, voiceprint_id FROM speakers WHERE transcript_id = $id;";
                spkCmd.Parameters.AddWithValue("$id", idStr);
                using var reader = await spkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    Guid? vpid = null;
                    if (!reader.IsDBNull(3))
                    {
                        string vpidStr = reader.GetString(3);
                        if (Guid.TryParse(vpidStr, out var parsedGuid))
                        {
                            vpid = parsedGuid;
                        }
                    }

                    speakers.Add(new Speaker(reader.GetString(0), reader.GetString(1), reader.GetString(2), vpid));
                }
            }

            // 3. Load Segments
            var segmentEntries = new List<(long RowId, string SpeakerId, double Start, double End)>();
            using (var segCmd = conn.CreateCommand())
            {
                segCmd.CommandText = """
                    SELECT id, speaker_id, start_seconds, end_seconds
                    FROM segments WHERE transcript_id = $id ORDER BY segment_index ASC;
                """;
                segCmd.Parameters.AddWithValue("$id", idStr);
                using var reader = await segCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    segmentEntries.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3)));
                }
            }

            // 4. Load Words for all segments
            var wordsBySegment = new Dictionary<long, List<Word>>();
            if (segmentEntries.Count > 0)
            {
                using var wordCmd = conn.CreateCommand();
                wordCmd.CommandText = """
                    SELECT w.segment_id, w.text, w.start_seconds, w.end_seconds, w.confidence
                    FROM words w
                    JOIN segments seg ON seg.id = w.segment_id
                    WHERE seg.transcript_id = $id
                    ORDER BY w.segment_id, w.word_index ASC;
                """;
                wordCmd.Parameters.AddWithValue("$id", idStr);
                using var reader = await wordCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    long segRowId = reader.GetInt64(0);
                    if (!wordsBySegment.TryGetValue(segRowId, out var list))
                    {
                        list = [];
                        wordsBySegment[segRowId] = list;
                    }

                    list.Add(new Word(
                        reader.GetString(1),
                        TimeSpan.FromSeconds(reader.GetDouble(2)),
                        TimeSpan.FromSeconds(reader.GetDouble(3)),
                        reader.GetDouble(4)));
                }
            }

            // Assemble Segments
            var segments = new List<Segment>(segmentEntries.Count);
            foreach (var (rowId, spkId, startSec, endSec) in segmentEntries)
            {
                var segWords = wordsBySegment.TryGetValue(rowId, out var wList) ? wList : [];
                segments.Add(new Segment(
                    spkId,
                    TimeSpan.FromSeconds(startSec),
                    TimeSpan.FromSeconds(endSec),
                    segWords));
            }

            var audioRef = new AudioReference(
                localPath: audioPath,
                duration: TimeSpan.FromSeconds(audioDurationSec),
                sampleRate: audioRate);

            return new Transcript(
                id: id,
                title: title,
                createdAt: createdAt,
                languageCode: languageCode,
                speakers: speakers,
                segments: segments,
                audio: audioRef);
        }
        finally
        {
            if (ownsConnection)
            {
                conn.Dispose();
            }
        }
    }

    public async Task<IReadOnlyList<TranscriptSummary>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = CreateOrGetConnection();
        bool ownsConnection = _managedConnection == null;

        try
        {
            var summaries = new List<TranscriptSummary>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 
                    t.id, 
                    t.title, 
                    t.created_at, 
                    t.language_code, 
                    t.audio_duration_seconds,
                    (SELECT COUNT(*) FROM speakers s WHERE s.transcript_id = t.id) AS speaker_count,
                    (SELECT COUNT(*) FROM segments seg WHERE seg.transcript_id = t.id) AS segment_count
                FROM transcripts t
                ORDER BY t.created_at DESC;
            """;

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                summaries.Add(new TranscriptSummary(
                    Id: Guid.Parse(reader.GetString(0)),
                    Title: reader.GetString(1),
                    CreatedAt: DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                    LanguageCode: reader.GetString(3),
                    Duration: TimeSpan.FromSeconds(reader.GetDouble(4)),
                    SpeakerCount: reader.GetInt32(5),
                    SegmentCount: reader.GetInt32(6)));
            }

            return summaries;
        }
        finally
        {
            if (ownsConnection)
            {
                conn.Dispose();
            }
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = CreateOrGetConnection();
        bool ownsConnection = _managedConnection == null;

        try
        {
            using var tx = conn.BeginTransaction();
            string idStr = id.ToString();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM transcripts WHERE id = $id; DELETE FROM transcript_fts WHERE transcript_id = $id; DELETE FROM transcript_versions WHERE transcript_id = $id;";
                cmd.Parameters.AddWithValue("$id", idStr);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            tx.Commit();
        }
        finally
        {
            if (ownsConnection)
            {
                conn.Dispose();
            }
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ct.ThrowIfCancellationRequested();

        string ftsQuery = PrepareFts5Query(query);
        if (string.IsNullOrWhiteSpace(ftsQuery))
            return [];

        var conn = CreateOrGetConnection();
        bool ownsConnection = _managedConnection == null;

        try
        {
            var results = new List<SearchResult>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 
                    fts.transcript_id,
                    t.title,
                    fts.segment_id,
                    fts.speaker_id,
                    seg.start_seconds,
                    seg.end_seconds,
                    fts.text,
                    snippet(transcript_fts, 3, '<b>', '</b>', '...', 15) AS match_snippet
                FROM transcript_fts fts
                JOIN transcripts t ON t.id = fts.transcript_id
                JOIN segments seg ON seg.id = fts.segment_id
                WHERE transcript_fts MATCH $query
                ORDER BY rank;
            """;
            cmd.Parameters.AddWithValue("$query", ftsQuery);

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(new SearchResult(
                    TranscriptId: Guid.Parse(reader.GetString(0)),
                    TranscriptTitle: reader.GetString(1),
                    SegmentId: reader.GetInt64(2),
                    SpeakerId: reader.GetString(3),
                    Start: TimeSpan.FromSeconds(reader.GetDouble(4)),
                    End: TimeSpan.FromSeconds(reader.GetDouble(5)),
                    Text: reader.GetString(6),
                    Snippet: reader.GetString(7)));
            }

            return results;
        }
        finally
        {
            if (ownsConnection)
            {
                conn.Dispose();
            }
        }
    }

    public async Task<Guid> ArchiveVersionAsync(Guid transcriptId, string note = "", CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var current = await GetByIdAsync(transcriptId, ct).ConfigureAwait(false);
        if (current == null)
        {
            throw new InvalidOperationException($"Cannot archive: transcript with ID '{transcriptId}' does not exist.");
        }

        var conn = CreateOrGetConnection();
        bool ownsConnection = _managedConnection == null;

        try
        {
            int nextVersion = 1;
            using (var verCmd = conn.CreateCommand())
            {
                verCmd.CommandText = "SELECT COALESCE(MAX(version_number), 0) + 1 FROM transcript_versions WHERE transcript_id = $id;";
                verCmd.Parameters.AddWithValue("$id", transcriptId.ToString());
                var res = await verCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (res != null && res != DBNull.Value)
                {
                    nextVersion = Convert.ToInt32(res, CultureInfo.InvariantCulture);
                }
            }

            Guid versionId = Guid.NewGuid();
            string json = JsonSerializer.Serialize(current);

            using (var insertCmd = conn.CreateCommand())
            {
                insertCmd.CommandText = """
                    INSERT INTO transcript_versions (id, transcript_id, version_number, created_at, note, content_json)
                    VALUES ($id, $transcript_id, $ver, $created_at, $note, $json);
                """;
                insertCmd.Parameters.AddWithValue("$id", versionId.ToString());
                insertCmd.Parameters.AddWithValue("$transcript_id", transcriptId.ToString());
                insertCmd.Parameters.AddWithValue("$ver", nextVersion);
                insertCmd.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                insertCmd.Parameters.AddWithValue("$note", note ?? string.Empty);
                insertCmd.Parameters.AddWithValue("$json", json);

                await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            return versionId;
        }
        finally
        {
            if (ownsConnection) conn.Dispose();
        }
    }

    public async Task<IReadOnlyList<TranscriptVersionInfo>> GetVersionsAsync(Guid transcriptId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = CreateOrGetConnection();
        bool ownsConnection = _managedConnection == null;

        try
        {
            var list = new List<TranscriptVersionInfo>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, transcript_id, version_number, created_at, note
                FROM transcript_versions
                WHERE transcript_id = $id
                ORDER BY version_number DESC;
            """;
            cmd.Parameters.AddWithValue("$id", transcriptId.ToString());

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new TranscriptVersionInfo(
                    Id: Guid.Parse(reader.GetString(0)),
                    TranscriptId: Guid.Parse(reader.GetString(1)),
                    VersionNumber: reader.GetInt32(2),
                    CreatedAt: DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                    Note: reader.GetString(4)));
            }

            return list;
        }
        finally
        {
            if (ownsConnection) conn.Dispose();
        }
    }

    public async Task<Transcript?> GetVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = CreateOrGetConnection();
        bool ownsConnection = _managedConnection == null;

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT content_json FROM transcript_versions WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", versionId.ToString());

            var json = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            if (string.IsNullOrWhiteSpace(json)) return null;

            return JsonSerializer.Deserialize<Transcript>(json);
        }
        finally
        {
            if (ownsConnection) conn.Dispose();
        }
    }

    private static string PrepareFts5Query(string query)
    {
        // Tokenize query into safe alphanumeric or unicode terms with prefix matching
        var terms = Regex.Matches(query, @"[\p{L}\p{N}]+").Select(m => m.Value).ToList();
        if (terms.Count == 0) return string.Empty;

        // Join terms as prefix-search tokens: "term*"
        return string.Join(" ", terms.Select(t => $"\"{t}\"*"));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _managedConnection?.Dispose();
            _disposed = true;
        }
    }
}
