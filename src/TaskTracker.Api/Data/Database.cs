using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace TaskTracker.Api.Data;

/// <summary>
/// The SQLite database: opens connections and brings the schema up to date at startup.
/// </summary>
/// <remarks>
/// Migrations are the embedded <c>Data/Migrations/NNNN_name.sql</c> scripts, applied in order.
/// The applied version is SQLite's <c>user_version</c>. Before migrating an existing database,
/// a copy goes to <c>backups/</c> next to it, so a bad migration can be undone by hand.
/// Keep migrations additive (new tables and nullable columns): a rollback runs the previous
/// release against the newer schema.
/// Migrations run with foreign keys off, so one can rebuild a table the way SQLite documents
/// (copy it, drop the old one, rename) without the drop cascading to the rows that point at it;
/// a migration that leaves a broken reference is rolled back.
/// </remarks>
public sealed partial class Database(string connectionString)
{
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return connection;
    }

    public void Migrate(ILogger logger)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (!IsInMemory(dataSource))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        using var connection = Open();
        connection.Execute("PRAGMA journal_mode = WAL;");

        var current = connection.ExecuteScalar<long>("PRAGMA user_version;");
        var pending = Migrations().Where(m => m.Version > current).ToList();
        var latest = Migrations().Select(m => m.Version).DefaultIfEmpty(0).Max();

        if (current > latest)
        {
            logger.LogWarning(
                "Database schema is version {Current}, newer than this release knows ({Latest}): running anyway",
                current, latest);
        }
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date (version {Version})", current);
            return;
        }

        if (current > 0 && !IsInMemory(dataSource))
        {
            var backup = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(dataSource))!,
                "backups",
                $"{Path.GetFileNameWithoutExtension(dataSource)}-v{current}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            connection.Execute($"VACUUM INTO '{backup.Replace("'", "''")}';");
            logger.LogInformation("Backed up the database to {Backup} before migrating", backup);
        }

        // Only takes effect outside a transaction. Open() turns it back on for the next user of
        // this pooled connection.
        connection.Execute("PRAGMA foreign_keys = OFF;");
        foreach (var migration in pending)
        {
            using var tx = connection.BeginTransaction();
            connection.Execute(migration.Sql, transaction: tx);
            if (connection.Query("PRAGMA foreign_key_check;", transaction: tx).Any())
                throw new InvalidOperationException($"Migration {migration.Name} left rows pointing at rows that don't exist");
            connection.Execute($"PRAGMA user_version = {migration.Version};", transaction: tx);
            tx.Commit();
            logger.LogInformation("Applied migration {Name}", migration.Name);
        }
        connection.Execute("PRAGMA foreign_keys = ON;");
    }

    private static bool IsInMemory(string dataSource) =>
        dataSource == ":memory:" || dataSource.StartsWith("file::memory:", StringComparison.Ordinal);

    private sealed record Migration(long Version, string Name, string Sql);

    private static IEnumerable<Migration> Migrations()
    {
        var assembly = typeof(Database).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            var match = MigrationName().Match(resource);
            if (!match.Success) continue;
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            yield return new Migration(long.Parse(match.Groups[1].Value), resource, reader.ReadToEnd());
        }
    }

    [GeneratedRegex(@"^migrations/(\d+)_[\w-]+\.sql$")]
    private static partial Regex MigrationName();
}
