using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

public static class SqlDialect
{
    public static string QuoteIdentifier(DatabaseProviderType providerType, string identifier)
    {
        return providerType switch
        {
            DatabaseProviderType.SqlServer => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]",
            DatabaseProviderType.MySql => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`",
            DatabaseProviderType.PostgreSql => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
            _ => identifier,
        };
    }

    public static string GetQualifiedTableName(DatabaseProviderType providerType, DbTableInfo table)
    {
        return providerType switch
        {
            DatabaseProviderType.SqlServer => string.IsNullOrWhiteSpace(table.SchemaName)
                ? $"{QuoteIdentifier(providerType, table.DatabaseName)}..{QuoteIdentifier(providerType, table.TableName)}"
                : $"{QuoteIdentifier(providerType, table.DatabaseName)}.{QuoteIdentifier(providerType, table.SchemaName)}.{QuoteIdentifier(providerType, table.TableName)}",
            DatabaseProviderType.MySql => $"{QuoteIdentifier(providerType, table.DatabaseName)}.{QuoteIdentifier(providerType, table.TableName)}",
            DatabaseProviderType.PostgreSql => string.IsNullOrWhiteSpace(table.SchemaName)
                ? QuoteIdentifier(providerType, table.TableName)
                : $"{QuoteIdentifier(providerType, table.SchemaName)}.{QuoteIdentifier(providerType, table.TableName)}",
            _ => table.TableName,
        };
    }

    public static string BuildPagedQuery(DatabaseProviderType providerType, DbTableInfo table, IReadOnlyList<string> orderColumns, int pageSize, string? sortColumn = null, bool sortDescending = false)
    {
        var orderBy = BuildOrderByClause(providerType, orderColumns, sortColumn, sortDescending);
        var qualifiedTable = GetQualifiedTableName(providerType, table);

        return providerType switch
        {
            DatabaseProviderType.SqlServer => $@"
                WITH OrderedRows AS (
                    SELECT *, ROW_NUMBER() OVER (ORDER BY {orderBy}) AS [__RowNum]
                    FROM {qualifiedTable}
                )
                SELECT *
                FROM OrderedRows
                WHERE [__RowNum] > @Offset AND [__RowNum] <= (@Offset + @Take)
                ORDER BY [__RowNum]",
            DatabaseProviderType.MySql => $"SELECT * FROM {qualifiedTable} ORDER BY {orderBy} LIMIT @Take OFFSET @Offset",
            DatabaseProviderType.PostgreSql => $"SELECT * FROM {qualifiedTable} ORDER BY {orderBy} LIMIT @Take OFFSET @Offset",
            _ => throw new NotSupportedException(),
        };
    }

    public static string BuildSingleRecordQuery(DatabaseProviderType providerType, DbTableInfo table, IReadOnlyList<string> keyColumns)
    {
        var whereClause = string.Join(" AND ", keyColumns.Select(column => $"{QuoteIdentifier(providerType, column)} = @{column}"));
        var qualifiedTable = GetQualifiedTableName(providerType, table);

        return providerType switch
        {
            DatabaseProviderType.SqlServer => $"SELECT TOP 1 * FROM {qualifiedTable} WHERE {whereClause}",
            DatabaseProviderType.MySql => $"SELECT * FROM {qualifiedTable} WHERE {whereClause} LIMIT 1",
            DatabaseProviderType.PostgreSql => $"SELECT * FROM {qualifiedTable} WHERE {whereClause} LIMIT 1",
            _ => throw new NotSupportedException(),
        };
    }

    public static string BuildSingleColumnUpdateQuery(DatabaseProviderType providerType, DbTableInfo table, IReadOnlyList<string> keyColumns, string columnName)
    {
        var qualifiedTable = GetQualifiedTableName(providerType, table);
        var whereClause = string.Join(" AND ", keyColumns.Select(column => $"{QuoteIdentifier(providerType, column)} = @{column}"));
        return $"UPDATE {qualifiedTable} SET {QuoteIdentifier(providerType, columnName)} = @NewValue WHERE {whereClause}";
    }

    public static string BuildPreviewByColumnQuery(DatabaseProviderType providerType, DbTableInfo table, string filterColumn, IReadOnlyList<string> orderColumns)
    {
        var qualifiedTable = GetQualifiedTableName(providerType, table);
        var whereClause = $"{QuoteIdentifier(providerType, filterColumn)} = @FilterValue";
        var orderBy = string.Join(", ", orderColumns.Select(column => QuoteIdentifier(providerType, column)));

        return providerType switch
        {
            DatabaseProviderType.SqlServer => $"SELECT TOP (@Take) * FROM {qualifiedTable} WHERE {whereClause} ORDER BY {orderBy}",
            DatabaseProviderType.MySql => $"SELECT * FROM {qualifiedTable} WHERE {whereClause} ORDER BY {orderBy} LIMIT @Take",
            DatabaseProviderType.PostgreSql => $"SELECT * FROM {qualifiedTable} WHERE {whereClause} ORDER BY {orderBy} LIMIT @Take",
            _ => throw new NotSupportedException(),
        };
    }

    public static string BuildSearchPagedQuery(DatabaseProviderType providerType, DbTableInfo table, IReadOnlyList<string> orderColumns, IReadOnlyList<string> searchColumns, string? sortColumn = null, bool sortDescending = false)
    {
        var qualifiedTable = GetQualifiedTableName(providerType, table);
        var orderBy = BuildOrderByClause(providerType, orderColumns, sortColumn, sortDescending);
        var whereClause = BuildSearchWhereClause(providerType, searchColumns);

        return providerType switch
        {
            DatabaseProviderType.SqlServer => $@"
                WITH OrderedRows AS (
                    SELECT *, ROW_NUMBER() OVER (ORDER BY {orderBy}) AS [__RowNum]
                    FROM {qualifiedTable}
                    WHERE {whereClause}
                )
                SELECT *
                FROM OrderedRows
                WHERE [__RowNum] > @Offset AND [__RowNum] <= (@Offset + @Take)
                ORDER BY [__RowNum]",
            DatabaseProviderType.MySql => $"SELECT * FROM {qualifiedTable} WHERE {whereClause} ORDER BY {orderBy} LIMIT @Take OFFSET @Offset",
            DatabaseProviderType.PostgreSql => $"SELECT * FROM {qualifiedTable} WHERE {whereClause} ORDER BY {orderBy} LIMIT @Take OFFSET @Offset",
            _ => throw new NotSupportedException(),
        };
    }

    public static string BuildSearchCountQuery(DatabaseProviderType providerType, DbTableInfo table, IReadOnlyList<string> searchColumns)
    {
        var qualifiedTable = GetQualifiedTableName(providerType, table);
        var whereClause = BuildSearchWhereClause(providerType, searchColumns);
        return $"SELECT COUNT(1) FROM {qualifiedTable} WHERE {whereClause}";
    }

    private static string BuildSearchWhereClause(DatabaseProviderType providerType, IReadOnlyList<string> searchColumns)
    {
        return string.Join(" OR ", searchColumns.Select(column => $"{BuildSearchTextExpression(providerType, column)} LIKE @SearchPattern"));
    }

    private static string BuildOrderByClause(DatabaseProviderType providerType, IReadOnlyList<string> orderColumns, string? sortColumn, bool sortDescending)
    {
        var fragments = new List<string>();
        if (!string.IsNullOrWhiteSpace(sortColumn))
        {
            fragments.Add($"{QuoteIdentifier(providerType, sortColumn)} {(sortDescending ? "DESC" : "ASC")}");
        }

        fragments.AddRange(orderColumns
            .Where(column => !string.Equals(column, sortColumn, StringComparison.OrdinalIgnoreCase))
            .Select(column => $"{QuoteIdentifier(providerType, column)} ASC"));

        return string.Join(", ", fragments);
    }

    private static string BuildSearchTextExpression(DatabaseProviderType providerType, string column)
    {
        var quoted = QuoteIdentifier(providerType, column);
        return providerType switch
        {
            DatabaseProviderType.SqlServer => $"CONVERT(nvarchar(max), {quoted})",
            DatabaseProviderType.MySql => $"CAST({quoted} AS CHAR)",
            DatabaseProviderType.PostgreSql => $"CAST({quoted} AS text)",
            _ => quoted,
        };
    }
}