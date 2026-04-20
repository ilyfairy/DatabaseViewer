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
                ORDER BY name;")).ToArray(),
            DatabaseProviderType.MySql => (await db.QueryAsync<string>(@"
                SELECT SCHEMA_NAME
                FROM information_schema.SCHEMATA
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

    public async Task<IReadOnlyList<DbTableInfo>> GetTablesAsync(ConnectionDefinition connection, string databaseName, bool includeSystemObjects = false, bool includeRowCounts = true)
    {
        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        if (connection.ProviderType == DatabaseProviderType.SqlServer)
        {
            var sql = includeRowCounts
                ? @"
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
                WHERE (@includeSystemObjects = 1 OR t.is_ms_shipped = 0)
                GROUP BY s.name, t.name, ep.value
                ORDER BY s.name, t.name;"
                : @"
                SELECT
                    s.name AS SchemaName,
                    t.name AS TableName,
                    CAST(ep.value AS nvarchar(4000)) AS Comment,
                    CAST(NULL AS int) AS TableRowCount
                FROM sys.tables t
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.major_id = t.object_id
                    AND ep.minor_id = 0
                    AND ep.name = 'MS_Description'
                WHERE (@includeSystemObjects = 1 OR t.is_ms_shipped = 0)
                ORDER BY s.name, t.name;";

            var rows = await db.QueryAsync(sql, new { includeSystemObjects });
            return rows.Select(row => new DbTableInfo
            {
                DatabaseName = databaseName,
                SchemaName = row.SchemaName,
                TableName = row.TableName,
                ObjectType = DbObjectType.Table,
                Comment = row.Comment,
                RowCount = row.TableRowCount,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.PostgreSql)
        {
            var postgreSqlSql = includeRowCounts
                ? @"
                SELECT
                    t.table_schema AS ""SchemaName"",
                    t.table_name AS ""TableName"",
                    obj_description(c.oid, 'pg_class') AS ""Comment"",
                    CAST(CASE WHEN c.reltuples >= 0 THEN c.reltuples ELSE NULL END AS integer) AS ""TableRowCount""
                FROM information_schema.tables t
                INNER JOIN pg_namespace n ON n.nspname = t.table_schema
                INNER JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = t.table_name
                WHERE t.table_catalog = @databaseName
                  AND t.table_type = 'BASE TABLE'
                  AND t.table_schema NOT IN ('pg_catalog', 'information_schema')
                                ORDER BY t.table_schema, t.table_name;"
                                : @"
                                SELECT
                                        t.table_schema AS ""SchemaName"",
                                        t.table_name AS ""TableName"",
                                        obj_description(c.oid, 'pg_class') AS ""Comment"",
                                        CAST(NULL AS integer) AS ""TableRowCount""
                                FROM information_schema.tables t
                                INNER JOIN pg_namespace n ON n.nspname = t.table_schema
                                INNER JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = t.table_name
                                WHERE t.table_catalog = @databaseName
                                    AND t.table_type = 'BASE TABLE'
                                    AND t.table_schema NOT IN ('pg_catalog', 'information_schema')
                                ORDER BY t.table_schema, t.table_name;";

                        var postgreSqlRows = await db.QueryAsync(postgreSqlSql, new { databaseName });

            return postgreSqlRows.Select(row => new DbTableInfo
            {
                DatabaseName = databaseName,
                SchemaName = row.SchemaName,
                TableName = row.TableName,
                ObjectType = DbObjectType.Table,
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
                ObjectType = DbObjectType.Table,
                Comment = null,
                RowCount = null,
            }).ToArray();
        }

        var mySqlRows = await db.QueryAsync(@"
            SHOW TABLE STATUS
            WHERE Comment <> 'VIEW';");

        return mySqlRows.Select(row => new DbTableInfo
        {
            DatabaseName = databaseName,
            SchemaName = null,
            TableName = row.Name,
            ObjectType = DbObjectType.Table,
            Comment = string.IsNullOrWhiteSpace((string?)row.Comment) ? null : row.Comment,
            RowCount = includeRowCounts ? ConvertToNullableInt32(row.Rows) : null,
        }).ToArray();
    }

    public async Task<IReadOnlyList<DbTableInfo>> GetViewsAsync(ConnectionDefinition connection, string databaseName, bool includeSystemObjects = false)
    {
        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        if (connection.ProviderType == DatabaseProviderType.SqlServer)
        {
            var sql = includeSystemObjects
                ? @"
                SELECT
                    s.name AS SchemaName,
                    v.name AS ViewName,
                    CAST(ep.value AS nvarchar(4000)) AS Comment
                FROM sys.all_views v
                INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.major_id = v.object_id
                    AND ep.minor_id = 0
                    AND ep.name = 'MS_Description'
                ORDER BY s.name, v.name;"
                : @"
                SELECT
                    s.name AS SchemaName,
                    v.name AS ViewName,
                    CAST(ep.value AS nvarchar(4000)) AS Comment
                FROM sys.views v
                INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.major_id = v.object_id
                    AND ep.minor_id = 0
                    AND ep.name = 'MS_Description'
                WHERE v.is_ms_shipped = 0
                ORDER BY s.name, v.name;";

            var rows = await db.QueryAsync(sql);
            return rows.Select(row => new DbTableInfo
            {
                DatabaseName = databaseName,
                SchemaName = row.SchemaName,
                TableName = row.ViewName,
                ObjectType = DbObjectType.View,
                Comment = string.IsNullOrWhiteSpace((string?)row.Comment) ? null : row.Comment,
                RowCount = null,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.MySql)
        {
            await using var command = db.CreateCommand();
            command.CommandText = "SHOW FULL TABLES WHERE Table_type = 'VIEW';";

            var views = new List<DbTableInfo>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var viewName = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(viewName))
                {
                    continue;
                }

                views.Add(new DbTableInfo
                {
                    DatabaseName = databaseName,
                    SchemaName = null,
                    TableName = viewName,
                    ObjectType = DbObjectType.View,
                    Comment = null,
                    RowCount = null,
                });
            }

            return views;
        }

        return Array.Empty<DbTableInfo>();
    }

    public async Task<IReadOnlyList<DbSynonymInfo>> GetSynonymsAsync(ConnectionDefinition connection, string databaseName, bool includeSystemObjects = false)
    {
        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        if (connection.ProviderType == DatabaseProviderType.SqlServer)
        {
            var sql = @"
                SELECT
                    s.name AS SchemaName,
                    sy.name AS SynonymName,
                    sy.base_object_name AS BaseObjectName,
                    PARSENAME(sy.base_object_name, 4) AS TargetServerName,
                    PARSENAME(sy.base_object_name, 3) AS TargetDatabaseName,
                    PARSENAME(sy.base_object_name, 2) AS TargetSchemaName,
                    PARSENAME(sy.base_object_name, 1) AS TargetObjectName
                FROM sys.synonyms sy
                INNER JOIN sys.schemas s ON s.schema_id = sy.schema_id
                ORDER BY s.name, sy.name;";

            var rows = await db.QueryAsync(sql);
            return rows.Select(row => new DbSynonymInfo
            {
                DatabaseName = databaseName,
                SchemaName = row.SchemaName,
                SynonymName = row.SynonymName,
                BaseObjectName = row.BaseObjectName,
                TargetServerName = row.TargetServerName,
                TargetDatabaseName = row.TargetDatabaseName,
                TargetSchemaName = row.TargetSchemaName,
                TargetObjectName = row.TargetObjectName,
            }).ToArray();
        }

        return Array.Empty<DbSynonymInfo>();
    }

    public async Task<IReadOnlyList<DbSequenceInfo>> GetSequencesAsync(ConnectionDefinition connection, string databaseName)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<DbSequenceInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                sc.name AS SchemaName,
                sq.name AS SequenceName,
                TYPE_NAME(sq.user_type_id) AS DataType,
                CONVERT(nvarchar(128), sq.start_value) AS StartValue,
                CONVERT(nvarchar(128), sq.increment) AS IncrementValue
            FROM sys.sequences sq
            INNER JOIN sys.schemas sc ON sc.schema_id = sq.schema_id
            ORDER BY sc.name, sq.name;");

        return rows.Select(row => new DbSequenceInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            SequenceName = row.SequenceName,
            DataType = row.DataType,
            StartValue = row.StartValue,
            IncrementValue = row.IncrementValue,
        }).ToArray();
    }

    public async Task<IReadOnlyList<DbRuleInfo>> GetRulesAsync(ConnectionDefinition connection, string databaseName)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<DbRuleInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                sc.name AS SchemaName,
                o.name AS RuleName,
                OBJECT_DEFINITION(o.object_id) AS Definition
            FROM sys.objects o
            INNER JOIN sys.schemas sc ON sc.schema_id = o.schema_id
            WHERE o.type = 'R'
            ORDER BY sc.name, o.name;");

        return rows.Select(row => new DbRuleInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            RuleName = row.RuleName,
            Definition = row.Definition,
        }).ToArray();
    }

    public async Task<IReadOnlyList<DbDefaultInfo>> GetDefaultsAsync(ConnectionDefinition connection, string databaseName)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<DbDefaultInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                sc.name AS SchemaName,
                o.name AS DefaultName,
                OBJECT_DEFINITION(o.object_id) AS Definition
            FROM sys.objects o
            INNER JOIN sys.schemas sc ON sc.schema_id = o.schema_id
            WHERE o.type = 'D'
            ORDER BY sc.name, o.name;");

        return rows.Select(row => new DbDefaultInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            DefaultName = row.DefaultName,
            Definition = row.Definition,
        }).ToArray();
    }

    public async Task<IReadOnlyList<DbUserDefinedTypeInfo>> GetUserDefinedTypesAsync(ConnectionDefinition connection, string databaseName)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<DbUserDefinedTypeInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                sc.name AS SchemaName,
                t.name AS TypeName,
                CASE
                    WHEN t.is_table_type = 1 THEN 'table type'
                    ELSE bt.name
                END AS BaseTypeName,
                CAST(t.is_table_type AS bit) AS IsTableType
            FROM sys.types t
            INNER JOIN sys.schemas sc ON sc.schema_id = t.schema_id
            LEFT JOIN sys.types bt ON bt.user_type_id = t.system_type_id AND bt.user_type_id = bt.system_type_id
            WHERE t.is_user_defined = 1
            ORDER BY sc.name, t.name;");

        return rows.Select(row => new DbUserDefinedTypeInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            TypeName = row.TypeName,
            BaseTypeName = row.BaseTypeName,
            IsTableType = row.IsTableType,
        }).ToArray();
    }

    public async Task<IReadOnlyList<DbDatabaseTriggerInfo>> GetDatabaseTriggersAsync(ConnectionDefinition connection, string databaseName)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<DbDatabaseTriggerInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                s.name AS SchemaName,
                tr.name AS TriggerName,
                CASE WHEN tr.is_instead_of_trigger = 1 THEN 'INSTEAD OF' ELSE 'AFTER' END AS TriggerTiming,
                STUFF((
                    SELECT DISTINCT ', ' + te.type_desc
                    FROM sys.trigger_events te
                    WHERE te.object_id = tr.object_id
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'), 1, 2, '') AS TriggerEvent
            FROM sys.triggers tr
            INNER JOIN sys.objects o ON o.object_id = tr.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE tr.parent_class = 0
            ORDER BY s.name, tr.name;");

        return rows.Select(row => new DbDatabaseTriggerInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            TriggerName = row.TriggerName,
            TriggerTiming = row.TriggerTiming,
            TriggerEvent = row.TriggerEvent,
        }).ToArray();
    }

    public async Task<IReadOnlyList<DbXmlSchemaCollectionInfo>> GetXmlSchemaCollectionsAsync(ConnectionDefinition connection, string databaseName)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<DbXmlSchemaCollectionInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                s.name AS SchemaName,
                xsc.name AS CollectionName,
                COUNT(xc.xml_collection_id) AS XmlNamespaceCount
            FROM sys.xml_schema_collections xsc
            INNER JOIN sys.schemas s ON s.schema_id = xsc.schema_id
            LEFT JOIN sys.xml_schema_components xc ON xc.xml_collection_id = xsc.xml_collection_id
            GROUP BY s.name, xsc.name
            ORDER BY s.name, xsc.name;");

        return rows.Select(row => new DbXmlSchemaCollectionInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            CollectionName = row.CollectionName,
            XmlNamespaceCount = row.XmlNamespaceCount,
        }).ToArray();
    }

    public async Task<IReadOnlyList<DbAssemblyInfo>> GetAssembliesAsync(ConnectionDefinition connection, string databaseName)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<DbAssemblyInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                a.name AS AssemblyName,
                a.clr_name AS ClrName,
                CASE a.permission_set
                    WHEN 1 THEN 'SAFE'
                    WHEN 2 THEN 'EXTERNAL_ACCESS'
                    WHEN 3 THEN 'UNSAFE'
                    ELSE CAST(a.permission_set AS nvarchar(10))
                END AS PermissionSet,
                a.is_visible AS IsVisible
            FROM sys.assemblies a
            ORDER BY a.name;");

        return rows.Select(row => new DbAssemblyInfo
        {
            DatabaseName = databaseName,
            AssemblyName = (string)row.AssemblyName,
            ClrName = (string)row.ClrName,
            PermissionSet = (string)row.PermissionSet,
            IsVisible = (bool)row.IsVisible,
        }).ToArray();
    }

    public async Task<DbCatalogObjectDetailInfo?> GetCatalogObjectDetailAsync(ConnectionDefinition connection, string databaseName, DbCatalogObjectType objectType, string? schemaName, string objectName)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return null;
        }

        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        var resolvedSchema = string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName;

        return objectType switch
        {
            DbCatalogObjectType.Synonym => await GetSynonymDetailAsync(db, databaseName, resolvedSchema, objectName),
            DbCatalogObjectType.Sequence => await GetSequenceDetailAsync(db, databaseName, resolvedSchema, objectName),
            DbCatalogObjectType.Rule => await GetRuleDetailAsync(db, databaseName, resolvedSchema, objectName),
            DbCatalogObjectType.Default => await GetDefaultDetailAsync(db, databaseName, resolvedSchema, objectName),
            DbCatalogObjectType.UserDefinedType => await GetUserDefinedTypeDetailAsync(db, databaseName, resolvedSchema, objectName),
            DbCatalogObjectType.DatabaseTrigger => await GetDatabaseTriggerDetailAsync(db, databaseName, resolvedSchema, objectName),
            DbCatalogObjectType.XmlSchemaCollection => await GetXmlSchemaCollectionDetailAsync(db, databaseName, resolvedSchema, objectName),
            DbCatalogObjectType.Assembly => await GetAssemblyDetailAsync(db, databaseName, objectName),
            _ => null,
        };
    }

    private static async Task<DbCatalogObjectDetailInfo?> GetSynonymDetailAsync(DbConnection db, string databaseName, string schemaName, string objectName)
    {
        var row = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                s.name AS SchemaName,
                sy.name AS SynonymName,
                sy.base_object_name AS BaseObjectName,
                PARSENAME(sy.base_object_name, 4) AS TargetServerName,
                PARSENAME(sy.base_object_name, 3) AS TargetDatabaseName,
                PARSENAME(sy.base_object_name, 2) AS TargetSchemaName,
                PARSENAME(sy.base_object_name, 1) AS TargetObjectName
            FROM sys.synonyms sy
            INNER JOIN sys.schemas s ON s.schema_id = sy.schema_id
            WHERE s.name = @schemaName
              AND sy.name = @objectName;",
            new { schemaName, objectName });

        if (row is null)
        {
            return null;
        }

        return new DbCatalogObjectDetailInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            ObjectName = row.SynonymName,
            ObjectType = DbCatalogObjectType.Synonym,
            Title = string.IsNullOrWhiteSpace((string?)row.SchemaName) ? (string)row.SynonymName : $"{row.SchemaName}.{row.SynonymName}",
            Summary = row.BaseObjectName,
            Properties =
            [
                new DbCatalogObjectProperty { Label = "同义词名称", Value = row.SynonymName },
                new DbCatalogObjectProperty { Label = "同义词架构", Value = row.SchemaName },
                new DbCatalogObjectProperty { Label = "目标服务器", Value = row.TargetServerName },
                new DbCatalogObjectProperty { Label = "目标数据库", Value = row.TargetDatabaseName },
                new DbCatalogObjectProperty { Label = "目标架构", Value = row.TargetSchemaName },
                new DbCatalogObjectProperty { Label = "目标对象", Value = row.TargetObjectName },
                new DbCatalogObjectProperty { Label = "基对象名称", Value = row.BaseObjectName },
            ],
        };
    }

    private static async Task<DbCatalogObjectDetailInfo?> GetSequenceDetailAsync(DbConnection db, string databaseName, string schemaName, string objectName)
    {
        var row = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                sc.name AS SchemaName,
                sq.name AS SequenceName,
                TYPE_NAME(sq.user_type_id) AS DataType,
                CONVERT(nvarchar(128), sq.start_value) AS StartValue,
                CONVERT(nvarchar(128), sq.increment) AS IncrementValue,
                CONVERT(nvarchar(128), sq.minimum_value) AS MinimumValue,
                CONVERT(nvarchar(128), sq.maximum_value) AS MaximumValue,
                CONVERT(nvarchar(128), sq.current_value) AS CurrentValue,
                CAST(sq.is_cycling AS bit) AS IsCycling,
                CAST(sq.is_cached AS bit) AS IsCached,
                CONVERT(nvarchar(128), sq.cache_size) AS CacheSize
            FROM sys.sequences sq
            INNER JOIN sys.schemas sc ON sc.schema_id = sq.schema_id
            WHERE sc.name = @schemaName
              AND sq.name = @objectName;",
            new { schemaName, objectName });

        if (row is null)
        {
            return null;
        }

        return new DbCatalogObjectDetailInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            ObjectName = row.SequenceName,
            ObjectType = DbCatalogObjectType.Sequence,
            Title = $"{row.SchemaName}.{row.SequenceName}",
            Summary = row.DataType,
            Properties =
            [
                new DbCatalogObjectProperty { Label = "序列名称", Value = row.SequenceName },
                new DbCatalogObjectProperty { Label = "架构", Value = row.SchemaName },
                new DbCatalogObjectProperty { Label = "数据类型", Value = row.DataType },
                new DbCatalogObjectProperty { Label = "起始值", Value = row.StartValue },
                new DbCatalogObjectProperty { Label = "增量", Value = row.IncrementValue },
                new DbCatalogObjectProperty { Label = "最小值", Value = row.MinimumValue },
                new DbCatalogObjectProperty { Label = "最大值", Value = row.MaximumValue },
                new DbCatalogObjectProperty { Label = "当前值", Value = row.CurrentValue },
                new DbCatalogObjectProperty { Label = "循环", Value = (bool)row.IsCycling ? "是" : "否" },
                new DbCatalogObjectProperty { Label = "缓存", Value = (bool)row.IsCached ? $"是 ({row.CacheSize})" : "否" },
            ],
        };
    }

    private static async Task<DbCatalogObjectDetailInfo?> GetRuleDetailAsync(DbConnection db, string databaseName, string schemaName, string objectName)
    {
        var row = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                sc.name AS SchemaName,
                o.name AS RuleName,
                OBJECT_DEFINITION(o.object_id) AS Definition
            FROM sys.objects o
            INNER JOIN sys.schemas sc ON sc.schema_id = o.schema_id
            WHERE o.type = 'R'
              AND sc.name = @schemaName
              AND o.name = @objectName;",
            new { schemaName, objectName });

        if (row is null)
        {
            return null;
        }

        return new DbCatalogObjectDetailInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            ObjectName = row.RuleName,
            ObjectType = DbCatalogObjectType.Rule,
            Title = $"{row.SchemaName}.{row.RuleName}",
            Summary = "规则",
            Definition = row.Definition,
            Properties =
            [
                new DbCatalogObjectProperty { Label = "规则名称", Value = row.RuleName },
                new DbCatalogObjectProperty { Label = "架构", Value = row.SchemaName },
            ],
        };
    }

    private static async Task<DbCatalogObjectDetailInfo?> GetDefaultDetailAsync(DbConnection db, string databaseName, string schemaName, string objectName)
    {
        var row = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                sc.name AS SchemaName,
                o.name AS DefaultName,
                OBJECT_DEFINITION(o.object_id) AS Definition
            FROM sys.objects o
            INNER JOIN sys.schemas sc ON sc.schema_id = o.schema_id
            WHERE o.type = 'D'
              AND sc.name = @schemaName
              AND o.name = @objectName;",
            new { schemaName, objectName });

        if (row is null)
        {
            return null;
        }

        return new DbCatalogObjectDetailInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            ObjectName = row.DefaultName,
            ObjectType = DbCatalogObjectType.Default,
            Title = $"{row.SchemaName}.{row.DefaultName}",
            Summary = "默认值",
            Definition = row.Definition,
            Properties =
            [
                new DbCatalogObjectProperty { Label = "默认值名称", Value = row.DefaultName },
                new DbCatalogObjectProperty { Label = "架构", Value = row.SchemaName },
            ],
        };
    }

    private static async Task<DbCatalogObjectDetailInfo?> GetUserDefinedTypeDetailAsync(DbConnection db, string databaseName, string schemaName, string objectName)
    {
        var row = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                sc.name AS SchemaName,
                t.name AS TypeName,
                CASE
                    WHEN t.is_table_type = 1 THEN 'table type'
                    ELSE bt.name
                END AS BaseTypeName,
                CAST(t.is_table_type AS bit) AS IsTableType,
                CAST(t.is_nullable AS bit) AS IsNullable,
                t.max_length AS MaxLength,
                t.precision AS PrecisionValue,
                t.scale AS ScaleValue
            FROM sys.types t
            INNER JOIN sys.schemas sc ON sc.schema_id = t.schema_id
            LEFT JOIN sys.types bt ON bt.user_type_id = t.system_type_id AND bt.user_type_id = bt.system_type_id
            WHERE t.is_user_defined = 1
              AND sc.name = @schemaName
              AND t.name = @objectName;",
            new { schemaName, objectName });

        if (row is null)
        {
            return null;
        }

        return new DbCatalogObjectDetailInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            ObjectName = row.TypeName,
            ObjectType = DbCatalogObjectType.UserDefinedType,
            Title = $"{row.SchemaName}.{row.TypeName}",
            Summary = row.BaseTypeName,
            Properties =
            [
                new DbCatalogObjectProperty { Label = "类型名称", Value = row.TypeName },
                new DbCatalogObjectProperty { Label = "架构", Value = row.SchemaName },
                new DbCatalogObjectProperty { Label = "基础类型", Value = row.BaseTypeName },
                new DbCatalogObjectProperty { Label = "表类型", Value = (bool)row.IsTableType ? "是" : "否" },
                new DbCatalogObjectProperty { Label = "可空", Value = (bool)row.IsNullable ? "是" : "否" },
                new DbCatalogObjectProperty { Label = "最大长度", Value = Convert.ToString(row.MaxLength) },
                new DbCatalogObjectProperty { Label = "精度", Value = Convert.ToString(row.PrecisionValue) },
                new DbCatalogObjectProperty { Label = "小数位", Value = Convert.ToString(row.ScaleValue) },
            ],
        };
    }

    private static async Task<DbCatalogObjectDetailInfo?> GetDatabaseTriggerDetailAsync(DbConnection db, string databaseName, string schemaName, string objectName)
    {
        var row = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                s.name AS SchemaName,
                tr.name AS TriggerName,
                CASE WHEN tr.is_instead_of_trigger = 1 THEN 'INSTEAD OF' ELSE 'AFTER' END AS TriggerTiming,
                STUFF((
                    SELECT DISTINCT ', ' + te.type_desc
                    FROM sys.trigger_events te
                    WHERE te.object_id = tr.object_id
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'), 1, 2, '') AS TriggerEvent,
                OBJECT_DEFINITION(tr.object_id) AS Definition
            FROM sys.triggers tr
            INNER JOIN sys.objects o ON o.object_id = tr.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE tr.parent_class = 0
              AND s.name = @schemaName
              AND tr.name = @objectName;",
            new { schemaName, objectName });

        if (row is null)
        {
            return null;
        }

        return new DbCatalogObjectDetailInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            ObjectName = row.TriggerName,
            ObjectType = DbCatalogObjectType.DatabaseTrigger,
            Title = $"{row.SchemaName}.{row.TriggerName}",
            Summary = row.TriggerEvent,
            Definition = row.Definition,
            Properties =
            [
                new DbCatalogObjectProperty { Label = "触发器名称", Value = row.TriggerName },
                new DbCatalogObjectProperty { Label = "架构", Value = row.SchemaName },
                new DbCatalogObjectProperty { Label = "触发时机", Value = row.TriggerTiming },
                new DbCatalogObjectProperty { Label = "触发事件", Value = row.TriggerEvent },
            ],
        };
    }

    private static async Task<DbCatalogObjectDetailInfo?> GetXmlSchemaCollectionDetailAsync(DbConnection db, string databaseName, string schemaName, string objectName)
    {
        var row = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                s.name AS SchemaName,
                xsc.name AS CollectionName,
                xsc.xml_collection_id AS CollectionId,
                COUNT(xc.xml_collection_id) AS XmlNamespaceCount
            FROM sys.xml_schema_collections xsc
            INNER JOIN sys.schemas s ON s.schema_id = xsc.schema_id
            LEFT JOIN sys.xml_schema_components xc ON xc.xml_collection_id = xsc.xml_collection_id
            WHERE s.name = @schemaName
              AND xsc.name = @objectName
            GROUP BY s.name, xsc.name, xsc.xml_collection_id;",
            new { schemaName, objectName });

        if (row is null)
        {
            return null;
        }

        return new DbCatalogObjectDetailInfo
        {
            DatabaseName = databaseName,
            SchemaName = row.SchemaName,
            ObjectName = row.CollectionName,
            ObjectType = DbCatalogObjectType.XmlSchemaCollection,
            Title = $"{row.SchemaName}.{row.CollectionName}",
            Summary = $"{row.XmlNamespaceCount} namespaces",
            Properties =
            [
                new DbCatalogObjectProperty { Label = "集合名称", Value = row.CollectionName },
                new DbCatalogObjectProperty { Label = "架构", Value = row.SchemaName },
                new DbCatalogObjectProperty { Label = "集合 ID", Value = Convert.ToString(row.CollectionId) },
                new DbCatalogObjectProperty { Label = "命名空间数量", Value = Convert.ToString(row.XmlNamespaceCount) },
            ],
        };
    }

    private static async Task<DbCatalogObjectDetailInfo?> GetAssemblyDetailAsync(DbConnection db, string databaseName, string objectName)
    {
        var row = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                a.name AS AssemblyName,
                a.clr_name AS ClrName,
                CASE a.permission_set
                    WHEN 1 THEN 'SAFE'
                    WHEN 2 THEN 'EXTERNAL_ACCESS'
                    WHEN 3 THEN 'UNSAFE'
                    ELSE CAST(a.permission_set AS nvarchar(10))
                END AS PermissionSet,
                a.is_visible AS IsVisible,
                a.create_date AS CreateDate,
                a.modify_date AS ModifyDate,
                CONVERT(nvarchar(40), ASSEMBLYPROPERTY(a.name, 'VersionMajor')) + '.' +
                CONVERT(nvarchar(40), ASSEMBLYPROPERTY(a.name, 'VersionMinor')) + '.' +
                CONVERT(nvarchar(40), ASSEMBLYPROPERTY(a.name, 'VersionBuild')) + '.' +
                CONVERT(nvarchar(40), ASSEMBLYPROPERTY(a.name, 'VersionRevision')) AS VersionString
            FROM sys.assemblies a
            WHERE a.name = @objectName;",
            new { objectName });

        if (row is null)
        {
            return null;
        }

        return new DbCatalogObjectDetailInfo
        {
            DatabaseName = databaseName,
            SchemaName = null,
            ObjectName = row.AssemblyName,
            ObjectType = DbCatalogObjectType.Assembly,
            Title = (string)row.AssemblyName,
            Summary = (string)row.PermissionSet,
            Properties =
            [
                new DbCatalogObjectProperty { Label = "程序集名称", Value = row.AssemblyName },
                new DbCatalogObjectProperty { Label = "CLR 名称", Value = row.ClrName },
                new DbCatalogObjectProperty { Label = "权限集", Value = row.PermissionSet },
                new DbCatalogObjectProperty { Label = "版本", Value = row.VersionString },
                new DbCatalogObjectProperty { Label = "可见", Value = (bool)row.IsVisible ? "是" : "否" },
                new DbCatalogObjectProperty { Label = "创建时间", Value = Convert.ToString(row.CreateDate) },
                new DbCatalogObjectProperty { Label = "修改时间", Value = Convert.ToString(row.ModifyDate) },
            ],
        };
    }

    public async Task<IReadOnlyList<TableIndexInfo>> GetObjectIndexesAsync(ConnectionDefinition connection, DbTableInfo table)
    {
        if (connection.ProviderType == DatabaseProviderType.Sqlite)
        {
            await using var sqliteDb = DbConnectionFactory.Create(connection, table.DatabaseName);
            await sqliteDb.OpenAsync();

            static string SqliteLiteral(string value) => $"'{value.Replace("'", "''")}'";

            var indexRows = await sqliteDb.QueryAsync($"PRAGMA index_list({SqliteLiteral(table.TableName)});");
            var results = new List<TableIndexInfo>();
            foreach (var row in indexRows)
            {
                var indexName = (string)row.name;
                var columnRows = await sqliteDb.QueryAsync($"PRAGMA index_info({SqliteLiteral(indexName)});");
                results.Add(new TableIndexInfo
                {
                    Name = indexName,
                    IsPrimaryKey = string.Equals((string?)row.origin, "pk", StringComparison.OrdinalIgnoreCase),
                    IsUnique = Convert.ToInt32(row.unique) == 1,
                    Columns = columnRows
                        .OrderBy(item => Convert.ToInt32(item.seqno))
                        .Select(item => (string)item.name)
                        .ToArray(),
                });
            }

            return results;
        }

        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<TableIndexInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, table.DatabaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                i.name AS IndexName,
                CAST(i.is_primary_key AS bit) AS IsPrimaryKey,
                CAST(i.is_unique AS bit) AS IsUnique,
                c.name AS ColumnName,
                ic.key_ordinal AS KeyOrdinal
            FROM sys.indexes i
            INNER JOIN sys.objects o ON o.object_id = i.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            LEFT JOIN sys.index_columns ic
                ON ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal > 0
            LEFT JOIN sys.columns c
                ON c.object_id = ic.object_id
                AND c.column_id = ic.column_id
            WHERE o.name = @objectName
              AND s.name = @schemaName
              AND o.type IN ('U', 'V')
              AND i.index_id > 0
              AND i.is_hypothetical = 0
              AND i.name IS NOT NULL
            ORDER BY i.name, ic.key_ordinal;",
            new { objectName = table.TableName, schemaName = table.SchemaName ?? "dbo" });

        return rows
            .GroupBy(row => (string)row.IndexName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TableIndexInfo
            {
                Name = group.Key,
                IsPrimaryKey = group.First().IsPrimaryKey,
                IsUnique = group.First().IsUnique,
                Columns = group
                    .Where(item => item.ColumnName is not null)
                    .OrderBy(item => (int?)item.KeyOrdinal ?? int.MaxValue)
                    .Select(item => (string)item.ColumnName)
                    .ToArray(),
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<TableTriggerInfo>> GetObjectTriggersAsync(ConnectionDefinition connection, DbTableInfo table)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<TableTriggerInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, table.DatabaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                tr.name AS TriggerName,
                CASE WHEN tr.is_instead_of_trigger = 1 THEN 'INSTEAD OF' ELSE 'AFTER' END AS TriggerTiming,
                STUFF((
                    SELECT DISTINCT ', ' + te.type_desc
                    FROM sys.trigger_events te
                    WHERE te.object_id = tr.object_id
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'), 1, 2, '') AS TriggerEvent
            FROM sys.triggers tr
            INNER JOIN sys.objects o ON o.object_id = tr.parent_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE tr.parent_class_desc = 'OBJECT_OR_COLUMN'
              AND o.name = @objectName
              AND s.name = @schemaName
              AND o.type IN ('U', 'V')
            ORDER BY tr.name;",
            new { objectName = table.TableName, schemaName = table.SchemaName ?? "dbo" });

        return rows.Select(row => new TableTriggerInfo
        {
            Name = row.TriggerName,
            Timing = row.TriggerTiming,
            Event = string.IsNullOrWhiteSpace((string?)row.TriggerEvent) ? null : row.TriggerEvent,
        }).ToArray();
    }

    public async Task<IReadOnlyList<TableStatisticInfo>> GetObjectStatisticsAsync(ConnectionDefinition connection, DbTableInfo table)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            return Array.Empty<TableStatisticInfo>();
        }

        await using var db = DbConnectionFactory.Create(connection, table.DatabaseName);
        await db.OpenAsync();

        var rows = await db.QueryAsync(@"
            SELECT
                st.name AS StatisticName,
                CAST(st.auto_created AS bit) AS IsAutoCreated,
                CAST(st.user_created AS bit) AS IsUserCreated,
                CAST(st.no_recompute AS bit) AS NoRecompute,
                st.filter_definition AS FilterDefinition,
                c.name AS ColumnName,
                sc.stats_column_id AS StatsColumnId
            FROM sys.stats st
            INNER JOIN sys.objects o ON o.object_id = st.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            LEFT JOIN sys.indexes i
                ON i.object_id = st.object_id
                AND i.index_id = st.stats_id
            LEFT JOIN sys.stats_columns sc
                ON sc.object_id = st.object_id
                AND sc.stats_id = st.stats_id
            LEFT JOIN sys.columns c
                ON c.object_id = sc.object_id
                AND c.column_id = sc.column_id
            WHERE o.name = @objectName
              AND s.name = @schemaName
              AND o.type IN ('U', 'V')
              AND i.index_id IS NULL
            ORDER BY st.name, sc.stats_column_id;",
            new { objectName = table.TableName, schemaName = table.SchemaName ?? "dbo" });

        return rows
            .GroupBy(row => (string)row.StatisticName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TableStatisticInfo
            {
                Name = group.Key,
                IsAutoCreated = group.First().IsAutoCreated,
                IsUserCreated = group.First().IsUserCreated,
                NoRecompute = group.First().NoRecompute,
                FilterDefinition = string.IsNullOrWhiteSpace((string?)group.First().FilterDefinition) ? null : group.First().FilterDefinition,
                Columns = group
                    .Where(item => item.ColumnName is not null)
                    .OrderBy(item => (int?)item.StatsColumnId ?? int.MaxValue)
                    .Select(item => (string)item.ColumnName)
                    .ToArray(),
            })
            .ToArray();
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
                    CAST(CASE
                        WHEN c.max_length = -1 THEN -1
                        WHEN ty.name IN ('nchar', 'nvarchar') AND c.max_length > 0 THEN c.max_length / 2
                        ELSE c.max_length
                    END AS int) AS MaxLength,
                    CAST(ep.value AS nvarchar(4000)) AS Comment,
                    CAST(c.precision AS int) AS NumericPrecision,
                    CAST(c.scale AS int) AS NumericScale
                FROM {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.columns c
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.objects o ON o.object_id = c.object_id
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.schemas s ON s.schema_id = o.schema_id
                INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.types ty ON ty.user_type_id = c.user_type_id
                LEFT JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.extended_properties ep
                    ON ep.class = 1
                    AND ep.major_id = c.object_id
                    AND ep.minor_id = c.column_id
                    AND ep.name = 'MS_Description'
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id
                    FROM {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.indexes i
                    INNER JOIN {SqlDialect.QuoteIdentifier(connection.ProviderType, table.DatabaseName)}.sys.index_columns ic
                        ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    WHERE i.is_primary_key = 1
                ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
                WHERE o.type IN ('U', 'V') AND o.name = @tableName AND s.name = @schemaName
                ORDER BY c.column_id;";

            var rows = await db.QueryAsync(sql, new { tableName = table.TableName, schemaName = table.SchemaName ?? "dbo" });
            return rows.Select(row => new ColumnSchema
            {
                Name = row.ColumnName,
                DataType = row.DataType,
                Comment = string.IsNullOrWhiteSpace((string?)row.Comment) ? null : row.Comment,
                NumericPrecision = row.NumericPrecision,
                NumericScale = row.NumericScale,
                IsPrimaryKey = row.IsPrimaryKey,
                IsNullable = row.IsNullable,
                IsAutoGenerated = row.IsAutoGenerated || string.Equals((string?)row.DataType, "timestamp", StringComparison.OrdinalIgnoreCase) || string.Equals((string?)row.DataType, "rowversion", StringComparison.OrdinalIgnoreCase),
                IsComputed = row.IsComputed,
                MaxLength = row.MaxLength is int length
                    ? length > 0
                        ? length
                        : length == -1
                            ? -1
                            : null
                    : null,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.PostgreSql)
        {
            var postgreSqlRows = await db.QueryAsync(@"
                SELECT
                    c.column_name AS ""ColumnName"",
                    CASE
                        WHEN c.data_type = 'USER-DEFINED' THEN c.udt_name
                        WHEN c.data_type = 'ARRAY' THEN c.udt_name
                        ELSE c.data_type
                    END AS ""DataType"",
                    pg_catalog.col_description(cls.oid, c.ordinal_position::int) AS ""Comment"",
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
                    ) AS ""IsPrimaryKey"",
                    CASE WHEN c.is_nullable = 'YES' THEN TRUE ELSE FALSE END AS ""IsNullable"",
                    CASE
                        WHEN COALESCE(c.column_default LIKE 'nextval(%', FALSE) OR c.is_identity = 'YES' THEN TRUE
                        ELSE FALSE
                    END AS ""IsAutoGenerated"",
                    CASE WHEN c.is_generated = 'ALWAYS' THEN TRUE ELSE FALSE END AS ""IsComputed"",
                    c.character_maximum_length AS ""MaxLength"",
                    c.numeric_precision AS ""NumericPrecision"",
                    c.numeric_scale AS ""NumericScale""
                FROM information_schema.columns c
                INNER JOIN pg_catalog.pg_namespace ns ON ns.nspname = c.table_schema
                INNER JOIN pg_catalog.pg_class cls ON cls.relname = c.table_name AND cls.relnamespace = ns.oid
                WHERE c.table_catalog = @databaseName
                  AND c.table_schema = @schemaName
                  AND c.table_name = @tableName
                ORDER BY c.ordinal_position;", new { databaseName = table.DatabaseName, schemaName = table.SchemaName ?? "public", tableName = table.TableName });

            return postgreSqlRows.Select(row => new ColumnSchema
            {
                Name = row.ColumnName,
                DataType = row.DataType,
                Comment = string.IsNullOrWhiteSpace((string?)row.Comment) ? null : row.Comment,
                NumericPrecision = row.NumericPrecision,
                NumericScale = row.NumericScale,
                IsPrimaryKey = row.IsPrimaryKey,
                IsNullable = row.IsNullable,
                IsAutoGenerated = row.IsAutoGenerated,
                IsComputed = row.IsComputed,
                MaxLength = row.MaxLength,
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.Sqlite)
        {
            var sqliteRows = await db.QueryAsync(@"
                SELECT
                    name AS ColumnName,
                    COALESCE(type, '') AS DataType,
                    CASE WHEN pk > 0 THEN CAST(1 AS integer) ELSE CAST(0 AS integer) END AS IsPrimaryKey,
                    CASE WHEN ""notnull"" = 1 OR pk > 0 THEN CAST(0 AS integer) ELSE CAST(1 AS integer) END AS IsNullable,
                    CASE WHEN pk > 0 AND upper(COALESCE(type, '')) LIKE '%INT%' THEN CAST(1 AS integer) ELSE CAST(0 AS integer) END AS IsAutoGenerated,
                    CASE WHEN hidden > 1 THEN CAST(1 AS integer) ELSE CAST(0 AS integer) END AS IsComputed
                FROM pragma_table_xinfo(@TableName)
                ORDER BY cid;", new { TableName = table.TableName });

            var sqliteColumns = sqliteRows.Select(row =>
            {
                var dataType = string.IsNullOrWhiteSpace((string?)row.DataType) ? "TEXT" : (string)row.DataType;
                return new ColumnSchema
                {
                    Name = row.ColumnName,
                    DataType = dataType,
                    NumericPrecision = TryParseSqliteNumericPrecision(dataType),
                    NumericScale = TryParseSqliteNumericScale(dataType),
                    IsPrimaryKey = Convert.ToInt32(row.IsPrimaryKey) != 0,
                    IsNullable = Convert.ToInt32(row.IsNullable) != 0,
                    IsAutoGenerated = Convert.ToInt32(row.IsAutoGenerated) != 0,
                    IsComputed = Convert.ToInt32(row.IsComputed) != 0,
                    MaxLength = TryParseSqliteMaxLength(dataType),
                };
            }).ToList();

            // SQLite 表如果没有显式主键，注入隐式 rowid 列作为虚拟主键，使编辑器可以通过 rowid 更新/删除行。
            if (!sqliteColumns.Any(column => column.IsPrimaryKey))
            {
                sqliteColumns.Insert(0, new ColumnSchema
                {
                    Name = "rowid",
                    DataType = "INTEGER",
                    IsPrimaryKey = true,
                    IsNullable = false,
                    IsAutoGenerated = true,
                    IsHiddenRowId = true,
                });
            }

            return sqliteColumns;
        }

        var mySqlRows = await db.QueryAsync(@"
            SELECT
                COLUMN_NAME AS ColumnName,
                DATA_TYPE AS DataType,
                COLUMN_COMMENT AS Comment,
                COLUMN_KEY = 'PRI' AS IsPrimaryKey,
                IS_NULLABLE = 'YES' AS IsNullable,
                (EXTRA LIKE '%auto_increment%') AS IsAutoGenerated,
                (GENERATION_EXPRESSION IS NOT NULL AND GENERATION_EXPRESSION <> '') AS IsComputed,
                CHARACTER_MAXIMUM_LENGTH AS MaxLength,
                NUMERIC_PRECISION AS NumericPrecision,
                NUMERIC_SCALE AS NumericScale
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @databaseName AND TABLE_NAME = @tableName
            ORDER BY ORDINAL_POSITION;", new { databaseName = table.DatabaseName, tableName = table.TableName });

        return mySqlRows.Select(row => new ColumnSchema
        {
            Name = row.ColumnName,
            DataType = row.DataType,
            Comment = string.IsNullOrWhiteSpace((string?)row.Comment) ? null : row.Comment,
            NumericPrecision = ConvertToNullableInt32(row.NumericPrecision),
            NumericScale = ConvertToNullableInt32(row.NumericScale),
            IsPrimaryKey = ConvertToBoolean(row.IsPrimaryKey),
            IsNullable = ConvertToBoolean(row.IsNullable),
            IsAutoGenerated = ConvertToBoolean(row.IsAutoGenerated),
            IsComputed = ConvertToBoolean(row.IsComputed),
            MaxLength = ConvertToNullableInt32(row.MaxLength),
        }).ToArray();
    }

    private static bool ConvertToBoolean(object? value)
    {
        if (value is null || value is DBNull)
        {
            return false;
        }

        return value switch
        {
            bool booleanValue => booleanValue,
            byte byteValue => byteValue != 0,
            sbyte sbyteValue => sbyteValue != 0,
            short shortValue => shortValue != 0,
            ushort ushortValue => ushortValue != 0,
            int intValue => intValue != 0,
            uint uintValue => uintValue != 0,
            long longValue => longValue != 0,
            ulong ulongValue => ulongValue != 0,
            decimal decimalValue => decimalValue != 0,
            string stringValue when bool.TryParse(stringValue, out var parsedBool) => parsedBool,
            string stringValue when long.TryParse(stringValue, out var parsedLong) => parsedLong != 0,
            _ => Convert.ToDecimal(value) != 0,
        };
    }

    private static int? ConvertToNullableInt32(object? value)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        var numericValue = Convert.ToDecimal(value);
        if (numericValue < int.MinValue || numericValue > int.MaxValue)
        {
            return null;
        }

        return decimal.ToInt32(decimal.Truncate(numericValue));
    }

    private static decimal? ConvertToNullableDecimal(object? value)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        return Convert.ToDecimal(value);
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
            // 使用 pg_catalog 而非 information_schema，正确配对复合外键的列顺序。
            var postgreSqlRows = await db.QueryAsync(@"
                SELECT
                    current_database() AS ""SourceDatabase"",
                    sn.nspname AS ""SourceSchema"",
                    sc.relname AS ""SourceTable"",
                    sa.attname AS ""SourceColumn"",
                    current_database() AS ""TargetDatabase"",
                    tn.nspname AS ""TargetSchema"",
                    tc.relname AS ""TargetTable"",
                    ta.attname AS ""TargetColumn""
                FROM pg_constraint con
                INNER JOIN pg_class sc ON sc.oid = con.conrelid
                INNER JOIN pg_namespace sn ON sn.oid = sc.relnamespace
                INNER JOIN pg_class tc ON tc.oid = con.confrelid
                INNER JOIN pg_namespace tn ON tn.oid = tc.relnamespace
                CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS cols(source_attnum, target_attnum, ord)
                INNER JOIN pg_attribute sa ON sa.attrelid = con.conrelid AND sa.attnum = cols.source_attnum
                INNER JOIN pg_attribute ta ON ta.attrelid = con.confrelid AND ta.attnum = cols.target_attnum
                WHERE con.contype = 'f'
                ORDER BY con.oid, cols.ord;");

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
                var sqliteRows = await db.QueryAsync(@"
                    SELECT
                        ""table"" AS TargetTable,
                        ""from"" AS SourceColumn,
                        COALESCE(""to"", '') AS TargetColumn
                    FROM pragma_foreign_key_list(@TableName)
                    ORDER BY id, seq;", new { TableName = tableName });

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

    private static int? TryParseSqliteNumericPrecision(string dataType)
    {
        var parts = ParseSqliteTypeArguments(dataType);
        return parts.Length switch
        {
            >= 2 when int.TryParse(parts[0], out var precision) => precision,
            _ => null,
        };
    }

    private static int? TryParseSqliteNumericScale(string dataType)
    {
        var parts = ParseSqliteTypeArguments(dataType);
        return parts.Length switch
        {
            >= 2 when int.TryParse(parts[1], out var scale) => scale,
            _ => null,
        };
    }

    private static string[] ParseSqliteTypeArguments(string dataType)
    {
        var start = dataType.IndexOf('(');
        var end = dataType.IndexOf(')', start + 1);
        if (start < 0 || end <= start + 1)
        {
            return Array.Empty<string>();
        }

        return dataType[(start + 1)..end]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<string?> ResolveSqlitePrimaryKeyColumnAsync(DbConnection db, string tableName, IDictionary<string, string?> cache)
    {
        if (cache.TryGetValue(tableName, out var cached))
        {
            return cached;
        }

        var column = await db.ExecuteScalarAsync<string?>(@"
            SELECT name
            FROM pragma_table_xinfo(@TableName)
            WHERE pk > 0
            ORDER BY pk
            LIMIT 1;", new { TableName = tableName });

        cache[tableName] = column;
        return column;
    }

    /// <summary>
    /// 获取指定数据库下的所有存储过程和函数。
    /// </summary>
    public async Task<IReadOnlyList<DbRoutineInfo>> GetRoutinesAsync(ConnectionDefinition connection, string databaseName)
    {
        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        if (connection.ProviderType == DatabaseProviderType.SqlServer)
        {
            var rows = await db.QueryAsync(@"
                SELECT
                    s.name AS SchemaName,
                    o.name AS RoutineName,
                    CASE o.type
                        WHEN 'P'  THEN 'Procedure'
                        WHEN 'PC' THEN 'Procedure'
                        WHEN 'FN' THEN 'ScalarFunction'
                        WHEN 'IF' THEN 'TableFunction'
                        WHEN 'TF' THEN 'TableFunction'
                        WHEN 'AF' THEN 'AggregateFunction'
                        ELSE 'Unknown'
                    END AS RoutineType
                FROM sys.objects o
                INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE o.type IN ('P', 'PC', 'FN', 'IF', 'TF', 'AF')
                  AND o.is_ms_shipped = 0
                ORDER BY o.type, s.name, o.name;");

            // 查询所有非系统存储过程/函数的参数
            var paramRows = await db.QueryAsync(@"
                SELECT
                    s.name AS SchemaName,
                    o.name AS RoutineName,
                    p.name AS ParamName,
                    t.name AS DataType,
                    CASE WHEN p.is_output = 1 THEN 'OUT' ELSE 'IN' END AS Direction,
                    p.has_default_value AS HasDefault,
                    p.default_value AS DefaultValue
                FROM sys.parameters p
                INNER JOIN sys.objects o ON o.object_id = p.object_id
                INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
                INNER JOIN sys.types t ON t.user_type_id = p.user_type_id
                WHERE o.type IN ('P', 'PC', 'FN', 'IF', 'TF', 'AF')
                  AND o.is_ms_shipped = 0
                  AND p.parameter_id > 0
                ORDER BY o.object_id, p.parameter_id;");

            var paramLookup = paramRows
                .GroupBy(p => $"{(string)p.SchemaName}.{(string)p.RoutineName}")
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => new DbRoutineParameter
                    {
                        Name = (string)p.ParamName,
                        DataType = (string)p.DataType,
                        Direction = (string)p.Direction,
                        DefaultValue = p.HasDefault ? p.DefaultValue?.ToString() : null,
                    }).ToList());

            return rows.Select(row =>
            {
                var key = $"{(string)row.SchemaName}.{(string)row.RoutineName}";
                return new DbRoutineInfo
                {
                    DatabaseName = databaseName,
                    SchemaName = row.SchemaName,
                    RoutineName = row.RoutineName,
                    RoutineType = row.RoutineType,
                    Parameters = paramLookup.TryGetValue(key, out var parameters) ? parameters : [],
                };
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.PostgreSql)
        {
            var rows = await db.QueryAsync(@"
                SELECT
                    n.nspname AS ""SchemaName"",
                    p.proname AS ""RoutineName"",
                    CASE p.prokind
                        WHEN 'p' THEN 'Procedure'
                        WHEN 'f' THEN 'ScalarFunction'
                        WHEN 'a' THEN 'AggregateFunction'
                        WHEN 'w' THEN 'ScalarFunction'
                        ELSE 'Unknown'
                    END AS ""RoutineType""
                FROM pg_proc p
                INNER JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
                ORDER BY p.prokind, n.nspname, p.proname;");

            // 查询 PostgreSQL 函数/存储过程参数
            var pgParamRows = await db.QueryAsync(@"
                SELECT
                    n.nspname AS ""SchemaName"",
                    p.proname AS ""RoutineName"",
                    pa.parameter_name AS ""ParamName"",
                    pa.data_type AS ""DataType"",
                    pa.parameter_mode AS ""Direction"",
                    pa.parameter_default AS ""DefaultValue""
                FROM information_schema.parameters pa
                INNER JOIN pg_proc p ON p.proname = pa.specific_name
                    AND p.oid::regprocedure::text LIKE '%' || pa.specific_name || '%'
                INNER JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
                ORDER BY pa.specific_name, pa.ordinal_position;");

            var pgParamLookup = pgParamRows
                .GroupBy(p => $"{(string)p.SchemaName}.{(string)p.RoutineName}")
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => new DbRoutineParameter
                    {
                        Name = (string)(p.ParamName ?? ""),
                        DataType = (string)p.DataType,
                        Direction = ((string)(p.Direction ?? "IN")).ToUpperInvariant() switch
                        {
                            "INOUT" => "INOUT",
                            "OUT" => "OUT",
                            _ => "IN",
                        },
                        DefaultValue = p.DefaultValue?.ToString(),
                    }).ToList());

            return rows.Select(row =>
            {
                var key = $"{(string)row.SchemaName}.{(string)row.RoutineName}";
                return new DbRoutineInfo
                {
                    DatabaseName = databaseName,
                    SchemaName = row.SchemaName,
                    RoutineName = row.RoutineName,
                    RoutineType = row.RoutineType,
                    Parameters = pgParamLookup.TryGetValue(key, out var parameters) ? parameters : [],
                };
            }).ToArray();
        }

        if (connection.ProviderType == DatabaseProviderType.MySql)
        {
            var rows = await db.QueryAsync(@"
                SELECT
                    ROUTINE_NAME AS RoutineName,
                    CASE ROUTINE_TYPE
                        WHEN 'PROCEDURE' THEN 'Procedure'
                        WHEN 'FUNCTION'  THEN 'ScalarFunction'
                        ELSE 'Unknown'
                    END AS RoutineType
                FROM information_schema.ROUTINES
                WHERE ROUTINE_SCHEMA = @databaseName
                ORDER BY ROUTINE_TYPE, ROUTINE_NAME;", new { databaseName });

            // 查询 MySQL 存储过程/函数参数
            var mysqlParamRows = await db.QueryAsync(@"
                SELECT
                    SPECIFIC_NAME AS RoutineName,
                    PARAMETER_NAME AS ParamName,
                    DATA_TYPE AS DataType,
                    PARAMETER_MODE AS Direction,
                    '' AS DefaultValue
                FROM information_schema.PARAMETERS
                WHERE SPECIFIC_SCHEMA = @databaseName
                  AND ORDINAL_POSITION > 0
                ORDER BY SPECIFIC_NAME, ORDINAL_POSITION;", new { databaseName });

            var mysqlParamLookup = mysqlParamRows
                .GroupBy(p => (string)p.RoutineName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => new DbRoutineParameter
                    {
                        Name = (string)(p.ParamName ?? ""),
                        DataType = (string)p.DataType,
                        Direction = ((string)(p.Direction ?? "IN")).ToUpperInvariant() switch
                        {
                            "INOUT" => "INOUT",
                            "OUT" => "OUT",
                            _ => "IN",
                        },
                        DefaultValue = null,
                    }).ToList());

            return rows.Select(row =>
            {
                return new DbRoutineInfo
                {
                    DatabaseName = databaseName,
                    SchemaName = databaseName,
                    RoutineName = row.RoutineName,
                    RoutineType = row.RoutineType,
                    Parameters = mysqlParamLookup.TryGetValue((string)row.RoutineName, out var parameters) ? parameters : [],
                };
            }).ToArray();
        }

        // SQLite 不支持存储过程和函数
        return Array.Empty<DbRoutineInfo>();
    }

    /// <summary>
    /// 获取存储过程或函数的源代码定义。
    /// </summary>
    public async Task<string?> GetRoutineSourceAsync(ConnectionDefinition connection, string databaseName, string? schemaName, string routineName, string routineType)
    {
        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        if (connection.ProviderType == DatabaseProviderType.SqlServer)
        {
            var source = await db.QueryFirstOrDefaultAsync<string>(@"
                SELECT m.definition
                FROM sys.sql_modules m
                INNER JOIN sys.objects o ON o.object_id = m.object_id
                INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE o.name = @routineName
                  AND s.name = @schemaName;", new { routineName, schemaName = schemaName ?? "dbo" });
            return source;
        }

        if (connection.ProviderType == DatabaseProviderType.PostgreSql)
        {
            var source = await db.QueryFirstOrDefaultAsync<string>(@"
                SELECT pg_get_functiondef(p.oid)
                FROM pg_proc p
                INNER JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE p.proname = @routineName
                  AND n.nspname = @schemaName;", new { routineName, schemaName = schemaName ?? "public" });
            return source;
        }

        if (connection.ProviderType == DatabaseProviderType.MySql)
        {
            var isProcedure = routineType == "Procedure";
            var showSql = isProcedure ? $"SHOW CREATE PROCEDURE `{routineName}`" : $"SHOW CREATE FUNCTION `{routineName}`";
            var row = await db.QueryFirstOrDefaultAsync(showSql);
            if (row is null) return null;

            var dict = (IDictionary<string, object?>)row;
            // MySQL SHOW CREATE 返回的列名为 "Create Procedure" 或 "Create Function"
            var key = isProcedure ? "Create Procedure" : "Create Function";
            return dict.TryGetValue(key, out var val) ? val?.ToString() : null;
        }

        return null;
    }

    public async Task<IReadOnlyList<string>> GetCollationsAsync(ConnectionDefinition connection, string databaseName)
    {
        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        return connection.ProviderType switch
        {
            DatabaseProviderType.SqlServer => (await db.QueryAsync<string>(@"
                SELECT name
                FROM fn_helpcollations()
                ORDER BY name;")).ToArray(),
            DatabaseProviderType.MySql => (await db.QueryAsync<string>(@"
                SELECT collation_name
                FROM information_schema.collations
                ORDER BY collation_name;")).ToArray(),
            _ => Array.Empty<string>(),
        };
    }

    /// <summary>
    /// 获取数据库属性信息（常规、文件、权限）。
    /// </summary>
    public async Task<DbDatabasePropertiesInfo?> GetDatabasePropertiesAsync(ConnectionDefinition connection, string databaseName)
    {
        await using var db = DbConnectionFactory.Create(connection, databaseName);
        await db.OpenAsync();

        return connection.ProviderType switch
        {
            DatabaseProviderType.SqlServer => await GetSqlServerDatabasePropertiesAsync(db, databaseName),
            DatabaseProviderType.MySql => await GetMySqlDatabasePropertiesAsync(db, databaseName),
            DatabaseProviderType.PostgreSql => await GetPostgreSqlDatabasePropertiesAsync(db, databaseName),
            DatabaseProviderType.Sqlite => await GetSqliteDatabasePropertiesAsync(db, databaseName),
            _ => null,
        };
    }

    private static async Task<DbDatabasePropertiesInfo> GetSqlServerDatabasePropertiesAsync(DbConnection db, string databaseName)
    {
        // 常规属性
        var generalRow = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                d.name AS Name,
                d.state_desc AS State,
                SUSER_SNAME(d.owner_sid) AS Owner,
                d.create_date AS CreateDate,
                d.collation_name AS Collation,
                d.recovery_model_desc AS RecoveryModel,
                d.compatibility_level AS CompatibilityLevel,
                d.user_access_desc AS UserAccess,
                d.is_read_only AS IsReadOnly,
                d.is_auto_close_on AS IsAutoClose,
                d.is_auto_shrink_on AS IsAutoShrink,
                d.is_fulltext_enabled AS IsFullTextEnabled,
                d.page_verify_option_desc AS PageVerifyOption
            FROM sys.databases d
            WHERE d.name = @databaseName;",
            new { databaseName });

        // 数据库大小
        var sizeRows = await db.QueryAsync(@"
            SELECT
                SUM(CASE WHEN type = 0 THEN size END) * 8.0 / 1024 AS DataSizeMB,
                SUM(CASE WHEN type = 1 THEN size END) * 8.0 / 1024 AS LogSizeMB,
                SUM(size) * 8.0 / 1024 AS TotalSizeMB,
                SUM(CASE WHEN type = 0 THEN size - FILEPROPERTY(name, 'SpaceUsed') ELSE 0 END) * 8.0 / 1024 AS FreeSpaceMB
            FROM sys.database_files;");
        var sizeRow = sizeRows.FirstOrDefault();

        // 备份信息
        var backupRow = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset WHERE database_name = @databaseName AND type = 'D') AS LastFullBackup,
                (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset WHERE database_name = @databaseName AND type = 'L') AS LastLogBackup
            ;", new { databaseName });

        var generalProperties = new List<DbCatalogObjectProperty>();
        if (generalRow is not null)
        {
            generalProperties.Add(new DbCatalogObjectProperty { Label = "名称", Value = (string?)generalRow.Name });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "状态", Value = (string?)generalRow.State });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "所有者", Value = (string?)generalRow.Owner });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "创建日期", Value = ((DateTime?)generalRow.CreateDate)?.ToString("yyyy/MM/dd HH:mm:ss") });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "排序规则", Value = (string?)generalRow.Collation });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "恢复模式", Value = (string?)generalRow.RecoveryModel });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "兼容级别", Value = ((int?)generalRow.CompatibilityLevel)?.ToString() });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "用户访问", Value = (string?)generalRow.UserAccess });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "只读", Value = ((bool?)generalRow.IsReadOnly) == true ? "是" : "否" });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "自动关闭", Value = ((bool?)generalRow.IsAutoClose) == true ? "是" : "否" });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "自动收缩", Value = ((bool?)generalRow.IsAutoShrink) == true ? "是" : "否" });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "全文检索", Value = ((bool?)generalRow.IsFullTextEnabled) == true ? "已启用" : "未启用" });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "页面验证", Value = (string?)generalRow.PageVerifyOption });
        }

        if (sizeRow is not null)
        {
            generalProperties.Add(new DbCatalogObjectProperty { Label = "数据大小", Value = FormatSize((decimal?)sizeRow.DataSizeMB) });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "日志大小", Value = FormatSize((decimal?)sizeRow.LogSizeMB) });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "总大小", Value = FormatSize((decimal?)sizeRow.TotalSizeMB) });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "可用空间", Value = FormatSize((decimal?)sizeRow.FreeSpaceMB) });
        }

        if (backupRow is not null)
        {
            generalProperties.Add(new DbCatalogObjectProperty { Label = "上次完整备份", Value = ((DateTime?)backupRow.LastFullBackup)?.ToString("yyyy/MM/dd HH:mm:ss") ?? "无" });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "上次日志备份", Value = ((DateTime?)backupRow.LastLogBackup)?.ToString("yyyy/MM/dd HH:mm:ss") ?? "无" });
        }

        // 用户数
        var userCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.database_principals WHERE type IN ('S','U','G') AND principal_id > 4;");
        generalProperties.Add(new DbCatalogObjectProperty { Label = "用户数", Value = userCount.ToString() });

        // 文件信息
        var fileRows = await db.QueryAsync(@"
            SELECT
                f.name AS LogicalName,
                CASE f.type WHEN 0 THEN '行数据' WHEN 1 THEN '日志' WHEN 2 THEN '文件流' WHEN 4 THEN '全文' ELSE '其他' END AS FileType,
                fg.name AS FileGroup,
                CAST(f.size * 8.0 / 1024 AS DECIMAL(18,2)) AS SizeMB,
                CASE f.is_percent_growth
                    WHEN 1 THEN '增量为 ' + CAST(f.growth AS VARCHAR) + '%'
                    ELSE '增量为 ' + CAST(f.growth * 8 / 1024 AS VARCHAR) + ' MB'
                END
                + CASE
                    WHEN f.max_size = -1 THEN '，增长无限制'
                    WHEN f.max_size = 0 THEN '，不可增长'
                    ELSE '，限制为 ' + CAST(CAST(f.max_size * 8.0 / 1024 AS DECIMAL(18,0)) AS VARCHAR) + ' MB'
                  END AS AutoGrowth,
                f.physical_name AS Path
            FROM sys.database_files f
            LEFT JOIN sys.filegroups fg ON fg.data_space_id = f.data_space_id
            ORDER BY f.type, f.file_id;");

        var files = fileRows.Select(row => new DbDatabaseFileInfo
        {
            LogicalName = (string)row.LogicalName,
            FileType = (string)row.FileType,
            FileGroup = (string?)row.FileGroup,
            SizeMB = ((decimal)row.SizeMB).ToString("0.##") + " MB",
            AutoGrowth = (string?)row.AutoGrowth,
            Path = (string?)row.Path,
        }).ToArray();

        // 权限/用户
        var userRows = await db.QueryAsync(@"
            SELECT
                dp.name AS UserName,
                dp.type_desc AS UserType,
                dp.default_schema_name AS DefaultSchema,
                sp.name AS LoginName
            FROM sys.database_principals dp
            LEFT JOIN sys.server_principals sp ON sp.sid = dp.sid
            WHERE dp.type IN ('S','U','G','E','X')
              AND dp.principal_id > 4
            ORDER BY dp.name;");

        var roleRows = await db.QueryAsync(@"
            SELECT
                m.name AS UserName,
                r.name AS RoleName
            FROM sys.database_role_members rm
            INNER JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
            INNER JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
            WHERE m.principal_id > 4;");

        var roleLookup = roleRows
            .GroupBy(r => (string)r.UserName)
            .ToDictionary(g => g.Key, g => g.Select(r => (string)r.RoleName).ToArray(), StringComparer.OrdinalIgnoreCase);

        var permissions = userRows.Select(row => new DbDatabasePermissionInfo
        {
            UserName = (string)row.UserName,
            UserType = (string)row.UserType,
            DefaultSchema = (string?)row.DefaultSchema,
            LoginName = (string?)row.LoginName,
            Roles = roleLookup.TryGetValue((string)row.UserName, out var roles) ? roles : Array.Empty<string>(),
        }).ToArray();

        return new DbDatabasePropertiesInfo
        {
            GeneralProperties = generalProperties,
            Files = files,
            Permissions = permissions,
        };
    }

    private static async Task<DbDatabasePropertiesInfo> GetMySqlDatabasePropertiesAsync(DbConnection db, string databaseName)
    {
        var generalProperties = new List<DbCatalogObjectProperty>();

        var schemaRow = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                SCHEMA_NAME AS Name,
                DEFAULT_CHARACTER_SET_NAME AS CharacterSet,
                DEFAULT_COLLATION_NAME AS Collation
            FROM information_schema.SCHEMATA
            WHERE SCHEMA_NAME = @databaseName;",
            new { databaseName });

        if (schemaRow is not null)
        {
            generalProperties.Add(new DbCatalogObjectProperty { Label = "名称", Value = (string?)schemaRow.Name });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "字符集", Value = (string?)schemaRow.CharacterSet });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "排序规则", Value = (string?)schemaRow.Collation });
        }

        // 数据库大小
        var sizeRow = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                SUM(data_length + index_length) / 1024 / 1024 AS TotalSizeMB,
                SUM(data_length) / 1024 / 1024 AS DataSizeMB,
                SUM(index_length) / 1024 / 1024 AS IndexSizeMB,
                COUNT(*) AS TableCount
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @databaseName;",
            new { databaseName });

        if (sizeRow is not null)
        {
            generalProperties.Add(new DbCatalogObjectProperty { Label = "总大小", Value = FormatSize(ConvertToNullableDecimal(sizeRow.TotalSizeMB)) });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "数据大小", Value = FormatSize(ConvertToNullableDecimal(sizeRow.DataSizeMB)) });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "索引大小", Value = FormatSize(ConvertToNullableDecimal(sizeRow.IndexSizeMB)) });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "表数量", Value = ConvertToNullableInt32(sizeRow.TableCount)?.ToString() });
        }

        // 用户/权限
        var userRows = await db.QueryAsync(@"
            SELECT DISTINCT
                GRANTEE AS UserName,
                PRIVILEGE_TYPE AS Privilege
            FROM information_schema.SCHEMA_PRIVILEGES
            WHERE TABLE_SCHEMA = @databaseName
            ORDER BY GRANTEE;",
            new { databaseName });

        var userLookup = userRows
            .GroupBy(r => (string)r.UserName)
            .Select(g => new DbDatabasePermissionInfo
            {
                UserName = g.Key,
                UserType = "用户",
                Roles = g.Select(r => (string)r.Privilege).ToArray(),
            })
            .ToArray();

        return new DbDatabasePropertiesInfo
        {
            GeneralProperties = generalProperties,
            Files = Array.Empty<DbDatabaseFileInfo>(),
            Permissions = userLookup,
        };
    }

    private static async Task<DbDatabasePropertiesInfo> GetPostgreSqlDatabasePropertiesAsync(DbConnection db, string databaseName)
    {
        var generalProperties = new List<DbCatalogObjectProperty>();

        var dbRow = await db.QuerySingleOrDefaultAsync(@"
            SELECT
                d.datname AS ""Name"",
                pg_catalog.pg_get_userbyid(d.datdba) AS ""Owner"",
                pg_catalog.pg_encoding_to_char(d.encoding) AS ""Encoding"",
                d.datcollate AS ""Collation"",
                d.datctype AS ""CType"",
                d.datconnlimit AS ""ConnectionLimit"",
                pg_catalog.pg_database_size(d.datname) AS ""SizeBytes""
            FROM pg_catalog.pg_database d
            WHERE d.datname = @databaseName;",
            new { databaseName });

        if (dbRow is not null)
        {
            generalProperties.Add(new DbCatalogObjectProperty { Label = "名称", Value = (string?)dbRow.Name });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "所有者", Value = (string?)dbRow.Owner });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "编码", Value = (string?)dbRow.Encoding });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "排序规则", Value = (string?)dbRow.Collation });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "字符分类", Value = (string?)dbRow.CType });
            generalProperties.Add(new DbCatalogObjectProperty { Label = "连接限制", Value = ((int?)dbRow.ConnectionLimit)?.ToString() });
            var sizeBytes = (long?)dbRow.SizeBytes;
            if (sizeBytes.HasValue)
            {
                generalProperties.Add(new DbCatalogObjectProperty { Label = "大小", Value = $"{sizeBytes.Value / 1024.0 / 1024.0:0.##} MB" });
            }
        }

        // 表数量
        var tableCount = await db.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
              AND table_type = 'BASE TABLE';");
        generalProperties.Add(new DbCatalogObjectProperty { Label = "表数量", Value = tableCount.ToString() });

        // 用户/权限
        var userRows = await db.QueryAsync(@"
            SELECT
                r.rolname AS ""UserName"",
                CASE WHEN r.rolsuper THEN 'Superuser' WHEN r.rolcreaterole THEN 'CreateRole' ELSE 'User' END AS ""UserType"",
                ARRAY_TO_STRING(ARRAY(
                    SELECT b.rolname
                    FROM pg_catalog.pg_auth_members m
                    INNER JOIN pg_catalog.pg_roles b ON b.oid = m.roleid
                    WHERE m.member = r.oid
                ), ', ') AS ""Roles""
            FROM pg_catalog.pg_roles r
            WHERE r.rolcanlogin = true
              AND r.rolname NOT LIKE 'pg_%'
            ORDER BY r.rolname;");

        var permissions = userRows.Select(row => new DbDatabasePermissionInfo
        {
            UserName = (string)row.UserName,
            UserType = (string)row.UserType,
            Roles = string.IsNullOrEmpty((string?)row.Roles) ? Array.Empty<string>() : ((string)row.Roles).Split(", "),
        }).ToArray();

        return new DbDatabasePropertiesInfo
        {
            GeneralProperties = generalProperties,
            Files = Array.Empty<DbDatabaseFileInfo>(),
            Permissions = permissions,
        };
    }

    private static async Task<DbDatabasePropertiesInfo> GetSqliteDatabasePropertiesAsync(DbConnection db, string databaseName)
    {
        var generalProperties = new List<DbCatalogObjectProperty>();
        generalProperties.Add(new DbCatalogObjectProperty { Label = "名称", Value = databaseName });

        // PRAGMA 查询
        var pragmas = new[] { "page_size", "page_count", "journal_mode", "encoding", "auto_vacuum", "freelist_count" };
        foreach (var pragma in pragmas)
        {
            var value = await db.ExecuteScalarAsync<string>($"PRAGMA {pragma};");
            var label = pragma switch
            {
                "page_size" => "页面大小",
                "page_count" => "页面数",
                "journal_mode" => "日志模式",
                "encoding" => "编码",
                "auto_vacuum" => "自动清理",
                "freelist_count" => "空闲页数",
                _ => pragma,
            };
            generalProperties.Add(new DbCatalogObjectProperty { Label = label, Value = value });
        }

        // 计算大小
        var pageSize = await db.ExecuteScalarAsync<long>("PRAGMA page_size;");
        var pageCount = await db.ExecuteScalarAsync<long>("PRAGMA page_count;");
        var totalSizeBytes = pageSize * pageCount;
        generalProperties.Add(new DbCatalogObjectProperty { Label = "数据库大小", Value = $"{totalSizeBytes / 1024.0 / 1024.0:0.##} MB" });

        // 表数量
        var tableCount = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';");
        generalProperties.Add(new DbCatalogObjectProperty { Label = "表数量", Value = tableCount.ToString() });

        return new DbDatabasePropertiesInfo
        {
            GeneralProperties = generalProperties,
            Files = Array.Empty<DbDatabaseFileInfo>(),
            Permissions = Array.Empty<DbDatabasePermissionInfo>(),
        };
    }

    private static string FormatSize(decimal? sizeMB)
    {
        if (!sizeMB.HasValue)
        {
            return "无";
        }

        return $"{sizeMB.Value:0.##} MB";
    }
}