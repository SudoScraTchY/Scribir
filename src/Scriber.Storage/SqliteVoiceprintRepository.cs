namespace Scriber.Storage;

using System.Data;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Scriber.Core.Domain;
using Scriber.Storage.Migrations;

/// <summary>
/// SQLite implementation of IVoiceprintRepository for persisting enrolled voiceprints (ADR-0010).
/// </summary>
public sealed class SqliteVoiceprintRepository : IVoiceprintRepository, IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _managedConnection;
    private bool _initialized;
    private bool _disposed;

    static SqliteVoiceprintRepository()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public SqliteVoiceprintRepository(string databasePathOrConnectionString, string? password = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePathOrConnectionString);

        if (databasePathOrConnectionString.Contains('='))
        {
            var builder = new SqliteConnectionStringBuilder(databasePathOrConnectionString);
            if (!string.IsNullOrEmpty(password))
            {
                builder.Password = password;
            }
            _connectionString = builder.ConnectionString;
        }
        else
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(databasePathOrConnectionString));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePathOrConnectionString,
                Mode = SqliteOpenMode.ReadWriteCreate
            };
            if (!string.IsNullOrEmpty(password))
            {
                builder.Password = password;
            }
            _connectionString = builder.ConnectionString;
        }

        if (_connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            _managedConnection = new SqliteConnection(_connectionString);
            _managedConnection.Open();
        }
    }

    public SqliteVoiceprintRepository(SqliteConnection existingConnection)
    {
        ArgumentNullException.ThrowIfNull(existingConnection);
        _managedConnection = existingConnection;
        _connectionString = existingConnection.ConnectionString;
        if (_managedConnection.State != ConnectionState.Open)
        {
            _managedConnection.Open();
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        var conn = GetConnection();
        try
        {
            DatabaseMigrator.Migrate(conn);
            _initialized = true;
        }
        finally
        {
            if (_managedConnection == null)
            {
                conn.Dispose();
            }
        }
    }

    private SqliteConnection GetConnection()
    {
        if (_managedConnection != null)
        {
            if (_managedConnection.State != ConnectionState.Open)
            {
                _managedConnection.Open();
            }
            return _managedConnection;
        }

        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public Task<IReadOnlyList<Voiceprint>> GetAllAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        var conn = GetConnection();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, display_name, embedding, created_at
                FROM voiceprints
                ORDER BY created_at DESC;
            """;

            using var reader = cmd.ExecuteReader();
            var list = new List<Voiceprint>();

            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();

                var id = Guid.Parse(reader.GetString(0));
                string displayName = reader.GetString(1);
                byte[] bytes = (byte[])reader[2];
                string createdAtStr = reader.GetString(3);
                var createdAt = DateTimeOffset.Parse(createdAtStr, CultureInfo.InvariantCulture);

                var floats = new float[bytes.Length / sizeof(float)];
                Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);

                list.Add(new Voiceprint(id, displayName, floats, createdAt));
            }

            return Task.FromResult<IReadOnlyList<Voiceprint>>(list);
        }
        finally
        {
            if (_managedConnection == null)
            {
                conn.Dispose();
            }
        }
    }

    public Task<Voiceprint?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        var conn = GetConnection();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, display_name, embedding, created_at
                FROM voiceprints
                WHERE id = $id;
            """;
            cmd.Parameters.AddWithValue("$id", id.ToString());

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return Task.FromResult<Voiceprint?>(null);
            }

            string displayName = reader.GetString(1);
            byte[] bytes = (byte[])reader[2];
            string createdAtStr = reader.GetString(3);
            var createdAt = DateTimeOffset.Parse(createdAtStr, CultureInfo.InvariantCulture);

            var floats = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);

            var vp = new Voiceprint(id, displayName, floats, createdAt);
            return Task.FromResult<Voiceprint?>(vp);
        }
        finally
        {
            if (_managedConnection == null)
            {
                conn.Dispose();
            }
        }
    }

    public Task SaveAsync(Voiceprint voiceprint, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(voiceprint);
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        var conn = GetConnection();
        try
        {
            byte[] bytes = MemoryMarshal.AsBytes(voiceprint.Embedding.Span).ToArray();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO voiceprints (id, display_name, embedding, created_at)
                VALUES ($id, $name, $emb, $at)
                ON CONFLICT(id) DO UPDATE SET
                    display_name = excluded.display_name,
                    embedding = excluded.embedding,
                    created_at = excluded.created_at;
            """;
            cmd.Parameters.AddWithValue("$id", voiceprint.Id.ToString());
            cmd.Parameters.AddWithValue("$name", voiceprint.DisplayName);
            cmd.Parameters.AddWithValue("$emb", bytes);
            cmd.Parameters.AddWithValue("$at", voiceprint.CreatedAt.ToString("O", CultureInfo.InvariantCulture));

            cmd.ExecuteNonQuery();
            return Task.CompletedTask;
        }
        finally
        {
            if (_managedConnection == null)
            {
                conn.Dispose();
            }
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();

        var conn = GetConnection();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM voiceprints WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id.ToString());
            cmd.ExecuteNonQuery();

            return Task.CompletedTask;
        }
        finally
        {
            if (_managedConnection == null)
            {
                conn.Dispose();
            }
        }
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
