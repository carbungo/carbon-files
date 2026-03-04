using System.Data;
using Microsoft.Data.Sqlite;

namespace CarbonFiles.Infrastructure.Data;

internal static class Db
{
    public static async Task<int> ExecuteAsync(
        IDbConnection connection,
        string sql,
        Action<SqliteParameterCollection>? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken ct = default)
    {
        var sqlite = (SqliteConnection)connection;
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = sql;
        if (transaction != null)
            cmd.Transaction = (SqliteTransaction)transaction;
        parameters?.Invoke(cmd.Parameters);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<T> ExecuteScalarAsync<T>(
        IDbConnection connection,
        string sql,
        Action<SqliteParameterCollection>? parameters = null,
        CancellationToken ct = default)
    {
        var sqlite = (SqliteConnection)connection;
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = sql;
        parameters?.Invoke(cmd.Parameters);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull)
            return default!;
        return (T)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    public static async Task<List<T>> QueryAsync<T>(
        IDbConnection connection,
        string sql,
        Action<SqliteParameterCollection>? parameters,
        Func<SqliteDataReader, T> read,
        IDbTransaction? transaction = null,
        CancellationToken ct = default)
    {
        var sqlite = (SqliteConnection)connection;
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = sql;
        if (transaction != null)
            cmd.Transaction = (SqliteTransaction)transaction;
        parameters?.Invoke(cmd.Parameters);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<T>();
        while (await reader.ReadAsync(ct))
            list.Add(read(reader));
        return list;
    }

    public static async Task<T?> QueryFirstOrDefaultAsync<T>(
        IDbConnection connection,
        string sql,
        Action<SqliteParameterCollection>? parameters,
        Func<SqliteDataReader, T> read,
        CancellationToken ct = default) where T : class
    {
        var sqlite = (SqliteConnection)connection;
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = sql;
        parameters?.Invoke(cmd.Parameters);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return read(reader);
        return null;
    }
}
