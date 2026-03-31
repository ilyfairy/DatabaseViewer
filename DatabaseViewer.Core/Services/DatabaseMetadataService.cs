using System.Data.Common;
using System.IO;
using Dapper;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

public sealed class DatabaseMetadataService
{
    public async Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionDefinition connection)
    {
        await using var db = DbConnectionFactory.Create(connection);
        await db.OpenAsync();

        return connection.ProviderType switch
        {
            DatabaseProviderType.SqlServer => (await db.QueryAsync<string>(@"
                SELECT name
                FROM sys.databases
                WHERE database_id > 4
                ORDER BY name;")).ToArray(),
            DatabaseProviderType.MySql => (await db.QueryAsync<string>(@"
                SELECT SCHEMA_NAME
                FROM information_schema.SCHEMATA
                WHERE SCHEMA_NAME NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
                ORDER BY SCHEMA_NAME;")).ToArray(),
            DatabaseProviderType.PostgreSql => (await db.QueryAsync<string>(@"
                SELECT datname
                FROM pg_database
                WHERE datistemplate = FALSE
                  AND datallowconn = TRUE
                ORDER BY datname;")).ToArray(),
                        DatabaseProviderType.Sqlite => [BuildSqliteDatabaseName(connection)],
            _ => throw new NotSupportedException(),
        };
    }

    public async Task<IReadOnlyList<DbTableInfo>> GetTablesAsync(ConnectionDefinition connection, string databaseName)
    {
        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        if (connection.ProviderType == DatabaseProviderType.SqlServer)
        {
            var sql = @"
                SELECT
                    s.name AS SchemaName,
                    t.name AS TableName,
                    CAST(ep.value AS nvarchar(4000)) AS Comment,
                    CAST(SUM(CASE WHEN p.index_id IN (0, 1) THEN p.rows ELSE 0 END) AS int) AS TableRowCount
                FROM sys.tables t
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.major_id = t.object_id
                    AND ep.minor_id = 0
                    AND ep.name = 'MS_Description'
                LEFT JOIN sys.partitions p ON p.object_id = t.object_id
                GROUP BY s.name, t.name, ep.value
                ORDER BY s.name, t.name;";

            var rows = await db.QueryAsync(sql);
            return rows.Select(row => new DbTableInfo
            {
                DatabaseName = databaseName,
                SchemaName = row.SchemaName,
                TableName = row.TableName,
                Comment = row.Comment,
                RowCount = row.TableRowCount,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.PostgreSql)
        {
            var postgreSqlRows = await db.QueryAsync(@"
                SELECT
                    t.table_schema AS SchemaName,
                    t.table_name AS TableName,
                    obj_description(c.oid, 'pg_class') AS Comment,
                    CAST(CASE WHEN c.reltuples >= 0 THEN c.reltuples ELSE NULL END AS integer) AS TableRowCount
                FROM information_schema.tables t
                INNER JOIN pg_namespace n ON n.nspname = t.table_schema
                INNER JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = t.table_name
                WHERE t.table_catalog = @databaseName
                  AND t.table_type = 'BASE TABLE'
                  AND t.table_schema NOT IN ('pg_catalog', 'information_schema')
                ORDER BY t.table_schema, t.table_name;", new { databaseName });

            return postgreSqlRows.Select(row => new DbTableInfo
            {
                DatabaseName = databaseName,
                SchemaName = row.SchemaName,
                TableName = row.TableName,
                Comment = string.IsNullOrWhiteSpace((string?)row.Comment) ? null : row.Comment,
                RowCount = row.TableRowCount,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.Sqlite)
        {
            var sqliteRows = await db.QueryAsync(@"
                SELECT
                    name AS TableName,
                    NULL AS Comment,
                    NULL AS TableRowCount
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY name;");

            return sqliteRows.Select(row => new DbTableInfo
            {
                DatabaseName = databaseName,
                SchemaName = null,
                TableName = row.TableName,
                Comment = null,
                RowCount = null,
            }).ToArray();
        }

        var mySqlRows = await db.QueryAsync(@"
            SELECT TABLE_NAME AS TableName, TABLE_COMMENT AS Comment, TABLE_ROWS AS TableRowCount
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @databaseName
            ORDER BY TABLE_NAME;", new { databaseName });

        return mySqlRows.Select(row => new DbTableInfo
        {
            DatabaseName = databaseName,
            SchemaName = databaseName,
            TableName = row.TableName,
            Comment = string.IsNullOrWhiteSpace((string?)row.Comment) ? null : row.Comment,
            RowCount = row.TableRowCount,
        }).ToArray();
    }

    public async Task<TableSchema> GetTableSchemaAsync(ConnectionDefinition connection, DbTableInfo table, IReadOnlyList<DbTableInfo> databaseTables)
    {
        await using var db = DbConnectionFactory.Create(connection, table.DatabaseName);
        await db.OpenAsync();

        var columns = (await GetColumnsAsync(db, connection, table)).ToList();
        var foreignKeys = await GetForeignKeysAsync(db, connection, table.DatabaseName);
        var foreignKeysByColumn = foreignKeys
            .Where(item => string.Equals(item.SourceTable, table.TableName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.SourceSchema ?? string.Empty, table.SchemaName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.SourceColumn, item => item, StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            if (foreignKeysByColumn.TryGetValue(column.Name, out var foreignKey))
            {
                column.ForeignKey = foreignKey;
            }
        }

        InferLogicalForeignKeys(columns, table, databaseTables);
        var incomingForeignKeys = foreignKeys
            .Where(item => string.Equals(item.TargetTable, table.TableName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.TargetSchema ?? string.Empty, table.SchemaName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .ToList();

        incomingForeignKeys.AddRange(await InferLogicalIncomingForeignKeysAsync(db, connection, table, columns, databaseTables, foreignKeys));

        return new TableSchema
        {
            Table = table,
            Columns = columns,
            IncomingForeignKeys = incomingForeignKeys
                .GroupBy(item => $"{item.SourceDatabase}|{item.SourceSchema}|{item.SourceTable}|{item.SourceColumn}|{item.TargetColumn}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray(),
        };
    }

    private static void InferLogicalForeignKeys(List<ColumnSchema> columns, DbTableInfo sourceTable, IReadOnlyList<DbTableInfo> databaseTables)
    {
        foreach (var column in columns.Where(item => item.ForeignKey is null && item.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase)))
        {
            var stem = column.Name[..^3];
            var candidate = databaseTables.FirstOrDefault(table =>
                string.Equals(table.TableName, stem, StringComparison.OrdinalIgnoreCase)
                || string.Equals(table.TableName, $"{stem}s", StringComparison.OrdinalIgnoreCase)
                || string.Equals(table.TableName, $"{stem}es", StringComparison.OrdinalIgnoreCase));

            if (candidate is null)
            {
                continue;
            }

            column.ForeignKey = new ForeignKeyReference
            {
                SourceDatabase = sourceTable.DatabaseName,
                SourceSchema = sourceTable.SchemaName,
                SourceTable = sourceTable.TableName,
                SourceColumn = column.Name,
                TargetDatabase = candidate.DatabaseName,
                TargetSchema = candidate.SchemaName,
                TargetTable = candidate.TableName,
                TargetColumn = "id",
            };
        }
    }

    private static async Task<IReadOnlyList<ForeignKeyReference>> InferLogicalIncomingForeignKeysAsync(
        DbConnection db,
        ConnectionDefinition connection,
        DbTableInfo targetTable,
        IReadOnlyList<ColumnSchema> targetColumns,
        IReadOnlyList<DbTableInfo> databaseTables,
        IReadOnlyList<ForeignKeyReference> existingForeignKeys)
    {
        var primaryKeys = targetColumns.Where(column => column.IsPrimaryKey).Select(column => column.Name).ToArray();
        if (primaryKeys.Length != 1)
        {
            return Array.Empty<ForeignKeyReference>();
        }

        var targetPk = primaryKeys[0];
        var aliases = BuildLogicalAliases(targetTable.TableName);
        var existingKeys = new HashSet<string>(
            existingForeignKeys.Select(item => $"{item.SourceSchema}|{item.SourceTable}|{item.SourceColumn}|{item.TargetSchema}|{item.TargetTable}|{item.TargetColumn}"),
            StringComparer.OrdinalIgnoreCase);
        var inferred = new List<ForeignKeyReference>();

        foreach (var sourceTable in databaseTables.Where(item => !string.Equals(item.QualifiedKey, targetTable.QualifiedKey, StringComparison.OrdinalIgnoreCase)))
        {
            var sourceColumns = await GetColumnsAsync(db, connection, sourceTable);
            foreach (var sourceColumn in sourceColumns)
            {
                var normalizedSource = NormalizeColumnName(sourceColumn.Name);
                if (!aliases.Contains(normalizedSource) || !string.Equals(sourceColumn.DataType, targetColumns.First(column => column.Name == targetPk).DataType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var key = $"{sourceTable.SchemaName}|{sourceTable.TableName}|{sourceColumn.Name}|{targetTable.SchemaName}|{targetTable.TableName}|{targetPk}";
                if (existingKeys.Contains(key))
                {
                    continue;
                }

                inferred.Add(new ForeignKeyReference
                {
                    SourceDatabase = sourceTable.DatabaseName,
                    SourceSchema = sourceTable.SchemaName,
                    SourceTable = sourceTable.TableName,
                    SourceColumn = sourceColumn.Name,
                    TargetDatabase = targetTable.DatabaseName,
                    TargetSchema = targetTable.SchemaName,
                    TargetTable = targetTable.TableName,
                    TargetColumn = targetPk,
                });
            }
        }

        return inferred;
    }

    private static HashSet<string> BuildLogicalAliases(string tableName)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = NormalizeColumnName(tableName);
        aliases.Add(normalized + "id");

        if (normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase) && normalized.Length > 1)
        {
            aliases.Add(normalized[..^1] + "id");
        }

        if (normalized.EndsWith("es", StringComparison.OrdinalIgnoreCase) && normalized.Length > 2)
        {
            aliases.Add(normalized[..^2] + "id");
        }

        return aliases;
    }

    private static string NormalizeColumnName(string name)
    {
        return name.Replace("_", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
    }

    private static async Task<IReadOnlyList<ColumnSchema>> GetColumnsAsync(DbConnection db, ConnectionDefinition connection, DbTableInfo table)
    {
        if (connection.ProviderType == DatabaseProviderType.SqlServer)
        {
            var sql = $@"
                SELECT
                    c.name AS ColumnName,
                    ty.name AS DataType,
                    CASE WHEN pk.column_id IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsPrimaryKey,
                    c.is_nullable AS IsNullable,
                    c.is_identity AS IsAutoGenerated,
                    c.is_computed AS IsComputed,
                    CAST(c.max_length AS int) AS MaxLength
                FROM {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.columns c
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.tables t ON t.object_id = c.object_id
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.schemas s ON s.schema_id = t.schema_id
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.types ty ON ty.user_type_id = c.user_type_id
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id
                    FROM {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.indexes i
                    INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.index_columns ic
                        ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    WHERE i.is_primary_key = 1
                ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
                WHERE t.name = @tableName AND s.name = @schemaName
                ORDER BY c.column_id;";

            var rows = await db.QueryAsync(sql, new { tableName = table.TableName, schemaName = table.SchemaName ?? "dbo" });
            return rows.Select(row => new ColumnSchema
            {
                Name = row.ColumnName,
                DataType = row.DataType,
                IsPrimaryKey = row.IsPrimaryKey,
                IsNullable = row.IsNullable,
                IsAutoGenerated = row.IsAutoGenerated || string.Equals((string?)row.DataType, "timestamp", StringComparison.OrdinalIgnoreCase) || string.Equals((string?)row.DataType, "rowversion", StringComparison.OrdinalIgnoreCase),
                IsComputed = row.IsComputed,
                MaxLength = row.MaxLength is int length && length > 0 ? length : null,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.PostgreSql)
        {
            var postgreSqlRows = await db.QueryAsync(@"
                SELECT
                    c.column_name AS ColumnName,
                    CASE
                        WHEN c.data_type = 'USER-DEFINED' THEN c.udt_name
                        WHEN c.data_type = 'ARRAY' THEN c.udt_name
                        ELSE c.data_type
                    END AS DataType,
                    EXISTS (
                        SELECT 1
                        FROM information_schema.table_constraints tc
                        INNER JOIN information_schema.key_column_usage kcu
                            ON tc.constraint_name = kcu.constraint_name
                            AND tc.table_schema = kcu.table_schema
                            AND tc.table_name = kcu.table_name
                            AND tc.table_catalog = kcu.table_catalog
                        WHERE tc.constraint_type = 'PRIMARY KEY'
                          AND tc.table_catalog = c.table_catalog
                          AND tc.table_schema = c.table_schema
                          AND tc.table_name = c.table_name
                          AND kcu.column_name = c.column_name
                    ) AS IsPrimaryKey,
                    c.is_nullable = 'YES' AS IsNullable,
                    (c.column_default LIKE 'nextval(%' OR c.is_identity = 'YES') AS IsAutoGenerated,
                    c.is_generated = 'ALWAYS' AS IsComputed,
                    c.character_maximum_length AS MaxLength
                FROM information_schema.columns c
                WHERE c.table_catalog = @databaseName
                  AND c.table_schema = @schemaName
                  AND c.table_name = @tableName
                ORDER BY c.ordinal_position;", new { databaseName = table.DatabaseName, schemaName = table.SchemaName ?? "public", tableName = table.TableName });

            return postgreSqlRows.Select(row => new ColumnSchema
            {
                Name = row.ColumnName,
                DataType = row.DataType,
                IsPrimaryKey = row.IsPrimaryKey,
                IsNullable = row.IsNullable,
                IsAutoGenerated = row.IsAutoGenerated,
                IsComputed = row.IsComputed,
                MaxLength = row.MaxLength,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.Sqlite)
        {
            var sqliteRows = await db.QueryAsync($@"
                SELECT
                    name AS ColumnName,
                    COALESCE(type, '') AS DataType,
                    CASE WHEN pk > 0 THEN CAST(1 AS integer) ELSE CAST(0 AS integer) END AS IsPrimaryKey,
                    CASE WHEN ""notnull"" = 1 OR pk > 0 THEN CAST(0 AS integer) ELSE CAST(1 AS integer) END AS IsNullable,
                    CASE WHEN pk > 0 AND upper(COALESCE(type, '')) LIKE '%INT%' THEN CAST(1 AS integer) ELSE CAST(0 AS integer) END AS IsAutoGenerated,
                    CASE WHEN hidden > 1 THEN CAST(1 AS integer) ELSE CAST(0 AS integer) END AS IsComputed
                FROM pragma_table_xinfo('{EscapeSqliteStringLiteral(table.TableName)}')
                ORDER BY cid;");

            return sqliteRows.Select(row =>
            {
                var dataType = string.IsNullOrWhiteSpace((string?)row.DataType) ? "TEXT" : (string)row.DataType;
                return new ColumnSchema
                {
                    Name = row.ColumnName,
                    DataType = dataType,
                    IsPrimaryKey = Convert.ToInt32(row.IsPrimaryKey) != 0,
                    IsNullable = Convert.ToInt32(row.IsNullable) != 0,
                    IsAutoGenerated = Convert.ToInt32(row.IsAutoGenerated) != 0,
                    IsComputed = Convert.ToInt32(row.IsComputed) != 0,
                    MaxLength = TryParseSqliteMaxLength(dataType),
                };
            }).ToArray();
        }

        var mySqlRows = await db.QueryAsync(@"
            SELECT
                COLUMN_NAME AS ColumnName,
                DATA_TYPE AS DataType,
                COLUMN_KEY = 'PRI' AS IsPrimaryKey,
                IS_NULLABLE = 'YES' AS IsNullable,
                (EXTRA LIKE '%auto_increment%') AS IsAutoGenerated,
                (GENERATION_EXPRESSION IS NOT NULL AND GENERATION_EXPRESSION <> '') AS IsComputed,
                CHARACTER_MAXIMUM_LENGTH AS MaxLength
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @databaseName AND TABLE_NAME = @tableName
            ORDER BY ORDINAL_POSITION;", new { databaseName = table.DatabaseName, tableName = table.TableName });

        return mySqlRows.Select(row => new ColumnSchema
        {
            Name = row.ColumnName,
            DataType = row.DataType,
            IsPrimaryKey = row.IsPrimaryKey,
            IsNullable = row.IsNullable,
            IsAutoGenerated = row.IsAutoGenerated,
            IsComputed = row.IsComputed,
            MaxLength = row.MaxLength,
        }).ToArray();
    }

    private static async Task<IReadOnlyList<ForeignKeyReference>> GetForeignKeysAsync(DbConnection db, ConnectionDefinition connection, string databaseName)
    {
        if (connection.ProviderType == DatabaseProviderType.SqlServer)
        {
            var sql = $@"
                SELECT
                    SCHEMA_NAME(pt.schema_id) AS SourceSchema,
                    pt.name AS SourceTable,
                    pc.name AS SourceColumn,
                    SCHEMA_NAME(rt.schema_id) AS TargetSchema,
                    rt.name AS TargetTable,
                    rc.name AS TargetColumn
                FROM {SqlDialect.QuoteIdentifier(connection.ProviderType, databaseName)}.sys.foreign_key_columns fkc
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, databaseName)}.sys.tables pt ON pt.object_id = fkc.parent_object_id
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, databaseName)}.sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, databaseName)}.sys.tables rt ON rt.object_id = fkc.referenced_object_id
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, databaseName)}.sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id;";

            var rows = await db.QueryAsync(sql);
            return rows.Select(row => new ForeignKeyReference
            {
                SourceDatabase = databaseName,
                SourceSchema = row.SourceSchema,
                SourceTable = row.SourceTable,
                SourceColumn = row.SourceColumn,
                TargetDatabase = databaseName,
                TargetSchema = row.TargetSchema,
                TargetTable = row.TargetTable,
                TargetColumn = row.TargetColumn,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.PostgreSql)
        {
            var postgreSqlRows = await db.QueryAsync(@"
                SELECT
                    tc.table_catalog AS SourceDatabase,
                    tc.table_schema AS SourceSchema,
                    tc.table_name AS SourceTable,
                    kcu.column_name AS SourceColumn,
                    ccu.table_catalog AS TargetDatabase,
                    ccu.table_schema AS TargetSchema,
                    ccu.table_name AS TargetTable,
                    ccu.column_name AS TargetColumn
                FROM information_schema.table_constraints tc
                INNER JOIN information_schema.key_column_usage kcu
                    ON tc.constraint_name = kcu.constraint_name
                    AND tc.table_schema = kcu.table_schema
                    AND tc.table_name = kcu.table_name
                    AND tc.table_catalog = kcu.table_catalog
                INNER JOIN information_schema.constraint_column_usage ccu
                    ON ccu.constraint_name = tc.constraint_name
                    AND ccu.constraint_schema = tc.table_schema
                    AND ccu.constraint_catalog = tc.table_catalog
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND tc.table_catalog = @databaseName;", new { databaseName });

            return postgreSqlRows.Select(row => new ForeignKeyReference
            {
                SourceDatabase = row.SourceDatabase,
                SourceSchema = row.SourceSchema,
                SourceTable = row.SourceTable,
                SourceColumn = row.SourceColumn,
                TargetDatabase = row.TargetDatabase,
                TargetSchema = row.TargetSchema,
                TargetTable = row.TargetTable,
                TargetColumn = row.TargetColumn,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.Sqlite)
        {
            var tableNames = (await db.QueryAsync<string>(@"
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY name;")).ToArray();

            var targetPrimaryKeyCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var foreignKeys = new List<ForeignKeyReference>();

            foreach (var tableName in tableNames)
            {
                var sqliteRows = await db.QueryAsync($@"
                    SELECT
                        ""table"" AS TargetTable,
                        ""from"" AS SourceColumn,
                        COALESCE(""to"", '') AS TargetColumn
                    FROM pragma_foreign_key_list('{EscapeSqliteStringLiteral(tableName)}')
                    ORDER BY id, seq;");

                foreach (var row in sqliteRows)
                {
                    var targetTable = (string)row.TargetTable;
                    var targetColumn = string.IsNullOrWhiteSpace((string?)row.TargetColumn)
                        ? await ResolveSqlitePrimaryKeyColumnAsync(db, targetTable, targetPrimaryKeyCache) ?? "rowid"
                        : (string)row.TargetColumn;

                    foreignKeys.Add(new ForeignKeyReference
                    {
                        SourceDatabase = databaseName,
                        SourceSchema = null,
                        SourceTable = tableName,
                        SourceColumn = row.SourceColumn,
                        TargetDatabase = databaseName,
                        TargetSchema = null,
                        TargetTable = targetTable,
                        TargetColumn = targetColumn,
                    });
                }
            }

            return foreignKeys;
        }

        var mySqlRows = await db.QueryAsync(@"
            SELECT
                TABLE_SCHEMA AS SourceDatabase,
                TABLE_NAME AS SourceTable,
                COLUMN_NAME AS SourceColumn,
                REFERENCED_TABLE_SCHEMA AS TargetDatabase,
                REFERENCED_TABLE_NAME AS TargetTable,
                REFERENCED_COLUMN_NAME AS TargetColumn
            FROM information_schema.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = @databaseName
              AND REFERENCED_TABLE_NAME IS NOT NULL;", new { databaseName });

        return mySqlRows.Select(row => new ForeignKeyReference
        {
            SourceDatabase = row.SourceDatabase,
            SourceSchema = row.SourceDatabase,
            SourceTable = row.SourceTable,
            SourceColumn = row.SourceColumn,
            TargetDatabase = row.TargetDatabase,
            TargetSchema = row.TargetDatabase,
            TargetTable = row.TargetTable,
            TargetColumn = row.TargetColumn,
        }).ToArray();
    }

    private static string BuildSqliteDatabaseName(ConnectionDefinition connection)
    {
        var fileName = Path.GetFileName(connection.Host?.Trim() ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        if (!string.IsNullOrWhiteSpace(connection.Name))
        {
            return connection.Name.Trim();
        }

        return "main";
    }

    private static string EscapeSqliteStringLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static int? TryParseSqliteMaxLength(string dataType)
    {
        var start = dataType.IndexOf('(');
        var end = dataType.IndexOf(')', start + 1);
        if (start < 0 || end <= start + 1)
        {
            return null;
        }

        var content = dataType[(start + 1)..end].Trim();
        return int.TryParse(content, out var maxLength) && maxLength > 0
            ? maxLength
            : null;
    }

    private static async Task<string?> ResolveSqlitePrimaryKeyColumnAsync(DbConnection db, string tableName, IDictionary<string, string?> cache)
    {
        if (cache.TryGetValue(tableName, out var cached))
        {
            return cached;
        }

        var column = await db.ExecuteScalarAsync<string?>($@"
            SELECT name
            FROM pragma_table_xinfo('{EscapeSqliteStringLiteral(tableName)}')
            WHERE pk > 0
            ORDER BY pk
            LIMIT 1;");

        cache[tableName] = column;
        return column;
    }
}