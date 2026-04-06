using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

public sealed class DatabaseQueryService
{
    private const int MaxSqlResultRows = 500;
    private static readonly Regex SqlServerBatchSeparatorRegex = new("^GO(?:\\s+(\\d+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    public async Task RekeySqliteDatabaseAsync(ConnectionDefinition currentDefinition, SqliteCipherOptions targetCipher)
    {
        if (currentDefinition.ProviderType != DatabaseProviderType.Sqlite)
        {
            throw new InvalidOperationException("只有 SQLite 连接支持修改加密密钥。", null);
        }

        await using var connection = DbConnectionFactory.Create(currentDefinition);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = DbConnectionFactory.BuildSqliteRekeyCommandText(targetCipher.KeyFormat, targetCipher.Password);
        await command.ExecuteNonQueryAsync();

        await using var validateCommand = connection.CreateCommand();
        validateCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        await validateCommand.ExecuteScalarAsync();
    }

    public async Task<TableDataResult> GetTablePageAsync(ConnectionDefinition definition, TableSchema schema, int offset, int pageSize, string? sortColumn = null, bool sortDescending = false)
    {
        await using var connection = DbConnectionFactory.Create(definition, schema.Table.DatabaseName);
        await connection.OpenAsync();

        var resolvedRowCount = schema.Table.RowCount;
        if (!resolvedRowCount.HasValue)
        {
            resolvedRowCount = await GetTableRowCountAsync(connection, definition.ProviderType, schema.Table);
            schema.Table.RowCount = resolvedRowCount;
        }

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
            RowCount = resolvedRowCount,
            HasMoreRows = hasMoreRows,
        };
    }

    private static async Task<int?> GetTableRowCountAsync(DbConnection connection, DatabaseProviderType providerType, DbTableInfo table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SqlDialect.BuildTableCountQuery(providerType, table);
        var scalar = await command.ExecuteScalarAsync();
        return scalar switch
        {
            null => null,
            DBNull => null,
            int intValue => intValue,
            long longValue when longValue <= int.MaxValue && longValue >= int.MinValue => (int)longValue,
            long longValue => (int)Math.Clamp(longValue, int.MinValue, int.MaxValue),
            decimal decimalValue => (int)decimalValue,
            _ => Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture),
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
        AddKeyParameters(updateCommand, schema.PrimaryKeys, keyValues);

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

    /// <summary>通过主键删除一行记录。</summary>
    public async Task DeleteRowAsync(
        ConnectionDefinition definition,
        TableSchema schema,
        IReadOnlyDictionary<string, object?> keyValues)
    {
        if (schema.PrimaryKeys.Count == 0)
        {
            throw new InvalidOperationException("This table has no primary key and is read-only in the editor to avoid accidental deletes.");
        }

        await using var connection = DbConnectionFactory.Create(definition, schema.Table.DatabaseName);
        await connection.OpenAsync();

        var deleteSql = SqlDialect.BuildDeleteRowQuery(definition.ProviderType, schema.Table, schema.PrimaryKeys);
        await using var command = connection.CreateCommand();
        command.CommandText = deleteSql;
        AddKeyParameters(command, schema.PrimaryKeys, keyValues);

        var affectedRows = await command.ExecuteNonQueryAsync();
        if (affectedRows == 0)
        {
            throw new InvalidOperationException("No rows were deleted. The record may have been changed or deleted by another user.");
        }
    }

    public async Task<RecordDetailsResult?> InsertRowAsync(
        ConnectionDefinition definition,
        TableSchema schema,
        IReadOnlyDictionary<string, object?> values)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException("At least one column value is required to insert a row.");
        }

        var writableColumns = schema.Columns
            .Where(column => values.ContainsKey(column.Name))
            .ToArray();
        if (writableColumns.Length == 0)
        {
            throw new InvalidOperationException("No writable values were provided for the new row.");
        }

        var parameterNames = writableColumns.Select((_, index) => $"p{index}").ToArray();

        await using var connection = DbConnectionFactory.Create(definition, schema.Table.DatabaseName);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var insertSql = SqlDialect.BuildInsertRowQuery(
            definition.ProviderType,
            schema.Table,
            writableColumns.Select(column => column.Name).ToArray(),
            parameterNames);

        await using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = insertSql;
        for (var index = 0; index < writableColumns.Length; index += 1)
        {
            AddParameter(insertCommand, parameterNames[index], values[writableColumns[index].Name]);
        }

        if (definition.ProviderType == DatabaseProviderType.MySql)
        {
            await insertCommand.ExecuteNonQueryAsync();
            var keyValues = await ResolveInsertedMySqlKeyValuesAsync(connection, transaction, schema, values);
            RecordDetailsResult? insertedRecord = null;
            if (keyValues.Count == schema.PrimaryKeys.Count && keyValues.Count > 0)
            {
                insertedRecord = await GetRecordAsync(connection, transaction, definition.ProviderType, schema, keyValues);
            }

            await transaction.CommitAsync();
            return insertedRecord;
        }

        await using var reader = await insertCommand.ExecuteReaderAsync();
        var dataTable = new DataTable();
        dataTable.Load(reader);
        await transaction.CommitAsync();

        if (dataTable.Rows.Count == 0)
        {
            return null;
        }

        return new RecordDetailsResult
        {
            Row = dataTable.Rows[0],
            Schema = schema,
            Identity = BuildIdentity(schema, dataTable.Rows[0]),
        };
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
        AddKeyParameters(command, keys, keyValues);

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

        var stopwatch = Stopwatch.StartNew();
        var resultSets = new List<SqlExecutionResultSet>();
        var affectedRows = 0;
        var hasAffectedRows = false;

        if (definition.ProviderType == DatabaseProviderType.SqlServer)
        {
            foreach (var batch in SplitSqlServerBatches(sql))
            {
                var batchResult = await ExecuteSqlBatchAsync(connection, batch);
                resultSets.AddRange(batchResult.ResultSets);
                if (batchResult.AffectedRows is int batchAffectedRows)
                {
                  affectedRows += batchAffectedRows;
                  hasAffectedRows = true;
                }
            }
        }
        else
        {
            var batchResult = await ExecuteSqlBatchAsync(connection, sql);
            resultSets.AddRange(batchResult.ResultSets);
            if (batchResult.AffectedRows is int batchAffectedRows)
            {
                affectedRows = batchAffectedRows;
                hasAffectedRows = true;
            }
        }

        stopwatch.Stop();

        for (var index = 0; index < resultSets.Count; index += 1)
        {
            resultSets[index] = new SqlExecutionResultSet
            {
                Name = $"结果 {index + 1}",
                Columns = resultSets[index].Columns,
                Rows = resultSets[index].Rows,
                RowCount = resultSets[index].RowCount,
                Truncated = resultSets[index].Truncated,
            };
        }

        return new SqlExecutionResult
        {
            ExecutedSql = sql,
            AffectedRows = hasAffectedRows ? affectedRows : null,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            ResultSets = resultSets,
        };
    }

    private async Task<SqlExecutionResult> ExecuteSqlBatchAsync(DbConnection connection, string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlExecutionResult
            {
                ExecutedSql = sql,
                AffectedRows = null,
                ElapsedMs = 0,
                ResultSets = Array.Empty<SqlExecutionResultSet>(),
            };
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 90;

        await using var reader = await command.ExecuteReaderAsync();

        var resultSets = new List<SqlExecutionResultSet>();
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

            resultSets.Add(new SqlExecutionResultSet
            {
                Name = string.Empty,
                Columns = columns,
                Rows = rows,
                RowCount = totalRows,
                Truncated = totalRows > rows.Count,
            });
        }
        while (await reader.NextResultAsync());

        return new SqlExecutionResult
        {
            ExecutedSql = sql,
            AffectedRows = reader.RecordsAffected >= 0 ? reader.RecordsAffected : null,
            ElapsedMs = 0,
            ResultSets = resultSets,
        };
    }

    private static IReadOnlyList<string> SplitSqlServerBatches(string sql)
    {
        var batches = new List<string>();
        var builder = new StringBuilder();
        var lines = sql.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        foreach (var line in lines)
        {
            var match = SqlServerBatchSeparatorRegex.Match(line.Trim());
            if (match.Success)
            {
                var batch = builder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    var repeatCount = match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var parsedCount)
                        ? Math.Max(parsedCount, 1)
                        : 1;
                    for (var index = 0; index < repeatCount; index += 1)
                    {
                        batches.Add(batch);
                    }
                }

                builder.Clear();
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }
            builder.Append(line);
        }

        var trailingBatch = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(trailingBatch))
        {
            batches.Add(trailingBatch);
        }

        return batches;
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

    private static async Task<Dictionary<string, object?>> ResolveInsertedMySqlKeyValuesAsync(
        DbConnection connection,
        DbTransaction transaction,
        TableSchema schema,
        IReadOnlyDictionary<string, object?> insertedValues)
    {
        var keyValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var primaryKey in schema.PrimaryKeys)
        {
            if (insertedValues.TryGetValue(primaryKey, out var value))
            {
                keyValues[primaryKey] = value;
            }
        }

        if (schema.PrimaryKeys.Count == 1)
        {
            var primaryKey = schema.PrimaryKeys[0];
            var primaryKeyColumn = schema.Columns.FirstOrDefault(column => string.Equals(column.Name, primaryKey, StringComparison.OrdinalIgnoreCase));
            if (primaryKeyColumn?.IsAutoGenerated == true && !keyValues.ContainsKey(primaryKey))
            {
                await using var keyCommand = connection.CreateCommand();
                keyCommand.Transaction = transaction;
                keyCommand.CommandText = "SELECT LAST_INSERT_ID();";
                keyValues[primaryKey] = NormalizeDbNull(await keyCommand.ExecuteScalarAsync());
            }
        }

        return keyValues;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>按索引化参数名（_k0, _k1...）添加主键 WHERE 参数，与 SqlDialect.BuildKeyWhereClause 匹配。</summary>
    private static void AddKeyParameters(DbCommand command, IReadOnlyList<string> keyColumns, IReadOnlyDictionary<string, object?> keyValues)
    {
        var (_, parameterNames) = SqlDialect.BuildKeyWhereClause(default, keyColumns);
        for (var index = 0; index < keyColumns.Count; index++)
        {
            AddParameter(command, parameterNames[index], keyValues[keyColumns[index]]);
        }
    }
}