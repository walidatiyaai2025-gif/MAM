using Microsoft.Data.SqlClient;

namespace MAM.Infrastructure.Catalog;

public sealed class SqlServerConnectionFactory
{
    private readonly string _connectionString;
    private readonly bool _enableRetryOnFailure;

    public SqlServerConnectionFactory(string connectionString, int commandTimeoutSeconds, bool enableRetryOnFailure)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("SQL Server connection string is required.", nameof(connectionString));
        }

        if (commandTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
        }

        _connectionString = connectionString;
        CommandTimeoutSeconds = commandTimeoutSeconds;
        _enableRetryOnFailure = enableRetryOnFailure;
    }

    public int CommandTimeoutSeconds { get; }

    public async ValueTask<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var attempts = _enableRetryOnFailure ? 3 : 1;
        Exception? finalException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var connection = new SqlConnection(_connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (Exception ex) when (ex is SqlException or TimeoutException)
            {
                finalException = ex;
                await connection.DisposeAsync();
                if (attempt == attempts)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("SQL Server connection could not be opened.", finalException);
    }
}
