using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DatabaseViewer.Api.Contracts;
using DatabaseViewer.Core.Models;
using DatabaseViewer.Core.Services;

namespace DatabaseViewer.Api.Services;

public sealed class ExplorerApiService
{
    private readonly ConnectionStore _connectionStore;
    private readonly DatabaseMetadataService _metadataService;
    private readonly DatabaseQueryService _queryService;
    private readonly Dictionary<string, TableSchema> _schemaCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<DbTableInfo>> _tablesCache = new(StringComparer.OrdinalIgnoreCase);

    public ExplorerApiService(ConnectionStore connectionStore, DatabaseMetadataService metadataService, DatabaseQueryService queryService)
    {
        _connectionStore = connectionStore;
        _metadataService = metadataService;
        _queryService = queryService;
    }

    public async Task<BootstrapResponse> GetBootstrapAsync()
    {
        var connections = await _connectionStore.LoadAsync();
        var result = new List<ConnectionNodeDto>();

        foreach (var connection in connections)
        {
            try
            {
                var databases = await _metadataService.GetDatabasesAsync(connection);
                var databaseNodes = new List<DatabaseNodeDto>();
                foreach (var database in databases)
                {
                    var tables = await _metadataService.GetTablesAsync(connection, database);
                    _tablesCache[BuildTablesCacheKey(connection.Id, database)] = tables;
                    databaseNodes.Add(new DatabaseNodeDto(
                        database,
                        tables.Select(table => new TableNodeDto(
                            BuildTableKey(connection.Id, table),
                            table.DatabaseName,
                            table.SchemaName,
                            table.TableName,
                            table.Comment,
                            table.RowCount)).ToArray()));
                }

                result.Add(new ConnectionNodeDto(
                    connection.Id,
                    connection.Name,
                    ToProviderKey(connection.ProviderType),
                    connection.Host,
                    connection.Port > 0 ? connection.Port : null,
                    connection.AuthenticationMode == AuthenticationMode.WindowsIntegrated ? "windows" : "password",
                    PickAccent(connection.ProviderType),
                    null,
                    databaseNodes));
            }
            catch (Exception ex)
            {
                result.Add(new ConnectionNodeDto(
                    connection.Id,
                    connection.Name,
                    ToProviderKey(connection.ProviderType),
                    connection.Host,
                    connection.Port > 0 ? connection.Port : null,
                    connection.AuthenticationMode == AuthenticationMode.WindowsIntegrated ? "windows" : "password",
                    PickAccent(connection.ProviderType),
                    ex.Message,
                    Array.Empty<DatabaseNodeDto>()));
            }
        }

        return new BootstrapResponse(result);
    }

    public async Task<DatabaseGraphResponse> GetDatabaseGraphAsync(Guid connectionId, string database)
    {
        var connection = (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == connectionId)
            ?? throw new InvalidOperationException("Connection not found.");
        var tables = await EnsureTablesAsync(connection, database);

        var schemas = await Task.WhenAll(tables.Select(async table => await GetSchemaAsync(connection, table)));
        var nodes = schemas.Select(schema => new DatabaseGraphNodeDto(
            BuildTableKey(connectionId, schema.Table),
            schema.Table.DisplayName,
            schema.Table.RowCount,
            IsJunctionTable(schema),
            schema.Table.Comment,
            schema.Columns.Select(column => new DatabaseGraphColumnDto(
                column.Name,
                column.DataType,
                BuildGraphDisplayType(column),
                column.IsNullable,
                schema.PrimaryKeys.Contains(column.Name, StringComparer.OrdinalIgnoreCase),
                column.ForeignKey is not null)).ToArray())).ToArray();

        var directEdges = schemas
            .SelectMany(schema => schema.Columns
                .Where(column => column.ForeignKey is not null)
                .Select(column => CreateDirectEdge(connectionId, schema, column)))
            .DistinctBy(edge => $"{edge.SourceTableKey}|{edge.TargetTableKey}|{edge.SourceColumn}|{edge.TargetColumn}|{edge.RelationType}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        var syntheticEdges = schemas
            .Where(IsJunctionTable)
            .SelectMany(schema => CreateSyntheticEdges(connectionId, schema))
            .DistinctBy(edge => $"{edge.SourceTableKey}|{edge.TargetTableKey}|{edge.RelationType}|{edge.ViaTableKey}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DatabaseGraphResponse(connectionId, database, nodes, directEdges.Concat(syntheticEdges).ToArray());
    }

    public async Task<ConnectionNodeDto> CreateConnectionAsync(CreateConnectionRequest request)
    {
        var definition = MapConnectionRequest(request);
        var canConnect = await _queryService.TestConnectionAsync(definition);
        if (!canConnect)
        {
            throw BuildConnectionFailureException(definition);
        }

        var connections = (await _connectionStore.LoadAsync()).ToList();
        connections.Add(definition);
        await _connectionStore.SaveAsync(connections);

        return new ConnectionNodeDto(
            definition.Id,
            definition.Name,
            ToProviderKey(definition.ProviderType),
            definition.Host,
            definition.Port > 0 ? definition.Port : null,
            definition.AuthenticationMode == AuthenticationMode.WindowsIntegrated ? "windows" : "password",
            PickAccent(definition.ProviderType),
            null,
            Array.Empty<DatabaseNodeDto>());
    }

    public async Task<ConnectionConfigResponse> GetConnectionConfigAsync(Guid connectionId)
    {
        var connection = (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == connectionId)
            ?? throw new InvalidOperationException("Connection not found.");

        return new ConnectionConfigResponse(
            connection.Id,
            connection.Name,
            ToProviderKey(connection.ProviderType),
            connection.AuthenticationMode == AuthenticationMode.WindowsIntegrated ? "windows" : "password",
            connection.Host,
            connection.Port > 0 ? connection.Port : null,
            string.IsNullOrWhiteSpace(connection.Username) ? null : connection.Username,
            connection.TrustServerCertificate);
    }

    public async Task TestConnectionAsync(TestConnectionRequest request)
    {
        var existing = request.ConnectionId.HasValue
            ? (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == request.ConnectionId.Value)
            : null;
        var definition = MapConnectionRequest(new CreateConnectionRequest(
            request.Name,
            request.Provider,
            request.Authentication,
            request.Host,
            request.Port,
            request.Username,
            request.Password,
            request.TrustServerCertificate), existing);

        var canConnect = await _queryService.TestConnectionAsync(definition);
        if (!canConnect)
        {
            throw BuildConnectionFailureException(definition);
        }
    }

    public async Task<ConnectionNodeDto> UpdateConnectionAsync(Guid connectionId, CreateConnectionRequest request)
    {
        var connections = (await _connectionStore.LoadAsync()).ToList();
        var existingIndex = connections.FindIndex(item => item.Id == connectionId);
        if (existingIndex < 0)
        {
            throw new InvalidOperationException("Connection not found.");
        }

        var updated = MapConnectionRequest(request, connections[existingIndex]);
        updated.Id = connectionId;

        var canConnect = await _queryService.TestConnectionAsync(updated);
        if (!canConnect)
        {
            throw BuildConnectionFailureException(updated);
        }

        connections[existingIndex] = updated;
        await _connectionStore.SaveAsync(connections);

        foreach (var cacheKey in _schemaCache.Keys.Where(key => key.StartsWith($"{connectionId}:", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _schemaCache.Remove(cacheKey);
        }

        foreach (var cacheKey in _tablesCache.Keys.Where(key => key.StartsWith($"{connectionId}:", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _tablesCache.Remove(cacheKey);
        }

        return new ConnectionNodeDto(
            updated.Id,
            updated.Name,
            ToProviderKey(updated.ProviderType),
            updated.Host,
            updated.Port > 0 ? updated.Port : null,
            updated.AuthenticationMode == AuthenticationMode.WindowsIntegrated ? "windows" : "password",
            PickAccent(updated.ProviderType),
            null,
            Array.Empty<DatabaseNodeDto>());
    }

    public async Task<TablePageResponse> GetTablePageAsync(string tableKey, int offset, int pageSize, string? sortColumn, string? sortDirection)
    {
        var context = await ResolveContextAsync(tableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var resolvedSort = ResolveSort(schema, sortColumn, sortDirection);
        var page = await _queryService.GetTablePageAsync(context.Connection, schema, offset, pageSize, resolvedSort.Column, resolvedSort.Descending);

        return new TablePageResponse(
            tableKey,
            context.Table.DatabaseName,
            context.Table.SchemaName,
            context.Table.TableName,
            context.Table.Comment,
            context.Table.RowCount,
            schema.PrimaryKeys,
            schema.Columns.Select(MapColumn).ToArray(),
            schema.Columns.Where(column => column.ForeignKey is not null).Select(column => new ForeignKeyDto(
                column.Name,
                BuildTableKey(context.Connection.Id, ResolveTargetTable(context.Table.DatabaseName, column.ForeignKey!)),
                column.ForeignKey!.TargetColumn,
                !IsPhysicalForeignKey(column.ForeignKey!))).ToArray(),
            page.Data.Rows.Cast<DataRow>().Select(row => MapRow(schema, row)).ToArray(),
            page.HasMoreRows);
    }

    public async Task<TableSearchResponse> SearchTableAsync(string tableKey, string query, string[]? columns, int offset, int pageSize, string? sortColumn, string? sortDirection)
    {
        var context = await ResolveContextAsync(tableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var searchableColumns = ResolveSearchableColumns(schema, columns);
        var resolvedSort = ResolveSort(schema, sortColumn, sortDirection);
        var result = await _queryService.SearchTableAsync(context.Connection, schema, searchableColumns, query, offset, pageSize, resolvedSort.Column, resolvedSort.Descending);

        return new TableSearchResponse(
            tableKey,
            query,
            searchableColumns,
            result.Data.Rows.Cast<DataRow>().Select(row => MapRow(schema, row)).ToArray(),
            result.TotalMatches,
            result.HasMoreRows);
    }

    public async Task<SqlContextResponse> GetSqlContextAsync(Guid connectionId, string database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("Database is required.");
        }

        var connection = (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == connectionId)
            ?? throw new InvalidOperationException("Connection not found.");
        var tables = await EnsureTablesAsync(connection, database);
        var schemas = await Task.WhenAll(tables.Select(async table => await GetSchemaAsync(connection, table)));

        return new SqlContextResponse(
            connectionId,
            database,
            ToProviderKey(connection.ProviderType),
            schemas.Select(schema => new SqlContextTableDto(
                schema.Table.TableName,
                schema.Table.SchemaName,
                schema.Table.DisplayName,
                schema.Columns.Select(column => column.Name).ToArray())).ToArray());
    }

    public async Task<CellContentResponse> GetCellContentAsync(string tableKey, string rowKey, string columnName)
    {
        var context = await ResolveContextAsync(tableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var column = schema.Columns.FirstOrDefault(entry => string.Equals(entry.Name, columnName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Column not found.");

        var record = await _queryService.GetRecordAsync(context.Connection, schema, DecodeRowKey(rowKey))
            ?? throw new InvalidOperationException("Record not found.");

        var rawValue = DatabaseQueryService.NormalizeDbNull(record.Row[column.Name]);
        if (rawValue is null)
        {
            return new CellContentResponse(tableKey, rowKey, column.Name, "empty", "application/octet-stream", BuildSuggestedFileName(context.Table.TableName, column.Name, ".bin"), 0, null, null);
        }

        if (rawValue is byte[] bytes)
        {
          var (kind, mimeType, textContent, extension) = DetectBinaryContent(bytes);
          return new CellContentResponse(
              tableKey,
              rowKey,
              column.Name,
              kind,
              mimeType,
              BuildSuggestedFileName(context.Table.TableName, column.Name, extension),
              bytes.Length,
              textContent,
              Convert.ToBase64String(bytes));
        }

        var stringValue = Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty;
        var encoded = Encoding.UTF8.GetBytes(stringValue);
        return new CellContentResponse(
            tableKey,
            rowKey,
            column.Name,
            "text",
            "text/plain; charset=utf-8",
            BuildSuggestedFileName(context.Table.TableName, column.Name, ".txt"),
            encoded.Length,
            stringValue,
            Convert.ToBase64String(encoded));
    }

    public async Task<RecordResponse> GetRecordAsync(string tableKey, string rowKey)
    {
        var context = await ResolveContextAsync(tableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var keyValues = DecodeRowKey(rowKey);
        var record = await _queryService.GetRecordAsync(context.Connection, schema, keyValues)
            ?? throw new InvalidOperationException("Record not found.");

        var reverseReferences = new List<ReverseReferenceGroupDto>();
        foreach (var incoming in schema.IncomingForeignKeys)
        {
            var sourceTable = new DbTableInfo
            {
                DatabaseName = incoming.SourceDatabase,
                SchemaName = incoming.SourceSchema,
                TableName = incoming.SourceTable,
            };

            var sourceSchema = await GetSchemaAsync(context.Connection, sourceTable);
            var preview = await _queryService.GetReferencingRecordsPreviewAsync(
                context.Connection,
                sourceSchema,
                incoming.SourceColumn,
                DatabaseQueryService.NormalizeDbNull(record.Row[incoming.TargetColumn]),
                6);

            if (preview.Data.Rows.Count == 0)
            {
                continue;
            }

            reverseReferences.Add(new ReverseReferenceGroupDto(
                BuildTableKey(context.Connection.Id, sourceTable),
                $"{sourceTable.DisplayName} 通过 {incoming.SourceColumn} 引用当前记录",
                preview.Data.Rows.Cast<DataRow>().Select(row => new ReverseReferenceRowDto(
                    EncodeRowKey(DatabaseQueryService.ExtractPrimaryKeyValues(sourceSchema, row)),
                    MapRow(sourceSchema, row))).ToArray()));
        }

        return new RecordResponse(
            tableKey,
            rowKey,
            MapRow(schema, record.Row),
            schema.PrimaryKeys,
            schema.Columns.Select(MapColumn).ToArray(),
            schema.Columns.Where(column => column.ForeignKey is not null).Select(column => new ForeignKeyDto(
                column.Name,
                BuildTableKey(context.Connection.Id, ResolveTargetTable(context.Table.DatabaseName, column.ForeignKey!)),
                column.ForeignKey!.TargetColumn,
                !IsPhysicalForeignKey(column.ForeignKey!))).ToArray(),
            reverseReferences);
    }

    public async Task<ForeignKeyTargetResponse?> ResolveForeignKeyAsync(string tableKey, string rowKey, string columnName)
    {
        var context = await ResolveContextAsync(tableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var foreignKey = schema.Columns.FirstOrDefault(column => string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase))?.ForeignKey;
        if (foreignKey is null)
        {
            return null;
        }

        var sourceRecord = await _queryService.GetRecordAsync(context.Connection, schema, DecodeRowKey(rowKey));
        if (sourceRecord is null)
        {
            return null;
        }

        var targetTable = ResolveTargetTable(context.Table.DatabaseName, foreignKey);
        var targetSchema = await GetSchemaAsync(context.Connection, targetTable);
        var targetRecord = await _queryService.GetRecordAsync(
            context.Connection,
            targetSchema,
            new Dictionary<string, object?>
            {
                [foreignKey.TargetColumn] = DatabaseQueryService.NormalizeDbNull(sourceRecord.Row[columnName]),
            });

        if (targetRecord is null)
        {
            return null;
        }

        return new ForeignKeyTargetResponse(
            BuildTableKey(context.Connection.Id, targetTable),
            EncodeRowKey(DatabaseQueryService.ExtractPrimaryKeyValues(targetSchema, targetRecord.Row)),
            $"{columnName} → {foreignKey.TargetColumn}");
    }

    public async Task<SqlExecutionResponse> ExecuteSqlAsync(SqlExecutionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Database))
        {
            throw new InvalidOperationException("Database is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            throw new InvalidOperationException("SQL is required.");
        }

        var connection = (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == request.ConnectionId)
            ?? throw new InvalidOperationException("Connection not found.");

        var result = await _queryService.ExecuteSqlAsync(connection, request.Database, request.Sql);

        return new SqlExecutionResponse(
            request.ConnectionId,
            request.Database,
            result.ExecutedSql,
            result.AffectedRows,
            result.ElapsedMs,
            result.ResultSets.Select(set => new SqlResultSetDto(
                set.Name,
                set.Columns.Select(column => new SqlResultColumnDto(column.Name, column.Type)).ToArray(),
                set.Rows.ToArray(),
                set.RowCount,
                set.Truncated)).ToArray());
    }

    public async Task<TableCellUpdateResponse> UpdateTableCellAsync(TableCellUpdateRequest request)
    {
        var context = await ResolveContextAsync(request.TableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var keyValues = DecodeRowKey(request.RowKey);
        var column = schema.Columns.FirstOrDefault(entry => string.Equals(entry.Name, request.ColumnName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Column not found.");

        if (column.IsAutoGenerated || column.IsComputed || IsReadOnlySystemColumn(column))
        {
            throw new InvalidOperationException($"Column {column.Name} is read-only and cannot be edited.");
        }

        var parsedValue = ConvertWriteValue(column, request.TextValue, request.Base64Value, request.SetNull);

        try
        {
            var updated = await _queryService.UpdateCellAsync(context.Connection, schema, keyValues, column.Name, parsedValue);
            return new TableCellUpdateResponse(
                request.TableKey,
                request.RowKey,
                EncodeRowKey(DatabaseQueryService.ExtractPrimaryKeyValues(schema, updated.Row)),
                MapRow(schema, updated.Row));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbException ex)
        {
            throw new InvalidOperationException(BuildFriendlyWriteErrorMessage(ex, column));
        }
    }

    public async Task<TableRowInsertResponse> InsertTableRowAsync(TableRowInsertRequest request)
    {
        var context = await ResolveContextAsync(request.TableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        ColumnSchema? firstColumn = null;

        foreach (var value in request.Values)
        {
            if (string.IsNullOrWhiteSpace(value.ColumnName))
            {
                continue;
            }

            var column = schema.Columns.FirstOrDefault(entry => string.Equals(entry.Name, value.ColumnName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Column {value.ColumnName} not found.");

            if (column.IsAutoGenerated || column.IsComputed || IsReadOnlySystemColumn(column))
            {
                throw new InvalidOperationException($"Column {column.Name} is read-only and cannot be written when inserting a row.");
            }

            firstColumn ??= column;
            values[column.Name] = ConvertWriteValue(column, value.TextValue, value.Base64Value, value.SetNull);
        }

        if (values.Count == 0)
        {
            throw new InvalidOperationException("请至少填写一个字段后再新增数据。", null);
        }

        try
        {
            var inserted = await _queryService.InsertRowAsync(context.Connection, schema, values);
            return new TableRowInsertResponse(
                request.TableKey,
                inserted is null ? null : EncodeRowKey(DatabaseQueryService.ExtractPrimaryKeyValues(schema, inserted.Row)),
                inserted is null ? null : MapRow(schema, inserted.Row));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbException ex)
        {
            throw new InvalidOperationException(BuildFriendlyWriteErrorMessage(ex, firstColumn ?? schema.Columns.First()));
        }
    }

    public async Task DeleteConnectionAsync(Guid connectionId)
    {
        var remaining = (await _connectionStore.LoadAsync()).Where(connection => connection.Id != connectionId).ToArray();
        await _connectionStore.SaveAsync(remaining);
    }

    private static ConnectionDefinition MapConnectionRequest(CreateConnectionRequest request, ConnectionDefinition? existing = null)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("连接名称不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Host))
        {
            throw new InvalidOperationException("主机地址不能为空。");
        }

        var providerType = request.Provider.ToLowerInvariant() switch
        {
            "sqlserver" => DatabaseProviderType.SqlServer,
            "mysql" => DatabaseProviderType.MySql,
            "postgresql" => DatabaseProviderType.PostgreSql,
            "sqlite" => DatabaseProviderType.Sqlite,
            _ => throw new InvalidOperationException("不支持的数据库类型。"),
        };

        var authenticationMode = request.Authentication.ToLowerInvariant() switch
        {
            "windows" when providerType == DatabaseProviderType.SqlServer => AuthenticationMode.WindowsIntegrated,
            "password" => AuthenticationMode.UsernamePassword,
            "windows" => throw new InvalidOperationException("只有 SQL Server 支持 Windows 身份验证。"),
            _ => throw new InvalidOperationException("不支持的认证方式。"),
        };

        var resolvedUsername = authenticationMode == AuthenticationMode.WindowsIntegrated || providerType == DatabaseProviderType.Sqlite
            ? string.Empty
            : !string.IsNullOrWhiteSpace(request.Username)
              ? request.Username.Trim()
              : existing?.Username ?? throw new InvalidOperationException("用户名不能为空。");

        var resolvedPassword = authenticationMode == AuthenticationMode.WindowsIntegrated || providerType == DatabaseProviderType.Sqlite
            ? string.Empty
            : request.Password ?? existing?.Password ?? string.Empty;

        return new ConnectionDefinition
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Name = request.Name.Trim(),
            ProviderType = providerType,
            AuthenticationMode = authenticationMode,
            Host = request.Host.Trim(),
            Port = request.Port ?? GetDefaultPort(providerType),
            Username = resolvedUsername,
            Password = resolvedPassword,
            TrustServerCertificate = request.TrustServerCertificate,
        };
    }

    private static InvalidOperationException BuildConnectionFailureException(ConnectionDefinition definition)
    {
        if (definition.ProviderType == DatabaseProviderType.Sqlite)
        {
            return new InvalidOperationException("SQLite 连接失败，请检查数据库文件路径以及目录读写权限。新数据库文件会在首次连接时自动创建。", null);
        }

        return new InvalidOperationException("连接失败，请检查地址、账号、密码和认证方式。\n如果是 SQL Server 本地实例，可尝试 .\\SQLEXPRESS 或 localhost。\n如果使用 Windows 身份验证，请将认证方式切换为 Windows。");
    }

    private static string ToProviderKey(DatabaseProviderType providerType) => providerType switch
    {
        DatabaseProviderType.SqlServer => "sqlserver",
        DatabaseProviderType.MySql => "mysql",
        DatabaseProviderType.PostgreSql => "postgresql",
        DatabaseProviderType.Sqlite => "sqlite",
        _ => "sqlserver",
    };

    private static int GetDefaultPort(DatabaseProviderType providerType) => providerType switch
    {
        DatabaseProviderType.SqlServer => 1433,
        DatabaseProviderType.MySql => 3306,
        DatabaseProviderType.PostgreSql => 5432,
        DatabaseProviderType.Sqlite => 0,
        _ => 0,
    };

    private static string PickAccent(DatabaseProviderType providerType) => providerType switch
    {
        DatabaseProviderType.SqlServer => "#0f766e",
        DatabaseProviderType.MySql => "#7c3aed",
        DatabaseProviderType.PostgreSql => "#2563eb",
        DatabaseProviderType.Sqlite => "#15803d",
        _ => "#0f766e",
    };

    private static ColumnDto MapColumn(ColumnSchema column) => new(
        column.Name,
        column.DataType,
        column.IsPrimaryKey,
        column.IsNullable,
        column.IsAutoGenerated,
        column.IsComputed,
        column.MaxLength);

    private static string BuildGraphDisplayType(ColumnSchema column)
    {
        if (column.MaxLength is null)
        {
            return column.DataType;
        }

        if (column.MaxLength.Value < 0)
        {
            return $"{column.DataType}(max)";
        }

        return $"{column.DataType}({column.MaxLength.Value})";
    }

    private static IReadOnlyList<string> ResolveSearchableColumns(TableSchema schema, string[]? columns)
    {
        var requested = (columns ?? Array.Empty<string>()).Where(column => !string.IsNullOrWhiteSpace(column)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = schema.Columns
            .Where(column => !IsBinaryDataType(column.DataType))
            .Where(column => requested.Count == 0 || requested.Contains(column.Name))
            .Select(column => column.Name)
            .ToArray();

        return candidates;
    }

    private static bool IsBinaryDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty).ToLowerInvariant();
        return normalized.Contains("binary")
            || normalized.Contains("image")
            || normalized.Contains("blob");
    }

    private static bool IsStringDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty).ToLowerInvariant();
        return normalized.Contains("char")
            || normalized.Contains("text")
            || normalized.Contains("xml")
            || normalized.Contains("json")
            || normalized.Contains("enum")
            || normalized.Contains("set")
            || normalized.Contains("uuid");
    }

    private static bool IsBooleanDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty).ToLowerInvariant();
        return normalized is "bit" or "bool" or "boolean";
    }

    private static bool IsIntegerDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty).ToLowerInvariant();
        return normalized is "tinyint" or "smallint" or "int" or "integer" or "bigint" or "mediumint";
    }

    private static bool IsDecimalDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty).ToLowerInvariant();
        return normalized is "decimal" or "numeric" or "money" or "smallmoney";
    }

    private static bool IsFloatingPointDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty).ToLowerInvariant();
        return normalized is "float" or "real" or "double" or "double precision";
    }

    private static bool IsGuidDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty).ToLowerInvariant();
        return normalized is "uniqueidentifier" or "uuid";
    }

    private static bool IsDateTimeOffsetDataType(string? dataType) => string.Equals(dataType, "datetimeoffset", StringComparison.OrdinalIgnoreCase);

    private static bool IsDateDataType(string? dataType) => string.Equals(dataType, "date", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimeDataType(string? dataType) => string.Equals(dataType, "time", StringComparison.OrdinalIgnoreCase);

    private static bool IsDateTimeDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty).ToLowerInvariant();
        return normalized is "datetime" or "datetime2" or "smalldatetime" or "timestamp";
    }

    private static bool IsReadOnlySystemColumn(ColumnSchema column)
    {
        var normalized = column.DataType.ToLowerInvariant();
        return normalized is "timestamp" or "rowversion";
    }

    private static object? ConvertWriteValue(ColumnSchema column, string? textValue, string? base64Value, bool setNull)
    {
        if (setNull)
        {
            if (!column.IsNullable)
            {
                throw new InvalidOperationException($"Column {column.Name} does not allow NULL values.");
            }

            return null;
        }

        if (IsBinaryDataType(column.DataType))
        {
            if (string.IsNullOrWhiteSpace(base64Value))
            {
                return Array.Empty<byte>();
            }

            try
            {
                return Convert.FromBase64String(base64Value);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"Column {column.Name} expects valid binary content.");
            }
        }

        var text = textValue ?? string.Empty;
        if (IsStringDataType(column.DataType))
        {
            return text;
        }

        if (IsBooleanDataType(column.DataType))
        {
            if (bool.TryParse(text, out var boolValue))
            {
                return boolValue;
            }

            if (text is "1" or "yes" or "y")
            {
                return true;
            }

            if (text is "0" or "no" or "n")
            {
                return false;
            }

            throw new InvalidOperationException($"Column {column.Name} expects a boolean value such as true/false or 1/0.");
        }

        if (IsIntegerDataType(column.DataType))
        {
            try
            {
                return column.DataType.ToLowerInvariant() switch
                {
                    "tinyint" => byte.Parse(text, CultureInfo.InvariantCulture),
                    "smallint" => short.Parse(text, CultureInfo.InvariantCulture),
                    "bigint" => long.Parse(text, CultureInfo.InvariantCulture),
                    _ => int.Parse(text, CultureInfo.InvariantCulture),
                };
            }
            catch (Exception)
            {
                throw new InvalidOperationException($"Column {column.Name} expects an integer value.");
            }
        }

        if (IsDecimalDataType(column.DataType))
        {
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
            {
                throw new InvalidOperationException($"Column {column.Name} expects a decimal value.");
            }

            return decimalValue;
        }

        if (IsFloatingPointDataType(column.DataType))
        {
            if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
            {
                throw new InvalidOperationException($"Column {column.Name} expects a numeric value.");
            }

            return doubleValue;
        }

        if (IsGuidDataType(column.DataType))
        {
            if (!Guid.TryParse(text, out var guidValue))
            {
                throw new InvalidOperationException($"Column {column.Name} expects a GUID value.");
            }

            return guidValue;
        }

        if (IsDateTimeOffsetDataType(column.DataType))
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dtoValue))
            {
                throw new InvalidOperationException($"Column {column.Name} expects a datetimeoffset value.");
            }

            return dtoValue;
        }

        if (IsDateDataType(column.DataType))
        {
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue))
            {
                throw new InvalidOperationException($"Column {column.Name} expects a date value.");
            }

            return dateValue.Date;
        }

        if (IsTimeDataType(column.DataType))
        {
            if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var timeValue))
            {
                throw new InvalidOperationException($"Column {column.Name} expects a time value.");
            }

            return timeValue;
        }

        if (IsDateTimeDataType(column.DataType))
        {
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTimeValue))
            {
                throw new InvalidOperationException($"Column {column.Name} expects a datetime value.");
            }

            return dateTimeValue;
        }

        return text;
    }

    private static string BuildFriendlyWriteErrorMessage(DbException exception, ColumnSchema column)
    {
        var message = exception.Message;
        if (message.Contains("REFERENCE constraint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("foreign key constraint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("foreign key", StringComparison.OrdinalIgnoreCase))
        {
            return $"Updating column {column.Name} violated a foreign key relationship. The new value does not exist in the target table, or this row is still referenced elsewhere.";
        }

        if (message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique", StringComparison.OrdinalIgnoreCase)
            || message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
        {
            return $"Updating column {column.Name} would create a duplicate primary key or unique value.";
        }

        if (message.Contains("cannot insert the value NULL", StringComparison.OrdinalIgnoreCase)
            || message.Contains("doesn't have a default value", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot be null", StringComparison.OrdinalIgnoreCase))
        {
            return $"Column {column.Name} does not allow empty or NULL values.";
        }

        return message;
    }

    private static (string? Column, bool Descending) ResolveSort(TableSchema schema, string? sortColumn, string? sortDirection)
    {
        if (string.IsNullOrWhiteSpace(sortColumn))
        {
            return (null, false);
        }

        var column = schema.Columns.FirstOrDefault(entry => string.Equals(entry.Name, sortColumn, StringComparison.OrdinalIgnoreCase));
        if (column is null || IsBinaryDataType(column.DataType))
        {
            return (null, false);
        }

        return (column.Name, string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase));
    }

    private static DatabaseGraphEdgeDto CreateDirectEdge(Guid connectionId, TableSchema schema, ColumnSchema column)
    {
        var foreignKey = column.ForeignKey!;
        var sourceTableKey = BuildTableKey(connectionId, schema.Table);
        var targetTable = ResolveTargetTable(schema.Table.DatabaseName, foreignKey);
        var targetTableKey = BuildTableKey(connectionId, targetTable);
        var sourceIsUnique = schema.PrimaryKeys.Contains(column.Name, StringComparer.OrdinalIgnoreCase);
        var relationType = sourceIsUnique ? "one-to-one" : "many-to-one";
        var relationLabel = sourceIsUnique ? "1:1" : "N:1";

        return new DatabaseGraphEdgeDto(
            sourceTableKey,
            targetTableKey,
            relationType,
            $"{relationLabel} · {column.Name} -> {foreignKey.TargetColumn}",
            !IsPhysicalForeignKey(foreignKey),
            null,
            column.Name,
            foreignKey.TargetColumn);
    }

    private static IEnumerable<DatabaseGraphEdgeDto> CreateSyntheticEdges(Guid connectionId, TableSchema schema)
    {
        var foreignKeys = schema.Columns.Where(column => column.ForeignKey is not null).Select(column => column.ForeignKey!).ToArray();
        if (foreignKeys.Length != 2)
        {
            yield break;
        }

        var left = foreignKeys[0];
        var right = foreignKeys[1];
        if (string.Equals(left.TargetTable, right.TargetTable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.TargetSchema ?? string.Empty, right.TargetSchema ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        yield return new DatabaseGraphEdgeDto(
            BuildTableKey(connectionId, ResolveTargetTable(schema.Table.DatabaseName, left)),
            BuildTableKey(connectionId, ResolveTargetTable(schema.Table.DatabaseName, right)),
            "many-to-many",
            $"N:N · via {schema.Table.DisplayName}",
            !IsPhysicalForeignKey(left) || !IsPhysicalForeignKey(right),
            BuildTableKey(connectionId, schema.Table),
            left.TargetColumn,
            right.TargetColumn);
    }

    private static bool IsJunctionTable(TableSchema schema)
    {
        var fkColumns = schema.Columns.Where(column => column.ForeignKey is not null).ToArray();
        if (fkColumns.Length != 2)
        {
            return false;
        }

        if (schema.PrimaryKeys.Count == 0)
        {
            return false;
        }

        return schema.PrimaryKeys.All(primaryKey => fkColumns.Any(column => string.Equals(column.Name, primaryKey, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<(ConnectionDefinition Connection, DbTableInfo Table)> ResolveContextAsync(string tableKey)
    {
        var parsed = ParseTableKey(tableKey);
        var connection = (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == parsed.ConnectionId)
            ?? throw new InvalidOperationException("Connection not found.");
        return (connection, new DbTableInfo
        {
            DatabaseName = parsed.Database,
            SchemaName = parsed.Schema,
            TableName = parsed.Table,
        });
    }

    private async Task<TableSchema> GetSchemaAsync(ConnectionDefinition connection, DbTableInfo table)
    {
        var cacheKey = BuildSchemaCacheKey(connection.Id, table);
        if (_schemaCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var tables = await EnsureTablesAsync(connection, table.DatabaseName);
        var resolved = tables.FirstOrDefault(item =>
            string.Equals(item.TableName, table.TableName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.SchemaName ?? string.Empty, table.SchemaName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            ?? table;
        var schema = await _metadataService.GetTableSchemaAsync(connection, resolved, tables);
        _schemaCache[cacheKey] = schema;
        return schema;
    }

    private async Task<IReadOnlyList<DbTableInfo>> EnsureTablesAsync(ConnectionDefinition connection, string database)
    {
        var cacheKey = BuildTablesCacheKey(connection.Id, database);
        if (_tablesCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var tables = await _metadataService.GetTablesAsync(connection, database);
        _tablesCache[cacheKey] = tables;
        return tables;
    }

    private static DbTableInfo ResolveTargetTable(string fallbackDatabase, ForeignKeyReference foreignKey) => new()
    {
        DatabaseName = string.IsNullOrWhiteSpace(foreignKey.TargetDatabase) ? fallbackDatabase : foreignKey.TargetDatabase,
        SchemaName = foreignKey.TargetSchema,
        TableName = foreignKey.TargetTable,
    };

    private static bool IsPhysicalForeignKey(ForeignKeyReference foreignKey) =>
        !string.Equals(foreignKey.TargetColumn, "id", StringComparison.OrdinalIgnoreCase)
        || !foreignKey.SourceColumn.EndsWith("_id", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, object?> MapRow(TableSchema schema, DataRow row)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["rowKey"] = EncodeRowKey(DatabaseQueryService.ExtractPrimaryKeyValues(schema, row)),
        };

        foreach (var column in schema.Columns)
        {
            payload[column.Name] = DatabaseQueryService.NormalizeDbNull(row[column.Name]);
        }

        return payload;
    }

    private static string EncodeRowKey(IReadOnlyDictionary<string, object?> keyValues) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(keyValues)));

    private static Dictionary<string, object?> DecodeRowKey(string rowKey)
    {
        if (string.IsNullOrWhiteSpace(rowKey))
        {
            return new Dictionary<string, object?>();
        }

        var decoded = JsonSerializer.Deserialize<Dictionary<string, object?>>(Encoding.UTF8.GetString(Convert.FromBase64String(rowKey)))
            ?? new Dictionary<string, object?>();

        return decoded.ToDictionary(pair => pair.Key, pair => NormalizeDecodedKeyValue(pair.Value), StringComparer.OrdinalIgnoreCase);
    }

    private static object? NormalizeDecodedKeyValue(object? value)
    {
        if (value is not JsonElement element)
        {
            return value;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            _ => element.GetRawText(),
        };
    }

    private static (Guid ConnectionId, string Database, string? Schema, string Table) ParseTableKey(string tableKey)
    {
        var segments = tableKey.Split("::", StringSplitOptions.None);
        if (segments.Length != 4)
        {
            throw new InvalidOperationException("Invalid table key.");
        }

        return (Guid.Parse(segments[0]), segments[1], string.IsNullOrWhiteSpace(segments[2]) ? null : segments[2], segments[3]);
    }

    private static string BuildTableKey(Guid connectionId, DbTableInfo table) => string.Join("::", connectionId, table.DatabaseName, table.SchemaName ?? string.Empty, table.TableName);

    private static string BuildSchemaCacheKey(Guid connectionId, DbTableInfo table) => $"{connectionId}:{table.QualifiedKey}";

    private static string BuildTablesCacheKey(Guid connectionId, string database) => $"{connectionId}:{database}";

    private static string BuildSuggestedFileName(string tableName, string columnName, string extension)
    {
        var safeTableName = SanitizeFileName(tableName);
        var safeColumnName = SanitizeFileName(columnName);
        return $"{safeTableName}_{safeColumnName}{extension}";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    private static (string Kind, string MimeType, string? TextContent, string Extension) DetectBinaryContent(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return ("empty", "application/octet-stream", null, ".bin");
        }

        if (TryDetectImage(bytes, out var imageMimeType, out var imageExtension))
        {
            return ("image", imageMimeType, null, imageExtension);
        }

        if (TryDecodeText(bytes, out var textContent, out var textMimeType))
        {
            return ("text", textMimeType, textContent, ".txt");
        }

        return ("binary", "application/octet-stream", null, ".bin");
    }

    private static bool TryDetectImage(byte[] bytes, out string mimeType, out string extension)
    {
        mimeType = "application/octet-stream";
        extension = ".bin";

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            mimeType = "image/png";
            extension = ".png";
            return true;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            mimeType = "image/jpeg";
            extension = ".jpg";
            return true;
        }

        if (bytes.Length >= 6 && Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a")
        {
            mimeType = "image/gif";
            extension = ".gif";
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            mimeType = "image/bmp";
            extension = ".bmp";
            return true;
        }

        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP")
        {
            mimeType = "image/webp";
            extension = ".webp";
            return true;
        }

        return false;
    }

    private static bool TryDecodeText(byte[] bytes, out string textContent, out string mimeType)
    {
        textContent = string.Empty;
        mimeType = "text/plain; charset=utf-8";

        foreach (var encoding in CandidateEncodings())
        {
            try
            {
                var decoded = encoding.GetString(bytes);
                if (!LooksLikeText(decoded))
                {
                    continue;
                }

                textContent = decoded;
                mimeType = $"text/plain; charset={encoding.WebName}";
                return true;
            }
            catch
            {
                // Try next encoding.
            }
        }

        return false;
    }

    private static IEnumerable<Encoding> CandidateEncodings()
    {
        yield return new UTF8Encoding(false, true);
        yield return new UnicodeEncoding(false, true, true);
        yield return new UnicodeEncoding(true, true, true);
    }

    private static bool LooksLikeText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        var sample = value.Length > 2048 ? value[..2048] : value;
        var printable = 0;

        foreach (var ch in sample)
        {
            if (ch == '\r' || ch == '\n' || ch == '\t' || !char.IsControl(ch))
            {
                printable++;
            }
        }

        return printable >= sample.Length * 0.9;
    }
}