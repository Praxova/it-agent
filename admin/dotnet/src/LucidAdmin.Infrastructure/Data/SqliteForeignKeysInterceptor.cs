using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace LucidAdmin.Infrastructure.Data;

/// <summary>
/// Interceptor to enable foreign key constraints for SQLite connections.
/// SQLite has foreign keys disabled by default, so this ensures they are always enabled.
/// </summary>
public class SqliteForeignKeysInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        base.ConnectionOpened(connection, eventData);
        EnableForeignKeys(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        EnableForeignKeys(connection);
    }

    private static void EnableForeignKeys(DbConnection connection)
    {
        // Only apply to SQLite connections
        if (connection.GetType().Name == "SqliteConnection")
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
        }
    }
}
