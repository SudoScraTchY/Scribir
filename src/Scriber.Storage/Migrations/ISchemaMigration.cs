using Microsoft.Data.Sqlite;

namespace Scriber.Storage.Migrations;

/// <summary>
/// Defines a versioned database schema migration step.
/// </summary>
public interface ISchemaMigration
{
    int Version { get; }
    string Name { get; }
    void Up(SqliteConnection connection, SqliteTransaction transaction);
}
