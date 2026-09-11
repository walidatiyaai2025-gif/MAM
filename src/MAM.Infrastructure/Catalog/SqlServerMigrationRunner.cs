using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Catalog;

public sealed class SqlServerMigrationRunner
{
    private static readonly Regex GoBatchSeparator = new(
        @"^\s*GO\s*(?:--.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly SqlServerConnectionFactory _connections;

    public SqlServerMigrationRunner(SqlServerConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<string>> ApplyDirectoryAsync(string migrationDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(migrationDirectory))
        {
            throw new ArgumentException("Migration directory is required.", nameof(migrationDirectory));
        }

        var fullDirectory = Path.GetFullPath(migrationDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"Migration directory was not found: {fullDirectory}");
        }

        var files = Directory.GetFiles(fullDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException("No SQL migration files were found.");
        }

        await using var connection = await _connections.OpenAsync(cancellationToken);
        var applied = new List<string>(files.Length);

        foreach (var file in files)
        {
            var script = await File.ReadAllTextAsync(file, cancellationToken);
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new InvalidOperationException($"SQL migration is empty: {Path.GetFileName(file)}");
            }

            foreach (var batch in GoBatchSeparator.Split(script).Where(static batch => !string.IsNullOrWhiteSpace(batch)))
            {
                await using var command = new SqlCommand(batch, connection)
                {
                    CommandTimeout = _connections.CommandTimeoutSeconds
                };
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            applied.Add(Path.GetFileName(file));
        }

        return applied;
    }
}
