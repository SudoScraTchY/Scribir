using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Scriber.Storage.Migrations;

/// <summary>
/// Executes pending schema migrations in version sequence within individual transactions.
/// </summary>
public static class DatabaseMigrator
{
    private static readonly IReadOnlyList<ISchemaMigration> AllMigrations =
    [
        new Migration_001_InitialSchema()
    ];

    public static void Migrate(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Ensure schema_migrations table exists
        using (var initCmd = connection.CreateCommand())
        {
            initCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at TEXT NOT NULL
                );
            """;
            initCmd.ExecuteNonQuery();
        }

        // Get currently applied version numbers
        var appliedVersions = new HashSet<int>();
        using (var queryCmd = connection.CreateCommand())
        {
            queryCmd.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";
            using var reader = queryCmd.ExecuteReader();
            while (reader.Read())
            {
                appliedVersions.Add(reader.GetInt32(0));
            }
        }

        // Apply pending migrations in ascending version order
        foreach (var migration in AllMigrations.OrderBy(m => m.Version))
        {
            if (appliedVersions.Contains(migration.Version))
                continue;

            using var tx = connection.BeginTransaction();
            try
            {
                migration.Up(connection, tx);

                using var recordCmd = connection.CreateCommand();
                recordCmd.Transaction = tx;
                recordCmd.CommandText = """
                    INSERT INTO schema_migrations (version, name, applied_at)
                    VALUES ($ver, $name, $at);
                """;
                recordCmd.Parameters.AddWithValue("$ver", migration.Version);
                recordCmd.Parameters.AddWithValue("$name", migration.Name);
                recordCmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                recordCmd.ExecuteNonQuery();

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}
