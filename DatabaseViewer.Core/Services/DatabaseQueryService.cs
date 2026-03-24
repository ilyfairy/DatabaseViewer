using System.Data;
using System.Data.Common;
using System.Diagnostics;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

public sealed class DatabaseQueryService
{
    private const int MaxSqlResultRows = 500;

    public async Task<bool> TestConnectionAsync(ConnectionDefinition definition)
    {
        try
        {
            await using var connection = DbConnectionFactory.Create(definition);
            await connection.OpenAsync();
            await connection.CloseAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<TableDataResult> GetTablePageAsync(ConnectionDefinition definition, TableSchema schema, int offset, int pageSize, string? sortColumn = null, bool sortDescending = false)
    {
        await using var connection = DbConnectionFactory.Create(definition, schema.Table.DatabaseName);
        await connection.OpenAsync();

        var orderColumns = schema.PrimaryKeys.Count > 0
            ? schema.PrimaryKeys
            : new[] { schema.Columns.First().Name };
        var sql = SqlDialect.BuildPagedQuery(definition.ProviderType, schema.Table, orderColumns, pageSize, sortColumn, sortDescending);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "Offset", offset);
        AddParameter(command, "Take", pageSize + 1);

        await using var reader = await command.ExecuteReaderAsync();
        var dataTable = new DataTable();
        dataTable.Load(reader);

        if (dataTable.Columns.Contains("__RowNum"))
        {
            dataTable.Columns.Remove("__RowNum");
        }

        var hasMoreRows = dataTable.Rows.Count > pageSize;
        if (hasMoreRows)
        {
            dataTable.Rows[dataTable.Rows.Count - 1].Delete();
            dataTable.AcceptChanges();
        }

        return new TableDataResult
        {
            Data = dataTable,
            Schema = schema,
            Offset = offset,
            PageSize = pageSize,
            HasMoreRows = hasMoreRows,
        };
    }

    public async Task<RecordDetailsResult?> GetRecordAsync(ConnectionDefinition definition, TableSchema schema, IReadOnlyDictionary<string, object?> keyValues)
    {
        await using var connection = DbConnectionFactory.Create(definition, schema.Table.DatabaseName);
        await connection.OpenAsync();

        return await GetRecordAsync(connection, null, definition.ProviderType, schema, keyValues);
    }

    public async Task<RecordDetailsResult> UpdateCellAsync(
        ConnectionDefinition definition,
        TableSchema schema,
        IReadOnlyDictionary<string, object?> keyValues,
        string columnName,
        object? newValue)
    {
        if (schema.PrimaryKeys.Count == 0)
        {
            throw new InvalidOperationException("This table has no primary key and is read-only in the editor to avoid accidental updates.");
        }

        var column = schema.Columns.FirstOrDefault(entry => string.Equals(entry.Name, columnName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Column not found.");

        await using var connection = DbConnectionFactory.Create(definition, schema.Table.DatabaseName);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var currentRecord = await GetRecordAsync(connection, transaction, definition.ProviderType, schema, keyValues)
            ?? throw new InvalidOperationException("Record not found. It may have been changed or deleted by another user.");

        var updateSql = SqlDialect.BuildSingleColumnUpdateQuery(definition.ProviderType, schema.Table, schema.PrimaryKeys, column.Name);
        await using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = updateSql;
        AddParameter(updateCommand, "NewValue", newValue);
        foreach (var key in schema.PrimaryKeys)
        {
            AddParameter(updateCommand, key, keyValues[key]);
        }

        var affectedRows = await updateCommand.ExecuteNonQueryAsync();
        if (affectedRows == 0)
        {
            throw new InvalidOperationException("No rows were updated. The record may have been changed or deleted by another user.");
        }

        var nextKeys = keyValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (schema.PrimaryKeys.Contains(column.Name, StringComparer.OrdinalIgnoreCase))
        {
            nextKeys[column.Name] = newValue;
        }

        var updatedRecord = await GetRecordAsync(connection, transaction, definition.ProviderType, schema, nextKeys)
            ?? throw new InvalidOperationException("The row was updated, but the latest record snapshot could not be loaded.");

        await transaction.CommitAsync();
        return updatedRecord;
    }

    private async Task<RecordDetailsResult?> GetRecordAsync(
        DbConnection connection,
        DbTransaction? transaction,
        DatabaseProviderType providerType,
        TableSchema schema,
        IReadOnlyDictionary<string, object?> keyValues)
    {
        var keys = schema.PrimaryKeys.Count > 0
            ? schema.PrimaryKeys
            : keyValues.Keys.ToArray();
        if (keys.Count == 0)
        {
            return null;
        }

        var sql = SqlDialect.BuildSingleRecordQuery(providerType, schema.Table, keys);
        foreach (var key in keys)
        {
            if (!keyValues.TryGetValue(key, out _))
            {
                return null;
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var key in keys)
        {
            AddParameter(command, key, keyValues[key]);
        }

        await using var reader = await command.ExecuteReaderAsync();
        var dataTable = new DataTable();
        dataTable.Load(reader);
        if (dataTable.Rows.Count == 0)
        {
            return null;
        }

        var row = dataTable.Rows[0];
        return new RecordDetailsResult
        {
            Row = row,
            Schema = schema,
            Identity = BuildIdentity(schema, row),
        };
    }

    public async Task<PreviewQueryResult> GetReferencingRecordsPreviewAsync(ConnectionDefinition definition, TableSchema schema, string filterColumn, object? filterValue, int take)
    {
        await using var connection = DbConnectionFactory.Create(definition, schema.Table.DatabaseName);
        await connection.OpenAsync();

        var orderColumns = schema.PrimaryKeys.Count > 0
            ? schema.PrimaryKeys
            : new[] { schema.Columns.First().Name };
        var sql = SqlDialect.BuildPreviewByColumnQuery(definition.ProviderType, schema.Table, filterColumn, orderColumns);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "FilterValue", filterValue);
        AddParameter(command, "Take", take + 1);

        await using var reader = await command.ExecuteReaderAsync();
        var dataTable = new DataTable();
        dataTable.Load(reader);

        var hasMoreRows = dataTable.Rows.Count > take;
        if (hasMoreRows)
        {
            dataTable.Rows[dataTable.Rows.Count - 1].Delete();
            dataTable.AcceptChanges();
        }

        return new PreviewQueryResult
        {
            Data = dataTable,
            HasMoreRows = hasMoreRows,
        };
    }

    public async Task<TableSearchResult> SearchTableAsync(ConnectionDefinition definition, TableSchema schema, IReadOnlyList<string> searchColumns, string query, int offset, int pageSize, string? sortColumn = null, bool sortDescending = false)
    {
        if (searchColumns.Count == 0)
        {
            return new TableSearchResult
            {
                Data = new DataTable(),
                Offset = offset,
                PageSize = pageSize,
                TotalMatches = 0,
                HasMoreRows = false,
            };
        }

        await using var connection = DbConnectionFactory.Create(definition, schema.Table.DatabaseName);
        await connection.OpenAsync();

        var orderColumns = schema.PrimaryKeys.Count > 0
            ? schema.PrimaryKeys
            : new[] { schema.Columns.First().Name };

        var countSql = SqlDialect.BuildSearchCountQuery(definition.ProviderType, schema.Table, searchColumns);
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = countSql;
        AddParameter(countCommand, "SearchPattern", $"%{query}%");
        var totalMatches = Convert.ToInt32(await countCommand.ExecuteScalarAsync() ?? 0);

        var pagedSql = SqlDialect.BuildSearchPagedQuery(definition.ProviderType, schema.Table, orderColumns, searchColumns, sortColumn, sortDescending);
        await using var dataCommand = connection.CreateCommand();
        dataCommand.CommandText = pagedSql;
        AddParameter(dataCommand, "SearchPattern", $"%{query}%");
        AddParameter(dataCommand, "Offset", offset);
        AddParameter(dataCommand, "Take", pageSize + 1);

        await using var reader = await dataCommand.ExecuteReaderAsync();
        var dataTable = new DataTable();
        dataTable.Load(reader);

        if (dataTable.Columns.Contains("__RowNum"))
        {
            dataTable.Columns.Remove("__RowNum");
        }

        var hasMoreRows = dataTable.Rows.Count > pageSize;
        if (hasMoreRows)
        {
            dataTable.Rows[dataTable.Rows.Count - 1].Delete();
            dataTable.AcceptChanges();
        }

        return new TableSearchResult
        {
            Data = dataTable,
            Offset = offset,
            PageSize = pageSize,
            TotalMatches = totalMatches,
            HasMoreRows = hasMoreRows,
        };
    }

    public async Task<SqlExecutionResult> ExecuteSqlAsync(ConnectionDefinition definition, string databaseName, string sql)
    {
        await using var connection = DbConnectionFactory.Create(definition, databaseName);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 90;

        var stopwatch = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync();

        var resultSets = new List<SqlExecutionResultSet>();
        var resultIndex = 0;
        do
        {
            if (reader.FieldCount <= 0)
            {
                continue;
            }

            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(index => new SqlExecutionColumn
                {
                    Name = reader.GetName(index),
                    Type = reader.GetFieldType(index).Name,
                })
                .ToArray();

            var rows = new List<Dictionary<string, object?>>();
            var totalRows = 0;
            while (await reader.ReadAsync())
            {
                totalRows += 1;
                if (rows.Count >= MaxSqlResultRows)
                {
                    continue;
                }

                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var column in columns)
                {
                    row[column.Name] = NormalizeDbNull(reader[column.Name]);
                }

                rows.Add(row);
            }

            resultIndex += 1;
            resultSets.Add(new SqlExecutionResultSet
            {
                Name = $"结果 {resultIndex}",
                Columns = columns,
                Rows = rows,
                RowCount = totalRows,
                Truncated = totalRows > rows.Count,
            });
        }
        while (await reader.NextResultAsync());

        stopwatch.Stop();

        return new SqlExecutionResult
        {
            ExecutedSql = sql,
            AffectedRows = reader.RecordsAffected >= 0 ? reader.RecordsAffected : null,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            ResultSets = resultSets,
        };
    }

    public static IReadOnlyDictionary<string, object?> ExtractPrimaryKeyValues(TableSchema schema, DataRowView rowView)
    {
        var keys = schema.PrimaryKeys.Count > 0
            ? schema.PrimaryKeys
            : new[] { schema.Columns.First().Name };

        return keys.ToDictionary(key => key, key => NormalizeDbNull(rowView.Row[key]), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, object?> ExtractPrimaryKeyValues(TableSchema schema, DataRow row)
    {
        var keys = schema.PrimaryKeys.Count > 0
            ? schema.PrimaryKeys
            : new[] { schema.Columns.First().Name };

        return keys.ToDictionary(key => key, key => NormalizeDbNull(row[key]), StringComparer.OrdinalIgnoreCase);
    }

    public static RecordIdentity BuildIdentity(TableSchema schema, DataRowView rowView)
    {
        var primaryKeyText = string.Join(", ", ExtractPrimaryKeyValues(schema, rowView).Select(pair => $"{pair.Key}={FormatValue(pair.Value)}"));
        return new RecordIdentity
        {
            TableKey = schema.Table.QualifiedKey,
            PrimaryKeyText = primaryKeyText,
        };
    }

    public static RecordIdentity BuildIdentity(TableSchema schema, DataRow row)
    {
        var primaryKeyText = string.Join(", ", ExtractPrimaryKeyValues(schema, row).Select(pair => $"{pair.Key}={FormatValue(pair.Value)}"));
        return new RecordIdentity
        {
            TableKey = schema.Table.QualifiedKey,
            PrimaryKeyText = primaryKeyText,
        };
    }

    public static object? NormalizeDbNull(object? value) => value is DBNull ? null : value;

    public static string FormatValue(object? value) => value switch
    {
        null => "NULL",
        DBNull => "NULL",
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss"),
        _ => value.ToString() ?? string.Empty,
    };

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}