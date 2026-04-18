using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Antlr4.Runtime;
using DatabaseViewer.Api.Contracts;
using DatabaseViewer.Core.Models;
using DatabaseViewer.Core.Services;
using SQLParser.Parsers.TSql;

namespace DatabaseViewer.Api.Services;

public sealed class ExplorerApiService
{
    private readonly ApplicationSettingsStore _applicationSettingsStore;
    private readonly ConnectionStore _connectionStore;
    private readonly DatabaseMetadataService _metadataService;
    private readonly DatabaseQueryService _queryService;
    private readonly Dictionary<string, TableSchema> _schemaCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<DbTableInfo>> _tablesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<DbTableInfo>> _viewsCache = new(StringComparer.OrdinalIgnoreCase);

    public ExplorerApiService(ApplicationSettingsStore applicationSettingsStore, ConnectionStore connectionStore, DatabaseMetadataService metadataService, DatabaseQueryService queryService)
    {
        _applicationSettingsStore = applicationSettingsStore;
        _connectionStore = connectionStore;
        _metadataService = metadataService;
        _queryService = queryService;
    }

    public async Task<ExplorerSettingsDto> GetSettingsAsync()
    {
        var settings = await _applicationSettingsStore.LoadAsync();
        return new ExplorerSettingsDto(settings.ShowTableRowCounts);
    }

    public async Task<ExplorerSettingsDto> UpdateSettingsAsync(UpdateExplorerSettingsRequest request)
    {
        var settings = new ApplicationSettings
        {
            ShowTableRowCounts = request.ShowTableRowCounts,
            WorkspaceLayout = (await _applicationSettingsStore.LoadAsync()).WorkspaceLayout,
        };

        await _applicationSettingsStore.SaveAsync(settings);
        InvalidateMetadataCaches();
        return new ExplorerSettingsDto(settings.ShowTableRowCounts);
    }

    public async Task<WorkspaceLayoutDto> GetWorkspaceLayoutAsync()
    {
        var settings = await _applicationSettingsStore.LoadAsync();
        return new WorkspaceLayoutDto(settings.WorkspaceLayout.SidebarPaneSize, settings.WorkspaceLayout.DetailPaneSize);
    }

    public async Task<WorkspaceLayoutDto> UpdateWorkspaceLayoutAsync(UpdateWorkspaceLayoutRequest request)
    {
        var settings = await _applicationSettingsStore.LoadAsync();
        settings.WorkspaceLayout = new WorkspaceLayoutSettings
        {
            SidebarPaneSize = request.SidebarPaneSize,
            DetailPaneSize = request.DetailPaneSize,
        };
        await _applicationSettingsStore.SaveAsync(settings);
        return new WorkspaceLayoutDto(settings.WorkspaceLayout.SidebarPaneSize, settings.WorkspaceLayout.DetailPaneSize);
    }

    public async Task<BootstrapResponse> GetBootstrapAsync()
    {
        var connections = await _connectionStore.LoadAsync();
        var result = new List<ConnectionNodeDto>();

        foreach (var connection in connections)
        {
            result.Add(BuildConnectionNode(connection, null, Array.Empty<DatabaseNodeDto>()));
        }

        return new BootstrapResponse(result);
    }

    public async Task<ConnectionNodeDto> ConnectAsync(Guid connectionId)
    {
        var settings = await _applicationSettingsStore.LoadAsync();
        var connection = await GetStoredConnectionAsync(connectionId);
        return await LoadConnectionMetadataAsync(connection, settings);
    }

    private async Task<ConnectionNodeDto> LoadConnectionMetadataAsync(ConnectionDefinition connection, ApplicationSettings settings)
    {
        try
        {
            var databases = await _metadataService.GetDatabasesAsync(connection);
            var databaseNodes = new List<DatabaseNodeDto>();
            foreach (var database in databases)
            {
                    IReadOnlyList<DbTableInfo> tables = Array.Empty<DbTableInfo>();
                    try
                    {
                        tables = await _metadataService.GetTablesAsync(connection, database, includeSystemObjects: false, settings.ShowTableRowCounts);
                        _tablesCache[BuildTablesCacheKey(connection.Id, database)] = tables;
                    }
                    catch
                    {
                        // Keep the database visible even if table metadata cannot be read.
                    }

                    IReadOnlyList<DbTableInfo> views = Array.Empty<DbTableInfo>();
                    try
                    {
                        views = await _metadataService.GetViewsAsync(connection, database);
                        _viewsCache[BuildViewsCacheKey(connection.Id, database)] = views;
                    }
                    catch
                    {
                        // Keep other groups visible even if views fail.
                    }

                    IReadOnlyList<DbSynonymInfo> synonyms = Array.Empty<DbSynonymInfo>();
                    try
                    {
                        synonyms = await _metadataService.GetSynonymsAsync(connection, database);
                    }
                    catch
                    {
                    }

                    IReadOnlyList<DbSequenceInfo> sequences = Array.Empty<DbSequenceInfo>();
                    try
                    {
                        sequences = await _metadataService.GetSequencesAsync(connection, database);
                    }
                    catch
                    {
                    }

                    IReadOnlyList<DbRuleInfo> rules = Array.Empty<DbRuleInfo>();
                    try
                    {
                        rules = await _metadataService.GetRulesAsync(connection, database);
                    }
                    catch
                    {
                    }

                    IReadOnlyList<DbDefaultInfo> defaults = Array.Empty<DbDefaultInfo>();
                    try
                    {
                        defaults = await _metadataService.GetDefaultsAsync(connection, database);
                    }
                    catch
                    {
                    }

                    IReadOnlyList<DbUserDefinedTypeInfo> userDefinedTypes = Array.Empty<DbUserDefinedTypeInfo>();
                    try
                    {
                        userDefinedTypes = await _metadataService.GetUserDefinedTypesAsync(connection, database);
                    }
                    catch
                    {
                    }

                    IReadOnlyList<DbDatabaseTriggerInfo> databaseTriggers = Array.Empty<DbDatabaseTriggerInfo>();
                    try
                    {
                        databaseTriggers = await _metadataService.GetDatabaseTriggersAsync(connection, database);
                    }
                    catch
                    {
                    }

                    IReadOnlyList<DbXmlSchemaCollectionInfo> xmlSchemaCollections = Array.Empty<DbXmlSchemaCollectionInfo>();
                    try
                    {
                        xmlSchemaCollections = await _metadataService.GetXmlSchemaCollectionsAsync(connection, database);
                    }
                    catch
                    {
                    }

                    IReadOnlyList<DbAssemblyInfo> assemblies = Array.Empty<DbAssemblyInfo>();
                    try
                    {
                        assemblies = await _metadataService.GetAssembliesAsync(connection, database);
                    }
                    catch
                    {
                    }

                    var routines = Array.Empty<RoutineNodeDto>();
                    try
                    {
                        var routineInfos = await _metadataService.GetRoutinesAsync(connection, database);
                        routines = routineInfos.Select(r => new RoutineNodeDto(
                            r.SchemaName,
                            r.RoutineName,
                            r.RoutineType,
                            r.Parameters.Select(p => new RoutineParameterDto(p.Name, p.DataType, p.Direction, p.DefaultValue)).ToArray()
                        )).ToArray();
                    }
                    catch
                    {
                        // 函数/存储过程加载失败不影响其它对象加载
                    }

                    databaseNodes.Add(new DatabaseNodeDto(
                        database,
                        tables.Select(table => new TableNodeDto(
                            BuildTableKey(connection.Id, table),
                            table.DatabaseName,
                            table.SchemaName,
                            table.TableName,
                            ToObjectTypeKey(table.ObjectType),
                            table.Comment,
                            table.RowCount)).ToArray(),
                        views.Select(view => new TableNodeDto(
                            BuildTableKey(connection.Id, view),
                            view.DatabaseName,
                            view.SchemaName,
                            view.TableName,
                            ToObjectTypeKey(view.ObjectType),
                            view.Comment,
                            view.RowCount)).ToArray(),
                        synonyms.Select(synonym => new SynonymNodeDto(
                            synonym.DatabaseName,
                            synonym.SchemaName,
                            synonym.SynonymName,
                            synonym.BaseObjectName)).ToArray(),
                        sequences.Select(sequence => new SequenceNodeDto(
                            sequence.DatabaseName,
                            sequence.SchemaName,
                            sequence.SequenceName,
                            sequence.DataType,
                            sequence.StartValue,
                            sequence.IncrementValue)).ToArray(),
                        rules.Select(rule => new RuleNodeDto(
                            rule.DatabaseName,
                            rule.SchemaName,
                            rule.RuleName,
                            rule.Definition)).ToArray(),
                        defaults.Select(item => new DefaultNodeDto(
                            item.DatabaseName,
                            item.SchemaName,
                            item.DefaultName,
                            item.Definition)).ToArray(),
                        userDefinedTypes.Select(item => new UserDefinedTypeNodeDto(
                            item.DatabaseName,
                            item.SchemaName,
                            item.TypeName,
                            item.BaseTypeName,
                            item.IsTableType)).ToArray(),
                        databaseTriggers.Select(item => new DatabaseTriggerNodeDto(
                            item.DatabaseName,
                            item.SchemaName,
                            item.TriggerName,
                            item.TriggerTiming,
                            item.TriggerEvent)).ToArray(),
                        xmlSchemaCollections.Select(item => new XmlSchemaCollectionNodeDto(
                            item.DatabaseName,
                            item.SchemaName,
                            item.CollectionName,
                            item.XmlNamespaceCount)).ToArray(),
                        assemblies.Select(item => new AssemblyNodeDto(
                            item.DatabaseName,
                            item.AssemblyName,
                            item.ClrName,
                            item.PermissionSet,
                            item.IsVisible)).ToArray(),
                        routines));
                }

                return BuildConnectionNode(connection, null, databaseNodes);
            }
            catch (Exception ex)
            {
                return BuildConnectionNode(connection, ex.Message, Array.Empty<DatabaseNodeDto>());
            }
    }

    public async Task<IReadOnlyList<string>> GetDatabaseNamesAsync(Guid connectionId)
    {
        var connection = await GetEffectiveConnectionAsync(connectionId);
        return await _metadataService.GetDatabasesAsync(connection);
    }

    public async Task<IReadOnlyList<string>> GetCollationsAsync(Guid connectionId, string database)
    {
        var connection = await GetEffectiveConnectionAsync(connectionId);
        return await _metadataService.GetCollationsAsync(connection, database);
    }

    public async Task<DatabaseGraphResponse> GetDatabaseGraphAsync(Guid connectionId, string database)
    {
        var connection = await GetEffectiveConnectionAsync(connectionId);
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

        var incomingEdges = schemas
            .SelectMany(schema => schema.IncomingForeignKeys
                .Select(incoming => CreateIncomingEdge(connectionId, schemas, schema, incoming)))
            .ToList();

        var mergedEdges = directEdges.Concat(incomingEdges)
            .DistinctBy(edge => $"{edge.SourceTableKey}|{edge.TargetTableKey}|{edge.SourceColumn}|{edge.TargetColumn}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        var syntheticEdges = schemas
            .Where(IsJunctionTable)
            .SelectMany(schema => CreateSyntheticEdges(connectionId, schema))
            .DistinctBy(edge => $"{edge.SourceTableKey}|{edge.TargetTableKey}|{edge.RelationType}|{edge.ViaTableKey}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DatabaseGraphResponse(connectionId, database, nodes, mergedEdges.Concat(syntheticEdges).ToArray());
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

        return BuildConnectionNode(definition, null, Array.Empty<DatabaseNodeDto>());
    }

    public async Task<ConnectionConfigResponse> GetConnectionConfigAsync(Guid connectionId)
    {
        var connection = await GetStoredConnectionAsync(connectionId);

        return new ConnectionConfigResponse(
            connection.Id,
            connection.Name,
            ToProviderKey(connection.ProviderType),
            connection.Host,
            connection.Port > 0 ? connection.Port : null,
            string.IsNullOrWhiteSpace(connection.Username) ? null : connection.Username,
            !string.IsNullOrWhiteSpace(connection.Password),
            new SqlServerConnectionConfigResponse(
                connection.SqlServer.AuthenticationMode == SqlServerAuthenticationMode.WindowsIntegrated ? "windows" : "password",
                connection.SqlServer.TrustServerCertificate),
            new MySqlConnectionConfigResponse(),
            new PostgreSqlConnectionConfigResponse(),
            new SqliteConnectionConfigResponse(
                ToSqliteOpenModeKey(connection.Sqlite.OpenMode),
                new SqliteCipherConfigResponse(
                    connection.Sqlite.Cipher.Enabled,
                    !string.IsNullOrWhiteSpace(connection.Sqlite.Cipher.Password),
                    ToSqliteCipherKeyFormatKey(connection.Sqlite.Cipher.KeyFormat),
                    connection.Sqlite.Cipher.PageSize,
                    connection.Sqlite.Cipher.KdfIter,
                    connection.Sqlite.Cipher.CipherCompatibility,
                    connection.Sqlite.Cipher.PlaintextHeaderSize,
                    connection.Sqlite.Cipher.SkipBytes,
                    connection.Sqlite.Cipher.UseHmac,
                    string.IsNullOrWhiteSpace(connection.Sqlite.Cipher.KdfAlgorithm) ? null : connection.Sqlite.Cipher.KdfAlgorithm,
                    string.IsNullOrWhiteSpace(connection.Sqlite.Cipher.HmacAlgorithm) ? null : connection.Sqlite.Cipher.HmacAlgorithm)),
            new SshTunnelConfigResponse(
                connection.SshTunnel.Enabled,
                ToSshAuthenticationKey(connection.SshTunnel.AuthenticationMode),
                string.IsNullOrWhiteSpace(connection.SshTunnel.Host) ? null : connection.SshTunnel.Host,
                connection.SshTunnel.Port > 0 ? connection.SshTunnel.Port : null,
                string.IsNullOrWhiteSpace(connection.SshTunnel.Username) ? null : connection.SshTunnel.Username,
                !string.IsNullOrWhiteSpace(connection.SshTunnel.Password),
                !string.IsNullOrWhiteSpace(connection.SshTunnel.Passphrase),
                string.IsNullOrWhiteSpace(connection.SshTunnel.PrivateKeyPath) ? null : connection.SshTunnel.PrivateKeyPath));
    }

    public async Task TestConnectionAsync(TestConnectionRequest request)
    {
        var existing = request.ConnectionId.HasValue
            ? (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == request.ConnectionId.Value)
            : null;
        var definition = MapConnectionRequest(new CreateConnectionRequest(
            request.Name,
            request.Provider,
            request.Host,
            request.Port,
            request.Username,
            request.Password,
            request.SqlServer,
            request.MySql,
            request.PostgreSql,
            request.Sqlite,
            request.SshTunnel), existing);

        var canConnect = await _queryService.TestConnectionAsync(definition);
        if (!canConnect)
        {
            throw BuildConnectionFailureException(definition);
        }
    }

    public async Task RekeySqliteDatabaseAsync(SqliteRekeyRequest request)
    {
        var connections = (await _connectionStore.LoadAsync()).ToList();
        var existingIndex = connections.FindIndex(item => item.Id == request.ConnectionId);
        if (existingIndex < 0)
        {
            throw new InvalidOperationException("Connection not found.");
        }

        var existing = connections[existingIndex];
        if (existing.ProviderType != DatabaseProviderType.Sqlite)
        {
            throw new InvalidOperationException("只有 SQLite 连接支持修改加密密钥。", null);
        }

        if (!existing.Sqlite.Cipher.Enabled)
        {
            throw new InvalidOperationException("当前实现仅支持修改已加密 SQLite 数据库的密钥。明文库加密/解密需要 sqlcipher_export 导出重写，暂未实现。", null);
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            throw new InvalidOperationException("新的 SQLCipher 密码不能为空。", null);
        }

        var currentConnection = CloneConnectionDefinition(existing);
        if (!string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            currentConnection.Sqlite.Cipher = CloneSqliteCipherOptions(existing.Sqlite.Cipher);
            currentConnection.Sqlite.Cipher.Enabled = true;
            currentConnection.Sqlite.Cipher.Password = request.CurrentPassword.Trim();
            currentConnection.Sqlite.Cipher.KeyFormat = ParseSqliteCipherKeyFormat(request.CurrentKeyFormat);
        }

        var updated = CloneConnectionDefinition(existing);
        updated.Sqlite.Cipher = CloneSqliteCipherOptions(existing.Sqlite.Cipher);
        updated.Sqlite.Cipher.Enabled = true;
        updated.Sqlite.Cipher.Password = request.NewPassword.Trim();
        updated.Sqlite.Cipher.KeyFormat = ParseSqliteCipherKeyFormat(request.NewKeyFormat);

        await _queryService.RekeySqliteDatabaseAsync(currentConnection, updated.Sqlite.Cipher);

        connections[existingIndex] = updated;
        await _connectionStore.SaveAsync(connections);
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

        foreach (var cacheKey in _viewsCache.Keys.Where(key => key.StartsWith($"{connectionId}:", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _viewsCache.Remove(cacheKey);
        }

        return BuildConnectionNode(updated, null, Array.Empty<DatabaseNodeDto>());
    }

    public async Task<TablePageResponse> GetTablePageAsync(string tableKey, int offset, int pageSize, string? sortColumn, string? sortDirection)
    {
        var settings = await _applicationSettingsStore.LoadAsync();
        var context = await ResolveContextAsync(tableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var resolvedSort = ResolveSort(schema, sortColumn, sortDirection);
        TableDataResult page;
        try
        {
            page = await _queryService.GetTablePageAsync(context.Connection, schema, offset, pageSize, resolvedSort.Column, resolvedSort.Descending, settings.ShowTableRowCounts);
        }
        catch (DbException ex)
        {
            throw new InvalidOperationException(BuildFriendlyReadErrorMessage(ex, schema));
        }

        return new TablePageResponse(
            tableKey,
            schema.Table.DatabaseName,
            schema.Table.SchemaName,
            schema.Table.TableName,
            schema.Table.Comment,
            settings.ShowTableRowCounts ? page.RowCount ?? schema.Table.RowCount : null,
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

    public async Task<TableDesignResponse> GetTableDesignAsync(string tableKey)
    {
        var context = await ResolveContextAsync(tableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);

        return new TableDesignResponse(
            tableKey,
            context.Connection.Id,
            schema.Table.DatabaseName,
            ToProviderKey(context.Connection.ProviderType),
            ToObjectTypeKey(schema.Table.ObjectType),
            schema.Table.SchemaName,
            schema.Table.TableName,
            schema.Table.Comment,
            schema.Columns.Select(MapColumn).ToArray(),
            schema.Columns.Where(column => column.ForeignKey is not null).Select(column => new ForeignKeyDto(
                column.Name,
                BuildTableKey(context.Connection.Id, ResolveTargetTable(context.Table.DatabaseName, column.ForeignKey!)),
                column.ForeignKey!.TargetColumn,
                !IsPhysicalForeignKey(column.ForeignKey!))).ToArray(),
            await BuildDesignIndexesAsync(context.Connection, schema),
            (await _metadataService.GetObjectTriggersAsync(context.Connection, schema.Table)).Select(trigger => new TableTriggerDto(trigger.Name, trigger.Timing, trigger.Event)).ToArray(),
            (await _metadataService.GetObjectStatisticsAsync(context.Connection, schema.Table)).Select(MapStatistic).ToArray());
    }

    public async Task<TableSearchResponse> SearchTableAsync(string tableKey, string query, string[]? columns, int offset, int pageSize, string? sortColumn, string? sortDirection)
    {
        var context = await ResolveContextAsync(tableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var searchableColumns = ResolveSearchableColumns(schema, columns);
        var resolvedSort = ResolveSort(schema, sortColumn, sortDirection);
        TableSearchResult result;
        try
        {
            result = await _queryService.SearchTableAsync(context.Connection, schema, searchableColumns, query, offset, pageSize, resolvedSort.Column, resolvedSort.Descending);
        }
        catch (DbException ex)
        {
            throw new InvalidOperationException(BuildFriendlyReadErrorMessage(ex, schema));
        }

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

        var connection = await GetEffectiveConnectionAsync(connectionId);
        var tables = connection.ProviderType == DatabaseProviderType.SqlServer
            ? await _metadataService.GetTablesAsync(connection, database, includeSystemObjects: true)
            : await EnsureTablesAsync(connection, database);
        var views = connection.ProviderType == DatabaseProviderType.SqlServer
            ? await _metadataService.GetViewsAsync(connection, database, includeSystemObjects: true)
            : await EnsureViewsAsync(connection, database);
        var schemas = await Task.WhenAll(tables.Concat(views).Select(async table => await GetSchemaAsync(connection, table)));

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

    public async Task<CatalogObjectDetailResponse> GetCatalogObjectDetailAsync(Guid connectionId, string database, string objectType, string? schema, string name)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("Database is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Object name is required.");
        }

        var connection = await GetEffectiveConnectionAsync(connectionId);
        var resolvedType = ParseCatalogObjectType(objectType);
        var detail = await _metadataService.GetCatalogObjectDetailAsync(connection, database, resolvedType, schema, name)
            ?? throw new InvalidOperationException("Catalog object not found.");

        return new CatalogObjectDetailResponse(
            connectionId,
            database,
            ToProviderKey(connection.ProviderType),
            ToCatalogObjectTypeKey(detail.ObjectType),
            detail.SchemaName,
            detail.ObjectName,
            detail.Title,
            detail.Summary,
            detail.Definition,
            detail.Properties.Select(property => new CatalogObjectPropertyDto(property.Label, property.Value)).ToArray());
    }

    public async Task<CellContentResponse> GetCellContentAsync(string tableKey, string rowKey, string columnName)
    {
        var context = await ResolveContextAsync(tableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var column = schema.Columns.FirstOrDefault(entry => string.Equals(entry.Name, columnName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Column not found.");

        RecordDetailsResult record;
        try
        {
            record = await _queryService.GetRecordAsync(context.Connection, schema, DecodeRowKey(rowKey))
                ?? throw new InvalidOperationException("Record not found.");
        }
        catch (DbException ex)
        {
            throw new InvalidOperationException(BuildFriendlyReadErrorMessage(ex, schema));
        }

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
        RecordDetailsResult record;
        try
        {
            record = await _queryService.GetRecordAsync(context.Connection, schema, keyValues)
                ?? throw new InvalidOperationException("Record not found.");
        }
        catch (DbException ex)
        {
            throw new InvalidOperationException(BuildFriendlyReadErrorMessage(ex, schema));
        }

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

    /// <summary>
    /// 获取存储过程或函数的源代码。
    /// SQL Server 使用 ALTER（兼容旧版本），PostgreSQL 使用 CREATE OR REPLACE，
    /// 使用户可以直接 F5 保存修改。
    /// </summary>
    public async Task<RoutineSourceResponse> GetRoutineSourceAsync(RoutineSourceRequest request)
    {
        var connection = (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == request.ConnectionId)
            ?? throw new InvalidOperationException("Connection not found.");
        var source = await _metadataService.GetRoutineSourceAsync(connection, request.Database, request.Schema, request.Name, request.RoutineType);

        if (source is not null && connection.ProviderType == DatabaseProviderType.SqlServer)
        {
            // 将 CREATE PROCEDURE/FUNCTION 替换为 ALTER（兼容 SQL Server 2008+）
            source = ReplaceFirstCreateWithAlter(source);
        }

        return new RoutineSourceResponse(source);
    }

    /// <summary>
    /// 使用 ANTLR TSql Lexer 将 DDL 中第一个 CREATE 关键字替换为 ALTER。
    /// 通过 token 流精确定位，不会误改注释或字符串中的内容。
    /// </summary>
    private static string ReplaceFirstCreateWithAlter(string source)
    {
        var inputStream = new AntlrInputStream(source);
        var lexer = new TSqlLexer(inputStream);
        var tokens = lexer.GetAllTokens();

        // 找到第一个 CREATE token
        IToken? createToken = null;
        IToken? nextKeywordToken = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            // 跳过空白和注释 channel（channel 0 = default）
            if (token.Channel != 0)
                continue;

            if (createToken is null)
            {
                if (token.Type == TSqlLexer.CREATE)
                {
                    createToken = token;
                }
                else
                {
                    break; // 第一个有效 token 不是 CREATE，不做替换
                }
            }
            else
            {
                // CREATE 之后的第一个有效 token：检查是否是 PROCEDURE 或 FUNCTION
                if (token.Type == TSqlLexer.PROCEDURE || token.Type == TSqlLexer.PROC
                    || token.Type == TSqlLexer.FUNCTION)
                {
                    nextKeywordToken = token;
                }
                break;
            }
        }

        if (createToken is null || nextKeywordToken is null)
            return source;

        // 精确替换 CREATE token 为 ALTER
        var sb = new StringBuilder(source.Length + 10);
        sb.Append(source, 0, createToken.StartIndex);
        sb.Append("ALTER");
        sb.Append(source, createToken.StopIndex + 1, source.Length - createToken.StopIndex - 1);
        return sb.ToString();
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
        InvalidateConnectionCaches(request.ConnectionId, request.Database);

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

    /// <summary>通过主键删除一行记录。</summary>
    public async Task DeleteTableRowAsync(TableRowDeleteRequest request)
    {
        var context = await ResolveContextAsync(request.TableKey);
        var schema = await GetSchemaAsync(context.Connection, context.Table);
        var keyValues = DecodeRowKey(request.RowKey);

        try
        {
            await _queryService.DeleteRowAsync(context.Connection, schema, keyValues);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbException ex)
        {
            throw new InvalidOperationException($"删除失败: {ex.Message}");
        }
    }

    public async Task DeleteConnectionAsync(Guid connectionId)
    {
        var remaining = (await _connectionStore.LoadAsync()).Where(connection => connection.Id != connectionId).ToArray();
        await _connectionStore.SaveAsync(remaining);
        SshTunnelManager.Release(connectionId);
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

        var sqlServerRequest = request.SqlServer ?? new SqlServerConnectionRequest(null, null);
        var sqlServerAuthenticationMode = (sqlServerRequest.AuthenticationMode ?? (existing?.SqlServer.AuthenticationMode == SqlServerAuthenticationMode.WindowsIntegrated ? "windows" : "password")).ToLowerInvariant() switch
        {
            "windows" when providerType == DatabaseProviderType.SqlServer => SqlServerAuthenticationMode.WindowsIntegrated,
            "password" => SqlServerAuthenticationMode.UsernamePassword,
            "windows" => throw new InvalidOperationException("只有 SQL Server 支持 Windows 身份验证。"),
            _ => throw new InvalidOperationException("不支持的认证方式。"),
        };

        var resolvedUsername = sqlServerAuthenticationMode == SqlServerAuthenticationMode.WindowsIntegrated || providerType == DatabaseProviderType.Sqlite
            ? string.Empty
            : !string.IsNullOrWhiteSpace(request.Username)
              ? request.Username.Trim()
              : existing?.Username ?? throw new InvalidOperationException("用户名不能为空。");

        var resolvedPassword = sqlServerAuthenticationMode == SqlServerAuthenticationMode.WindowsIntegrated || providerType == DatabaseProviderType.Sqlite
            ? string.Empty
            : request.Password ?? existing?.Password ?? string.Empty;

        var sqliteRequest = request.Sqlite ?? new SqliteConnectionRequest(null, null);
        var sqliteCipherRequest = sqliteRequest.Cipher ?? new SqliteCipherRequest(false, null, null, null, null, null, null, null, null, null, null);
        var sqliteCipher = ResolveSqliteCipherOptions(providerType, sqliteCipherRequest, existing?.Sqlite.Cipher);
        var sqliteOpenMode = providerType == DatabaseProviderType.Sqlite
            ? ParseSqliteOpenMode(sqliteRequest.OpenMode ?? ToSqliteOpenModeKey(existing?.Sqlite.OpenMode ?? SqliteOpenMode.ReadWrite))
            : SqliteOpenMode.ReadWrite;

        var sshTunnel = request.SshTunnel ?? new SshTunnelRequest(false, "password", null, null, null, null, null, null);
        var sshAuthenticationMode = ParseSshAuthenticationMode(sshTunnel.Authentication);

        if (sshTunnel.Enabled && providerType == DatabaseProviderType.Sqlite)
        {
            throw new InvalidOperationException("SQLite 不支持 SSH 隧道。");
        }

        if (sshTunnel.Enabled && sqlServerAuthenticationMode == SqlServerAuthenticationMode.WindowsIntegrated)
        {
            throw new InvalidOperationException("SSH 隧道下暂不支持 SQL Server 的 Windows 身份验证，请改用账号密码连接。");
        }

        if (sshTunnel.Enabled && providerType == DatabaseProviderType.SqlServer && LooksLikeNamedInstance(request.Host))
        {
            throw new InvalidOperationException("SSH 隧道下的 SQL Server 需要填写显式 TCP 主机和端口，不支持命名实例地址。");
        }

        var resolvedSshHost = sshTunnel.Enabled
            ? !string.IsNullOrWhiteSpace(sshTunnel.Host)
                ? sshTunnel.Host.Trim()
                : existing?.SshTunnel.Host ?? throw new InvalidOperationException("SSH 主机不能为空。")
            : string.Empty;

        var resolvedSshUsername = sshTunnel.Enabled
            ? !string.IsNullOrWhiteSpace(sshTunnel.Username)
                ? sshTunnel.Username.Trim()
                : existing?.SshTunnel.Username ?? throw new InvalidOperationException("SSH 用户名不能为空。")
            : string.Empty;

        var resolvedSshPassword = sshTunnel.Enabled
            ? sshTunnel.Password ?? existing?.SshTunnel.Password ?? string.Empty
            : string.Empty;

        if (sshTunnel.Enabled && sshAuthenticationMode == SshAuthenticationMode.Password && string.IsNullOrWhiteSpace(resolvedSshPassword))
        {
            throw new InvalidOperationException("SSH 密码不能为空。");
        }

        var resolvedPrivateKeyPath = sshTunnel.Enabled && sshAuthenticationMode == SshAuthenticationMode.PublicKey
            ? !string.IsNullOrWhiteSpace(sshTunnel.PrivateKeyPath)
                ? sshTunnel.PrivateKeyPath.Trim()
                : existing?.SshTunnel.PrivateKeyPath ?? string.Empty
            : string.Empty;

        var resolvedPassphrase = sshTunnel.Enabled && sshAuthenticationMode == SshAuthenticationMode.PublicKey
            ? sshTunnel.Passphrase ?? existing?.SshTunnel.Passphrase ?? string.Empty
            : string.Empty;

        return new ConnectionDefinition
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Name = request.Name.Trim(),
            ProviderType = providerType,
            Host = request.Host.Trim(),
            Port = request.Port ?? GetDefaultPort(providerType),
            Username = resolvedUsername,
            Password = resolvedPassword,
            SqlServer = new SqlServerConnectionOptions
            {
                AuthenticationMode = sqlServerAuthenticationMode,
                TrustServerCertificate = sqlServerRequest.TrustServerCertificate ?? existing?.SqlServer.TrustServerCertificate ?? true,
            },
            MySql = new MySqlConnectionOptions(),
            PostgreSql = new PostgreSqlConnectionOptions(),
            Sqlite = new SqliteConnectionOptions
            {
                OpenMode = sqliteOpenMode,
                Cipher = sqliteCipher,
            },
            SshTunnel = new SshTunnelOptions
            {
                Enabled = sshTunnel.Enabled,
                AuthenticationMode = sshAuthenticationMode,
                Host = resolvedSshHost,
                Port = sshTunnel.Enabled ? sshTunnel.Port ?? 22 : 22,
                Username = resolvedSshUsername,
                Password = resolvedSshPassword,
                PrivateKeyPath = resolvedPrivateKeyPath,
                Passphrase = resolvedPassphrase,
            },
        };
    }

    private static bool LooksLikeNamedInstance(string host)
    {
        return host.Contains('\\', StringComparison.Ordinal)
            || host.Contains("(local)", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, ".", StringComparison.Ordinal);
    }

    private static SshAuthenticationMode ParseSshAuthenticationMode(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "publickey" => SshAuthenticationMode.PublicKey,
            "public-key" => SshAuthenticationMode.PublicKey,
            _ => SshAuthenticationMode.Password,
        };
    }

    private static string ToSshAuthenticationKey(SshAuthenticationMode value)
    {
        return value == SshAuthenticationMode.PublicKey ? "publicKey" : "password";
    }

    private static SqliteCipherOptions ResolveSqliteCipherOptions(DatabaseProviderType providerType, SqliteCipherRequest request, SqliteCipherOptions? existing)
    {
        if (providerType != DatabaseProviderType.Sqlite)
        {
            return new SqliteCipherOptions();
        }

        if (!request.Enabled)
        {
            return new SqliteCipherOptions();
        }

        var resolvedPassword = !string.IsNullOrWhiteSpace(request.Password)
            ? request.Password.Trim()
            : existing?.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(resolvedPassword))
        {
            throw new InvalidOperationException("启用 SQLCipher 时必须提供密码或十六进制密钥。", null);
        }

        var keyFormat = ParseSqliteCipherKeyFormat(request.KeyFormat);
        var normalizedKdfAlgorithm = NormalizeSqliteCipherAlgorithm(request.KdfAlgorithm, "cipher_kdf_algorithm",
        [
            "PBKDF2_HMAC_SHA1",
            "PBKDF2_HMAC_SHA256",
            "PBKDF2_HMAC_SHA512",
        ]);
        var normalizedHmacAlgorithm = NormalizeSqliteCipherAlgorithm(request.HmacAlgorithm, "cipher_hmac_algorithm",
        [
            "HMAC_SHA1",
            "HMAC_SHA256",
            "HMAC_SHA512",
        ]);

        ValidateSqliteCipherNumber(request.PageSize, "cipher_page_size", minimumValue: 512);
        ValidateSqliteCipherNumber(request.KdfIter, "kdf_iter", minimumValue: 1);
        ValidateSqliteCipherNumber(request.CipherCompatibility, "cipher_compatibility", minimumValue: 1, maximumValue: 4);
        ValidateSqliteCipherNumber(request.PlaintextHeaderSize, "cipher_plaintext_header_size", minimumValue: 0);
        ValidateSqliteCipherNumber(request.SkipBytes, "skip_bytes", minimumValue: 0);

        return new SqliteCipherOptions
        {
            Enabled = true,
            Password = resolvedPassword,
            KeyFormat = keyFormat,
            PageSize = request.PageSize,
            KdfIter = request.KdfIter,
            CipherCompatibility = request.CipherCompatibility,
            PlaintextHeaderSize = request.PlaintextHeaderSize,
            SkipBytes = request.SkipBytes,
            UseHmac = request.UseHmac,
            KdfAlgorithm = normalizedKdfAlgorithm,
            HmacAlgorithm = normalizedHmacAlgorithm,
        };
    }

    private static void ValidateSqliteCipherNumber(int? value, string optionName, int minimumValue, int? maximumValue = null)
    {
        if (!value.HasValue)
        {
            return;
        }

        if (value.Value < minimumValue || (maximumValue.HasValue && value.Value > maximumValue.Value))
        {
            throw new InvalidOperationException($"SQLite 加密参数 {optionName} 超出允许范围。", null);
        }
    }

    private static string NormalizeSqliteCipherAlgorithm(string? value, string optionName, IReadOnlyCollection<string> allowedValues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalizedValue = value.Trim().ToUpperInvariant();
        if (!allowedValues.Contains(normalizedValue, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"SQLite 加密参数 {optionName} 不受支持。", null);
        }

        return normalizedValue;
    }

    private static SqliteCipherKeyFormat ParseSqliteCipherKeyFormat(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "hex" => SqliteCipherKeyFormat.Hex,
            _ => SqliteCipherKeyFormat.Passphrase,
        };
    }

    private static ConnectionDefinition CloneConnectionDefinition(ConnectionDefinition source)
    {
        return new ConnectionDefinition
        {
            Id = source.Id,
            Name = source.Name,
            ProviderType = source.ProviderType,
            Host = source.Host,
            Port = source.Port,
            Username = source.Username,
            Password = source.Password,
            SqlServer = new SqlServerConnectionOptions
            {
                AuthenticationMode = source.SqlServer.AuthenticationMode,
                TrustServerCertificate = source.SqlServer.TrustServerCertificate,
            },
            MySql = new MySqlConnectionOptions(),
            PostgreSql = new PostgreSqlConnectionOptions(),
            Sqlite = new SqliteConnectionOptions
            {
                OpenMode = source.Sqlite.OpenMode,
                Cipher = CloneSqliteCipherOptions(source.Sqlite.Cipher),
            },
            SshTunnel = new SshTunnelOptions
            {
                Enabled = source.SshTunnel.Enabled,
                AuthenticationMode = source.SshTunnel.AuthenticationMode,
                Host = source.SshTunnel.Host,
                Port = source.SshTunnel.Port,
                Username = source.SshTunnel.Username,
                Password = source.SshTunnel.Password,
                PrivateKeyPath = source.SshTunnel.PrivateKeyPath,
                Passphrase = source.SshTunnel.Passphrase,
            },
        };
    }

    private static SqliteCipherOptions CloneSqliteCipherOptions(SqliteCipherOptions source)
    {
        return new SqliteCipherOptions
        {
            Enabled = source.Enabled,
            Password = source.Password,
            KeyFormat = source.KeyFormat,
            PageSize = source.PageSize,
            KdfIter = source.KdfIter,
            CipherCompatibility = source.CipherCompatibility,
            PlaintextHeaderSize = source.PlaintextHeaderSize,
            SkipBytes = source.SkipBytes,
            UseHmac = source.UseHmac,
            KdfAlgorithm = source.KdfAlgorithm,
            HmacAlgorithm = source.HmacAlgorithm,
        };
    }

    private static string ToSqliteCipherKeyFormatKey(SqliteCipherKeyFormat value)
    {
        return value == SqliteCipherKeyFormat.Hex ? "hex" : "passphrase";
    }

    private static SqliteOpenMode ParseSqliteOpenMode(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "readonly" => SqliteOpenMode.ReadOnly,
            _ => SqliteOpenMode.ReadWrite,
        };
    }

    private static string? ToSqliteOpenModeKey(ConnectionDefinition connection)
    {
        return connection.ProviderType == DatabaseProviderType.Sqlite
            ? ToSqliteOpenModeKey(connection.Sqlite.OpenMode)
            : null;
    }

    private static string ToSqliteOpenModeKey(SqliteOpenMode value)
    {
        return value == SqliteOpenMode.ReadOnly ? "readonly" : "readwrite";
    }

    private ConnectionNodeDto BuildConnectionNode(ConnectionDefinition connection, string? error, IReadOnlyList<DatabaseNodeDto> databases)
    {
        return new ConnectionNodeDto(
            connection.Id,
            connection.Name,
            ToProviderKey(connection.ProviderType),
            connection.Host,
            connection.Port > 0 ? connection.Port : null,
            new SqlServerConnectionSummaryDto(
                connection.SqlServer.AuthenticationMode == SqlServerAuthenticationMode.WindowsIntegrated ? "windows" : "password",
                connection.SqlServer.TrustServerCertificate),
            new MySqlConnectionSummaryDto(),
            new PostgreSqlConnectionSummaryDto(),
            new SqliteConnectionSummaryDto(ToSqliteOpenModeKey(connection.Sqlite.OpenMode)),
            PickAccent(connection.ProviderType),
            error,
            databases);
    }

    private async Task<ConnectionDefinition> GetStoredConnectionAsync(Guid connectionId)
    {
        return (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == connectionId)
            ?? throw new InvalidOperationException("Connection not found.");
    }

    private async Task<ConnectionDefinition> GetEffectiveConnectionAsync(Guid connectionId)
    {
        return await GetStoredConnectionAsync(connectionId);
    }

    private static InvalidOperationException BuildConnectionFailureException(ConnectionDefinition definition)
    {
        if (definition.ProviderType == DatabaseProviderType.Sqlite)
        {
            return definition.Sqlite.Cipher.Enabled
                ? new InvalidOperationException("SQLite / SQLCipher 连接失败，请检查数据库文件路径、密码、密钥格式以及加密参数。新数据库文件会在首次连接时自动创建。", null)
                : new InvalidOperationException("SQLite 连接失败，请检查数据库文件路径以及目录读写权限。新数据库文件会在首次连接时自动创建。", null);
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
        column.MaxLength,
        column.Comment,
        column.NumericPrecision,
        column.NumericScale,
        column.IsHiddenRowId);

    private static TableStatisticDto MapStatistic(TableStatisticInfo statistic) => new(
        statistic.Name,
        statistic.IsAutoCreated,
        statistic.IsUserCreated,
        statistic.NoRecompute,
        statistic.FilterDefinition,
        statistic.Columns.ToArray());

    private async Task<IReadOnlyList<TableIndexDto>> BuildDesignIndexesAsync(ConnectionDefinition connection, TableSchema schema)
    {
        var indexes = await _metadataService.GetObjectIndexesAsync(connection, schema.Table);
        if (indexes.Count > 0)
        {
            return indexes.Select(index => new TableIndexDto(index.Name, index.IsPrimaryKey, index.IsUnique, index.Columns.ToArray())).ToArray();
        }

        if (schema.PrimaryKeys.Count == 0)
        {
            return Array.Empty<TableIndexDto>();
        }

        return
        [
            new TableIndexDto($"PK_{schema.Table.TableName}", true, true, schema.PrimaryKeys.ToArray())
        ];
    }

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

    private static string BuildFriendlyReadErrorMessage(DbException exception, TableSchema schema)
    {
        var message = exception.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
        if (schema.Table.ObjectType == DbObjectType.View)
        {
            if (message.Contains("绑定错误", StringComparison.OrdinalIgnoreCase)
                || message.Contains("could not use view or function", StringComparison.OrdinalIgnoreCase)
                || message.Contains("invalid column name", StringComparison.OrdinalIgnoreCase)
                || message.Contains("列名", StringComparison.OrdinalIgnoreCase))
            {
                return $"视图 {schema.Table.DisplayName} 当前无法查询，可能是该视图或它依赖的对象定义已经失效。数据库返回: {message}";
            }

            return $"视图 {schema.Table.DisplayName} 查询失败: {message}";
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

    /// <summary>通过 IncomingForeignKeys 补充直接边未覆盖的反向关系。</summary>
    private static DatabaseGraphEdgeDto CreateIncomingEdge(Guid connectionId, TableSchema[] schemas, TableSchema targetSchema, ForeignKeyReference incoming)
    {
        var sourceTable = new DbTableInfo
        {
            DatabaseName = incoming.SourceDatabase,
            SchemaName = incoming.SourceSchema,
            TableName = incoming.SourceTable,
        };
        var sourceTableKey = BuildTableKey(connectionId, sourceTable);
        var targetTableKey = BuildTableKey(connectionId, targetSchema.Table);

        var sourceSchema = Array.Find(schemas, s =>
            string.Equals(s.Table.TableName, incoming.SourceTable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Table.SchemaName ?? string.Empty, incoming.SourceSchema ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        var sourceIsUnique = sourceSchema?.PrimaryKeys.Contains(incoming.SourceColumn, StringComparer.OrdinalIgnoreCase) ?? false;
        var relationType = sourceIsUnique ? "one-to-one" : "many-to-one";
        var relationLabel = sourceIsUnique ? "1:1" : "N:1";

        return new DatabaseGraphEdgeDto(
            sourceTableKey,
            targetTableKey,
            relationType,
            $"{relationLabel} · {incoming.SourceColumn} -> {incoming.TargetColumn}",
            !IsPhysicalForeignKey(incoming),
            null,
            incoming.SourceColumn,
            incoming.TargetColumn);
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
        var connection = await GetEffectiveConnectionAsync(parsed.ConnectionId);
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
        var views = await EnsureViewsAsync(connection, table.DatabaseName);
        var resolved = tables.Concat(views).FirstOrDefault(item =>
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

        var settings = await _applicationSettingsStore.LoadAsync();
        var tables = await _metadataService.GetTablesAsync(connection, database, includeSystemObjects: false, settings.ShowTableRowCounts);
        _tablesCache[cacheKey] = tables;
        return tables;
    }

    private void InvalidateMetadataCaches()
    {
        _schemaCache.Clear();
        _tablesCache.Clear();
        _viewsCache.Clear();
    }

    /// <summary>
    /// 获取数据库属性信息。
    /// </summary>
    public async Task<DatabasePropertiesResponse> GetDatabasePropertiesAsync(Guid connectionId, string database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("Database is required.");
        }

        var connection = await GetEffectiveConnectionAsync(connectionId);

        var properties = await _metadataService.GetDatabasePropertiesAsync(connection, database)
            ?? throw new InvalidOperationException("Database properties not available for this provider.");

        return new DatabasePropertiesResponse(
            connectionId,
            database,
            ToProviderKey(connection.ProviderType),
            properties.GeneralProperties.Select(p => new CatalogObjectPropertyDto(p.Label, p.Value)).ToArray(),
            properties.Files.Select(f => new DatabaseFileDto(f.LogicalName, f.FileType, f.FileGroup, f.SizeMB, f.AutoGrowth, f.Path)).ToArray(),
            properties.Permissions.Select(p => new DatabasePermissionDto(p.UserName, p.UserType, p.DefaultSchema, p.LoginName, p.Roles)).ToArray());
    }

    private async Task<IReadOnlyList<DbTableInfo>> EnsureViewsAsync(ConnectionDefinition connection, string database)
    {
        var cacheKey = BuildViewsCacheKey(connection.Id, database);
        if (_viewsCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var views = await _metadataService.GetViewsAsync(connection, database);
        _viewsCache[cacheKey] = views;
        return views;
    }

    private static DbTableInfo ResolveTargetTable(string fallbackDatabase, ForeignKeyReference foreignKey) => new()
    {
        DatabaseName = string.IsNullOrWhiteSpace(foreignKey.TargetDatabase) ? fallbackDatabase : foreignKey.TargetDatabase,
        SchemaName = foreignKey.TargetSchema,
        TableName = foreignKey.TargetTable,
    };

    private void InvalidateConnectionCaches(Guid connectionId, string? database = null)
    {
        foreach (var cacheKey in _schemaCache.Keys.Where(key => key.StartsWith($"{connectionId}:", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            if (database is null || cacheKey.StartsWith($"{connectionId}:{database}.", StringComparison.OrdinalIgnoreCase))
            {
                _schemaCache.Remove(cacheKey);
            }
        }

        if (database is null)
        {
            foreach (var cacheKey in _tablesCache.Keys.Where(key => key.StartsWith($"{connectionId}:", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _tablesCache.Remove(cacheKey);
            }

            foreach (var cacheKey in _viewsCache.Keys.Where(key => key.StartsWith($"{connectionId}:", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _viewsCache.Remove(cacheKey);
            }

            return;
        }

        _tablesCache.Remove(BuildTablesCacheKey(connectionId, database));
        _viewsCache.Remove(BuildViewsCacheKey(connectionId, database));
    }

    private static bool IsPhysicalForeignKey(ForeignKeyReference foreignKey) =>
        !string.Equals(foreignKey.TargetColumn, "id", StringComparison.OrdinalIgnoreCase)
        || !foreignKey.SourceColumn.EndsWith("_id", StringComparison.OrdinalIgnoreCase);

    private static string ToObjectTypeKey(DbObjectType objectType) => objectType switch
    {
        DbObjectType.View => "view",
        _ => "table",
    };

    private static DbCatalogObjectType ParseCatalogObjectType(string value) => value.ToLowerInvariant() switch
    {
        "synonym" => DbCatalogObjectType.Synonym,
        "sequence" => DbCatalogObjectType.Sequence,
        "rule" => DbCatalogObjectType.Rule,
        "default" => DbCatalogObjectType.Default,
        "user-defined-type" => DbCatalogObjectType.UserDefinedType,
        "type" => DbCatalogObjectType.UserDefinedType,
        "database-trigger" => DbCatalogObjectType.DatabaseTrigger,
        "xml-schema-collection" => DbCatalogObjectType.XmlSchemaCollection,
        "assembly" => DbCatalogObjectType.Assembly,
        _ => throw new InvalidOperationException("Unsupported catalog object type."),
    };

    private static string ToCatalogObjectTypeKey(DbCatalogObjectType objectType) => objectType switch
    {
        DbCatalogObjectType.Synonym => "synonym",
        DbCatalogObjectType.Sequence => "sequence",
        DbCatalogObjectType.Rule => "rule",
        DbCatalogObjectType.Default => "default",
        DbCatalogObjectType.UserDefinedType => "user-defined-type",
        DbCatalogObjectType.DatabaseTrigger => "database-trigger",
        DbCatalogObjectType.XmlSchemaCollection => "xml-schema-collection",
        DbCatalogObjectType.Assembly => "assembly",
        _ => "synonym",
    };

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
    private static string BuildViewsCacheKey(Guid connectionId, string database) => $"{connectionId}:{database}:views";

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