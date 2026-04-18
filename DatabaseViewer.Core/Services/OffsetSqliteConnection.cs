using System.Data;
using System.Data.Common;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SQLitePCL;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

internal static unsafe class NativeSqliteMethods
{
    [DllImport("e_sqlcipher", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_vfs_find")]
    public static extern nint sqlite3_vfs_find(byte* vfsName);

    [DllImport("e_sqlcipher", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_vfs_register")]
    public static extern int sqlite3_vfs_register(nint vfsPointer, int makeDflt);
}

internal static class OffsetSqliteConnectionFactory
{
    public static DbConnection Create(string filePath, SqliteCipherOptions options, SqliteOpenMode openMode)
    {
        SqliteOffsetVfs.Register();
        SqliteOffsetVfs.SetOffset(filePath, options.SkipBytes.GetValueOrDefault());

        var openFlags = openMode == SqliteOpenMode.ReadOnly
            ? raw.SQLITE_OPEN_READONLY
            : raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE;
        var openResult = raw.sqlite3_open_v2(filePath, out var databaseHandle, openFlags, SqliteOffsetVfs.VfsName);
        if (openResult != raw.SQLITE_OK)
        {
            throw new InvalidOperationException($"无法打开带偏移的 SQLite 数据库，SQLite 错误码: {openResult}", null);
        }

        try
        {
            ApplyCipherOptions(databaseHandle, options);
            return new OffsetSqliteConnection(databaseHandle, filePath);
        }
        catch
        {
            raw.sqlite3_close_v2(databaseHandle);
            throw;
        }
    }

    private static void ApplyCipherOptions(sqlite3 databaseHandle, SqliteCipherOptions options)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("启用 SQLCipher 时必须提供密码或十六进制密钥。", null);
        }

        Execute(databaseHandle, options.KeyFormat == SqliteCipherKeyFormat.Hex
            ? $"PRAGMA hexkey = '{DbConnectionFactory.NormalizeSqliteHexKey(options.Password)}';"
            : $"PRAGMA key = '{DbConnectionFactory.EscapeSqliteTextLiteral(options.Password)}';");

        if (options.PageSize.HasValue)
        {
            Execute(databaseHandle, $"PRAGMA cipher_page_size = {options.PageSize.Value};");
        }

        if (options.KdfIter.HasValue)
        {
            Execute(databaseHandle, $"PRAGMA kdf_iter = {options.KdfIter.Value};");
        }

        if (!string.IsNullOrWhiteSpace(options.HmacAlgorithm))
        {
            Execute(databaseHandle, $"PRAGMA cipher_hmac_algorithm = {NormalizeHmacAlgorithm(options.HmacAlgorithm)};");
        }

        if (!string.IsNullOrWhiteSpace(options.KdfAlgorithm))
        {
            Execute(databaseHandle, $"PRAGMA cipher_default_kdf_algorithm = {NormalizeKdfAlgorithm(options.KdfAlgorithm)};");
        }

        if (options.PlaintextHeaderSize.HasValue)
        {
            Execute(databaseHandle, $"PRAGMA cipher_plaintext_header_size = {options.PlaintextHeaderSize.Value};");
        }

        if (options.UseHmac.HasValue)
        {
            Execute(databaseHandle, $"PRAGMA cipher_use_hmac = {(options.UseHmac.Value ? "ON" : "OFF")};");
        }

        Execute(databaseHandle, "SELECT count(*) FROM sqlite_master;");
    }

    private static string NormalizeHmacAlgorithm(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "HMAC_SHA1" => "HMAC_SHA1",
            "HMAC_SHA256" => "HMAC_SHA256",
            "HMAC_SHA512" => "HMAC_SHA512",
            _ => throw new InvalidOperationException("SQLite 加密参数 cipher_hmac_algorithm 不受支持。", null),
        };
    }

    private static string NormalizeKdfAlgorithm(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "PBKDF2_HMAC_SHA1" => "PBKDF2_HMAC_SHA1",
            "PBKDF2_HMAC_SHA256" => "PBKDF2_HMAC_SHA256",
            "PBKDF2_HMAC_SHA512" => "PBKDF2_HMAC_SHA512",
            _ => throw new InvalidOperationException("SQLite 加密参数 cipher_default_kdf_algorithm 不受支持。", null),
        };
    }

    private static void Execute(sqlite3 databaseHandle, string sql)
    {
        var result = raw.sqlite3_exec(databaseHandle, sql, null, IntPtr.Zero, out var errorMessage);
        if (result == raw.SQLITE_OK)
        {
            return;
        }

        throw new InvalidOperationException($"SQLite 偏移连接初始化失败: {errorMessage ?? raw.sqlite3_errmsg(databaseHandle).utf8_to_string()}", null);
    }
}

    internal sealed class OffsetSqliteConnection : DbConnection
{
    private sqlite3 _databaseHandle;
    private readonly string _databaseFilePath;
    private ConnectionState _state = ConnectionState.Open;
    private bool _disposed;

    public OffsetSqliteConnection(sqlite3 databaseHandle, string databaseFilePath)
    {
        _databaseHandle = databaseHandle;
        _databaseFilePath = databaseFilePath;
    }

    internal sqlite3 Handle => _databaseHandle;

#pragma warning disable CS8765
    public override string ConnectionString
    {
        get => $"Data Source={_databaseFilePath}";
        set => throw new NotSupportedException("偏移 SQLite 连接不支持修改连接字符串。");
    }
#pragma warning restore CS8765

    public override string Database => _databaseFilePath;

    public override string DataSource => _databaseFilePath;

    public override string ServerVersion => raw.sqlite3_libversion().utf8_to_string();

    public override ConnectionState State => _disposed ? ConnectionState.Closed : _state;

    public override void Open()
    {
        ThrowIfDisposed();
        _state = ConnectionState.Open;
    }

    public override void Close()
    {
        if (_disposed)
        {
            return;
        }

        _state = ConnectionState.Closed;
    }

    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException("SQLite 不支持切换数据库。", null);
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        ThrowIfDisposed();
        if (_state != ConnectionState.Open)
        {
            throw new InvalidOperationException("连接未打开。", null);
        }

        return new OffsetSqliteTransaction(this, isolationLevel);
    }

    protected override DbCommand CreateDbCommand()
    {
        ThrowIfDisposed();
        if (_state != ConnectionState.Open)
        {
            throw new InvalidOperationException("连接未打开。", null);
        }

        return new OffsetSqliteCommand(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            raw.sqlite3_close_v2(_databaseHandle);
            _disposed = true;
            _state = ConnectionState.Closed;
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OffsetSqliteConnection));
        }
    }
}

internal sealed class OffsetSqliteCommand : DbCommand
{
    private OffsetSqliteConnection? _connection;
    private string? _commandText;
    private readonly DbParameterCollection _parameters = new OffsetSqliteParameterCollection();

    public OffsetSqliteCommand(OffsetSqliteConnection connection)
    {
        _connection = connection;
    }

#pragma warning disable CS8765
    public override string CommandText
    {
        get => _commandText ?? string.Empty;
        set => _commandText = value;
    }
#pragma warning restore CS8765

    public override int CommandTimeout { get; set; } = 30;

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = value as OffsetSqliteConnection;
    }

    protected override DbParameterCollection DbParameterCollection => _parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery()
    {
        EnsureCommandReady();

        var prepareResult = raw.sqlite3_prepare_v2(_connection!.Handle, _commandText, out var statementHandle);
        if (prepareResult != raw.SQLITE_OK)
        {
            throw new InvalidOperationException($"SQL 准备失败: {raw.sqlite3_errmsg(_connection.Handle).utf8_to_string()}", null);
        }

        try
        {
            OffsetSqliteBindHelper.BindAll(statementHandle, _parameters);
            while (true)
            {
                var stepResult = raw.sqlite3_step(statementHandle);
                if (stepResult == raw.SQLITE_ROW)
                {
                    continue;
                }

                if (stepResult == raw.SQLITE_DONE)
                {
                    break;
                }

                throw new InvalidOperationException($"SQL 执行失败: {raw.sqlite3_errmsg(_connection.Handle).utf8_to_string()} (code {stepResult})", null);
            }

            return raw.sqlite3_changes(_connection.Handle);
        }
        finally
        {
            raw.sqlite3_finalize(statementHandle);
        }
    }

    public override object? ExecuteScalar()
    {
        using var reader = ExecuteReader();
        if (reader.Read() && reader.FieldCount > 0)
        {
            return reader.GetValue(0);
        }

        return null;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        EnsureCommandReady();

        var prepareResult = raw.sqlite3_prepare_v2(_connection!.Handle, _commandText, out var statementHandle);
        if (prepareResult != raw.SQLITE_OK)
        {
            throw new InvalidOperationException($"SQL 准备失败: {raw.sqlite3_errmsg(_connection.Handle).utf8_to_string()}", null);
        }

        try
        {
            OffsetSqliteBindHelper.BindAll(statementHandle, _parameters);
            return new OffsetSqliteDataReader(_connection.Handle, statementHandle);
        }
        catch
        {
            raw.sqlite3_finalize(statementHandle);
            throw;
        }
    }

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter()
    {
        return new OffsetSqliteParameter();
    }

    private void EnsureCommandReady()
    {
        if (_connection is null || _connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("连接未打开。", null);
        }

        if (string.IsNullOrWhiteSpace(_commandText))
        {
            throw new InvalidOperationException("CommandText 不能为空。", null);
        }
    }
}

internal sealed class OffsetSqliteParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = new();

    public override int Count => _parameters.Count;

    public override object SyncRoot => _parameters;

    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (DbParameter parameter in values)
        {
            _parameters.Add(parameter);
        }
    }

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) => _parameters.Contains((DbParameter)value);

    public override bool Contains(string value) => _parameters.Any(parameter => parameter.ParameterName == value);

    public override void CopyTo(Array array, int index) => _parameters.ToArray().CopyTo(array, index);

    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();

    public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);

    public override int IndexOf(string parameterName) => _parameters.FindIndex(parameter => parameter.ParameterName == parameterName);

    public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);

    public override void Remove(object value) => _parameters.Remove((DbParameter)value);

    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            _parameters.RemoveAt(index);
        }
    }

    protected override DbParameter GetParameter(int index) => _parameters[index];

    protected override DbParameter GetParameter(string parameterName) => _parameters.First(parameter => parameter.ParameterName == parameterName);

    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            _parameters[index] = value;
        }
    }
}

internal sealed class OffsetSqliteParameter : DbParameter
{
#pragma warning disable CS8765
    public override DbType DbType { get; set; }

    public override ParameterDirection Direction { get; set; }

    public override bool IsNullable { get; set; }

    public override string ParameterName { get; set; } = string.Empty;

    public override int Size { get; set; }

    public override string SourceColumn { get; set; } = string.Empty;

    public override bool SourceColumnNullMapping { get; set; }

    public override object? Value { get; set; }

    public override void ResetDbType() => DbType = DbType.String;
#pragma warning restore CS8765
}

internal static class OffsetSqliteBindHelper
{
    public static void BindAll(sqlite3_stmt statementHandle, DbParameterCollection parameters)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        var positionalIndex = 1;
        foreach (DbParameter parameter in parameters)
        {
            var bindIndex = 0;
            var name = parameter.ParameterName ?? string.Empty;
            if (!string.IsNullOrEmpty(name))
            {
                bindIndex = ResolveParameterIndex(statementHandle, name);
            }

            if (bindIndex == 0)
            {
                bindIndex = positionalIndex++;
            }

            BindValue(statementHandle, bindIndex, parameter.Value);
        }
    }

    private static int ResolveParameterIndex(sqlite3_stmt statementHandle, string parameterName)
    {
        if (parameterName.Length > 0 && (parameterName[0] == '@' || parameterName[0] == ':' || parameterName[0] == '$' || parameterName[0] == '?'))
        {
            return raw.sqlite3_bind_parameter_index(statementHandle, parameterName);
        }

        foreach (var prefix in new[] { "@", ":", "$" })
        {
            var resolvedIndex = raw.sqlite3_bind_parameter_index(statementHandle, prefix + parameterName);
            if (resolvedIndex != 0)
            {
                return resolvedIndex;
            }
        }

        return raw.sqlite3_bind_parameter_index(statementHandle, parameterName);
    }

    private static void BindValue(sqlite3_stmt statementHandle, int bindIndex, object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            raw.sqlite3_bind_null(statementHandle, bindIndex);
            return;
        }

        switch (value)
        {
            case long longValue:
                raw.sqlite3_bind_int64(statementHandle, bindIndex, longValue);
                break;
            case int intValue:
                raw.sqlite3_bind_int64(statementHandle, bindIndex, intValue);
                break;
            case short shortValue:
                raw.sqlite3_bind_int64(statementHandle, bindIndex, shortValue);
                break;
            case byte byteValue:
                raw.sqlite3_bind_int64(statementHandle, bindIndex, byteValue);
                break;
            case bool boolValue:
                raw.sqlite3_bind_int64(statementHandle, bindIndex, boolValue ? 1 : 0);
                break;
            case double doubleValue:
                raw.sqlite3_bind_double(statementHandle, bindIndex, doubleValue);
                break;
            case float floatValue:
                raw.sqlite3_bind_double(statementHandle, bindIndex, floatValue);
                break;
            case decimal decimalValue:
                raw.sqlite3_bind_double(statementHandle, bindIndex, (double)decimalValue);
                break;
            case string stringValue:
                raw.sqlite3_bind_text(statementHandle, bindIndex, stringValue);
                break;
            case byte[] blobValue:
                raw.sqlite3_bind_blob(statementHandle, bindIndex, blobValue);
                break;
            case ReadOnlyMemory<byte> memoryValue:
                raw.sqlite3_bind_blob(statementHandle, bindIndex, memoryValue.ToArray());
                break;
            case Guid guidValue:
                raw.sqlite3_bind_text(statementHandle, bindIndex, guidValue.ToString());
                break;
            case DateTimeOffset dateTimeOffsetValue:
                raw.sqlite3_bind_int64(statementHandle, bindIndex, dateTimeOffsetValue.ToUnixTimeSeconds());
                break;
            case DateTime dateTimeValue:
                raw.sqlite3_bind_text(statementHandle, bindIndex, dateTimeValue.ToString("O"));
                break;
            default:
                raw.sqlite3_bind_text(statementHandle, bindIndex, value.ToString() ?? string.Empty);
                break;
        }
    }
}

internal sealed class OffsetSqliteDataReader : DbDataReader
{
    private readonly sqlite3 _databaseHandle;
    private sqlite3_stmt _statementHandle;
    private bool _isClosed;
    private DataTable? _schemaTable;

    public OffsetSqliteDataReader(sqlite3 databaseHandle, sqlite3_stmt statementHandle)
    {
        _databaseHandle = databaseHandle;
        _statementHandle = statementHandle;
    }

    public override int FieldCount => raw.sqlite3_column_count(_statementHandle);

    public override bool HasRows => true;

    public override bool IsClosed => _isClosed;

    public override int RecordsAffected => raw.sqlite3_changes(_databaseHandle);

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_isClosed)
        {
            return false;
        }

        var stepResult = raw.sqlite3_step(_statementHandle);
        if (stepResult == raw.SQLITE_ROW)
        {
            return true;
        }

        if (stepResult == raw.SQLITE_DONE)
        {
            return false;
        }

        throw new InvalidOperationException($"读取 SQLite 数据失败: {raw.sqlite3_errmsg(_databaseHandle).utf8_to_string()} (code {stepResult})", null);
    }

    public override bool NextResult() => false;

    public override string GetName(int ordinal) => raw.sqlite3_column_name(_statementHandle, ordinal).utf8_to_string();

    public override int GetOrdinal(string name)
    {
        for (var index = 0; index < FieldCount; index++)
        {
            if (string.Equals(GetName(index), name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        throw new IndexOutOfRangeException($"列 '{name}' 不存在。");
    }

    public override string GetDataTypeName(int ordinal) => raw.sqlite3_column_decltype(_statementHandle, ordinal).utf8_to_string() ?? "TEXT";

    public override Type GetFieldType(int ordinal)
    {
        var declaredType = GetDataTypeName(ordinal);
        if (!string.IsNullOrWhiteSpace(declaredType))
        {
            return InferFieldType(declaredType);
        }

        return raw.sqlite3_column_type(_statementHandle, ordinal) switch
        {
            raw.SQLITE_INTEGER => typeof(long),
            raw.SQLITE_FLOAT => typeof(double),
            raw.SQLITE_TEXT => typeof(string),
            raw.SQLITE_BLOB => typeof(byte[]),
            _ => typeof(object),
        };
    }

    public override object GetValue(int ordinal)
    {
        return raw.sqlite3_column_type(_statementHandle, ordinal) switch
        {
            raw.SQLITE_INTEGER => raw.sqlite3_column_int64(_statementHandle, ordinal),
            raw.SQLITE_FLOAT => raw.sqlite3_column_double(_statementHandle, ordinal),
            raw.SQLITE_TEXT => raw.sqlite3_column_text(_statementHandle, ordinal).utf8_to_string(),
            raw.SQLITE_BLOB => raw.sqlite3_column_blob(_statementHandle, ordinal).ToArray(),
            raw.SQLITE_NULL => DBNull.Value,
            _ => DBNull.Value,
        };
    }

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var index = 0; index < count; index++)
        {
            values[index] = GetValue(index);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal) => raw.sqlite3_column_type(_statementHandle, ordinal) == raw.SQLITE_NULL;

    public override bool GetBoolean(int ordinal) => GetInt64(ordinal) != 0;

    public override byte GetByte(int ordinal) => (byte)GetInt64(ordinal);

    public override char GetChar(int ordinal) => (char)GetInt64(ordinal);

    public override DateTime GetDateTime(int ordinal) => DateTime.Parse(GetString(ordinal));

    public override decimal GetDecimal(int ordinal) => (decimal)GetDouble(ordinal);

    public override double GetDouble(int ordinal) => raw.sqlite3_column_double(_statementHandle, ordinal);

    public override float GetFloat(int ordinal) => (float)GetDouble(ordinal);

    public override Guid GetGuid(int ordinal) => Guid.Parse(GetString(ordinal));

    public override short GetInt16(int ordinal) => (short)GetInt64(ordinal);

    public override int GetInt32(int ordinal) => (int)GetInt64(ordinal);

    public override long GetInt64(int ordinal) => raw.sqlite3_column_int64(_statementHandle, ordinal);

    public override string GetString(int ordinal) => raw.sqlite3_column_text(_statementHandle, ordinal).utf8_to_string();

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var bytes = raw.sqlite3_column_blob(_statementHandle, ordinal).ToArray();
        if (buffer is not null)
        {
            Array.Copy(bytes, dataOffset, buffer, bufferOffset, length);
        }

        return bytes.Length;
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var value = GetString(ordinal);
        if (buffer is not null)
        {
            value.CopyTo((int)dataOffset, buffer, bufferOffset, length);
        }

        return value.Length;
    }

    public override DataTable GetSchemaTable()
    {
        if (_schemaTable is not null)
        {
            return _schemaTable;
        }

        var schemaTable = new DataTable("SchemaTable")
        {
            Locale = System.Globalization.CultureInfo.InvariantCulture,
        };

        schemaTable.Columns.Add(SchemaTableColumn.ColumnName, typeof(string));
        schemaTable.Columns.Add(SchemaTableColumn.ColumnOrdinal, typeof(int));
        schemaTable.Columns.Add(SchemaTableColumn.ColumnSize, typeof(int));
        schemaTable.Columns.Add(SchemaTableColumn.NumericPrecision, typeof(short));
        schemaTable.Columns.Add(SchemaTableColumn.NumericScale, typeof(short));
        schemaTable.Columns.Add(SchemaTableColumn.DataType, typeof(Type));
        schemaTable.Columns.Add(SchemaTableColumn.ProviderType, typeof(int));
        schemaTable.Columns.Add(SchemaTableColumn.IsLong, typeof(bool));
        schemaTable.Columns.Add(SchemaTableColumn.AllowDBNull, typeof(bool));
        schemaTable.Columns.Add("IsReadOnly", typeof(bool));
        schemaTable.Columns.Add("IsRowVersion", typeof(bool));
        schemaTable.Columns.Add(SchemaTableColumn.IsUnique, typeof(bool));
        schemaTable.Columns.Add(SchemaTableColumn.IsKey, typeof(bool));
        schemaTable.Columns.Add("IsAutoIncrement", typeof(bool));
        schemaTable.Columns.Add(SchemaTableColumn.BaseSchemaName, typeof(string));
        schemaTable.Columns.Add("BaseCatalogName", typeof(string));
        schemaTable.Columns.Add(SchemaTableColumn.BaseTableName, typeof(string));
        schemaTable.Columns.Add(SchemaTableColumn.BaseColumnName, typeof(string));
        schemaTable.Columns.Add("DataTypeName", typeof(string));

        for (var index = 0; index < FieldCount; index++)
        {
            var declaredType = GetDataTypeName(index);
            var fieldType = GetFieldType(index);
            var row = schemaTable.NewRow();
            row[SchemaTableColumn.ColumnName] = GetName(index);
            row[SchemaTableColumn.ColumnOrdinal] = index;
            row[SchemaTableColumn.ColumnSize] = -1;
            row[SchemaTableColumn.NumericPrecision] = (short)0;
            row[SchemaTableColumn.NumericScale] = (short)0;
            row[SchemaTableColumn.DataType] = fieldType;
            row[SchemaTableColumn.ProviderType] = GetProviderType(fieldType);
            row[SchemaTableColumn.IsLong] = fieldType == typeof(string) || fieldType == typeof(byte[]);
            row[SchemaTableColumn.AllowDBNull] = true;
            row["IsReadOnly"] = false;
            row["IsRowVersion"] = false;
            row[SchemaTableColumn.IsUnique] = false;
            row[SchemaTableColumn.IsKey] = false;
            row["IsAutoIncrement"] = false;
            row[SchemaTableColumn.BaseSchemaName] = string.Empty;
            row["BaseCatalogName"] = string.Empty;
            row[SchemaTableColumn.BaseTableName] = string.Empty;
            row[SchemaTableColumn.BaseColumnName] = GetName(index);
            row["DataTypeName"] = declaredType;
            schemaTable.Rows.Add(row);
        }

        _schemaTable = schemaTable;
        return schemaTable;
    }

    public override System.Collections.IEnumerator GetEnumerator() => new DbEnumerator(this, false);

    public override void Close()
    {
        if (_isClosed)
        {
            return;
        }

        raw.sqlite3_finalize(_statementHandle);
        _isClosed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }

    private static Type InferFieldType(string declaredType)
    {
        var normalizedType = declaredType.Trim().ToUpperInvariant();
        if (normalizedType.Contains("INT", StringComparison.Ordinal))
        {
            return typeof(long);
        }

        if (normalizedType.Contains("CHAR", StringComparison.Ordinal)
            || normalizedType.Contains("CLOB", StringComparison.Ordinal)
            || normalizedType.Contains("TEXT", StringComparison.Ordinal)
            || normalizedType.Contains("JSON", StringComparison.Ordinal))
        {
            return typeof(string);
        }

        if (normalizedType.Contains("BLOB", StringComparison.Ordinal))
        {
            return typeof(byte[]);
        }

        if (normalizedType.Contains("REAL", StringComparison.Ordinal)
            || normalizedType.Contains("FLOA", StringComparison.Ordinal)
            || normalizedType.Contains("DOUB", StringComparison.Ordinal)
            || normalizedType.Contains("DEC", StringComparison.Ordinal)
            || normalizedType.Contains("NUM", StringComparison.Ordinal))
        {
            return typeof(double);
        }

        if (normalizedType.Contains("DATE", StringComparison.Ordinal)
            || normalizedType.Contains("TIME", StringComparison.Ordinal))
        {
            return typeof(string);
        }

        return typeof(string);
    }

    private static int GetProviderType(Type fieldType)
    {
        if (fieldType == typeof(long))
        {
            return raw.SQLITE_INTEGER;
        }

        if (fieldType == typeof(double))
        {
            return raw.SQLITE_FLOAT;
        }

        if (fieldType == typeof(byte[]))
        {
            return raw.SQLITE_BLOB;
        }

        return raw.SQLITE_TEXT;
    }
}

internal sealed class OffsetSqliteTransaction : DbTransaction
{
    private OffsetSqliteConnection? _connection;
    private readonly IsolationLevel _isolationLevel;
    private bool _completed;

    public OffsetSqliteTransaction(OffsetSqliteConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection;
        _isolationLevel = isolationLevel;

        raw.sqlite3_exec(connection.Handle, isolationLevel == IsolationLevel.Serializable
            ? "BEGIN EXCLUSIVE TRANSACTION"
            : "BEGIN TRANSACTION", null, IntPtr.Zero, out _);
    }

    public override IsolationLevel IsolationLevel => _isolationLevel;

    protected override DbConnection? DbConnection => _connection;

    public override void Commit()
    {
        if (_completed || _connection is null)
        {
            throw new InvalidOperationException("事务已完成或连接已关闭。", null);
        }

        raw.sqlite3_exec(_connection.Handle, "COMMIT TRANSACTION", null, IntPtr.Zero, out _);
        _completed = true;
    }

    public override void Rollback()
    {
        if (_completed || _connection is null)
        {
            throw new InvalidOperationException("事务已完成或连接已关闭。", null);
        }

        raw.sqlite3_exec(_connection.Handle, "ROLLBACK TRANSACTION", null, IntPtr.Zero, out _);
        _completed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_completed && _connection is not null)
        {
            Rollback();
        }

        _connection = null;
        base.Dispose(disposing);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct sqlite3_file(sqlite3_io_methods* methodsPointer)
{
    public sqlite3_io_methods* pMethods = methodsPointer;

    public static implicit operator sqlite3_file(sqlite3_io_methods* methodsPointer) => new(methodsPointer);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct StreamSqlite3File
{
    public sqlite3_file Base;
    public long Offset;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct sqlite3_io_methods
{
    public int iVersion;
    public delegate* unmanaged<sqlite3_file*, int> xClose;
    public delegate* unmanaged<sqlite3_file*, void*, int, long, int> xRead;
    public delegate* unmanaged<sqlite3_file*, void*, int, long, int> xWrite;
    public delegate* unmanaged<sqlite3_file*, long, int> xTruncate;
    public delegate* unmanaged<sqlite3_file*, int, int> xSync;
    public delegate* unmanaged<sqlite3_file*, long*, int> xFileSize;
    public delegate* unmanaged<sqlite3_file*, int, int> xLock;
    public delegate* unmanaged<sqlite3_file*, int, int> xUnlock;
    public delegate* unmanaged<sqlite3_file*, int*, int> xCheckReservedLock;
    public delegate* unmanaged<sqlite3_file*, int, void*, int> xFileControl;
    public delegate* unmanaged<sqlite3_file*, int> xSectorSize;
    public delegate* unmanaged<sqlite3_file*, int> xDeviceCharacteristics;
    public delegate* unmanaged<sqlite3_file*, int, int, int, void**, int> xShmMap;
    public delegate* unmanaged<sqlite3_file*, int, int, int, int> xShmLock;
    public delegate* unmanaged<sqlite3_file*, void> xShmBarrier;
    public delegate* unmanaged<sqlite3_file*, int, int> xShmUnmap;
    public delegate* unmanaged<sqlite3_file*, long, int, void**, int> xFetch;
    public delegate* unmanaged<sqlite3_file*, long, void*, int> xUnfetch;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct sqlite3_vfs
{
    public int iVersion;
    public int szOsFile;
    public int mxPathname;
    public sqlite3_vfs* pNext;
    public SqliteUtf8String zName;
    public void* pAppData;
    public delegate* unmanaged<sqlite3_vfs*, SqliteUtf8String, sqlite3_file*, int, int*, int> xOpen;
    public delegate* unmanaged<sqlite3_vfs*, SqliteUtf8String, int, int> xDelete;
    public delegate* unmanaged<sqlite3_vfs*, SqliteUtf8String, int, int*, int> xAccess;
    public delegate* unmanaged<sqlite3_vfs*, SqliteUtf8String, int, byte*, int> xFullPathname;
    public delegate* unmanaged<sqlite3_vfs*, SqliteUtf8String, void*> xDlOpen;
    public delegate* unmanaged<sqlite3_vfs*, int, SqliteUtf8String, void> xDlError;
    public delegate* unmanaged<sqlite3_vfs*, void*, SqliteUtf8String, delegate* unmanaged<void>> xDlSym;
    public delegate* unmanaged<sqlite3_vfs*, void*, void> xDlClose;
    public delegate* unmanaged<sqlite3_vfs*, int, char*, int> xRandomness;
    public delegate* unmanaged<sqlite3_vfs*, int, int> xSleep;
    public delegate* unmanaged<sqlite3_vfs*, double*, int> xCurrentTime;
    public delegate* unmanaged<sqlite3_vfs*, int, char*, int> xGetLastError;
    public delegate* unmanaged<sqlite3_vfs*, long*, int> xCurrentTimeInt64;
    public delegate* unmanaged<sqlite3_vfs*, SqliteUtf8String, delegate* unmanaged<void>, int> xSetSystemCall;
    public delegate* unmanaged<sqlite3_vfs*, SqliteUtf8String, delegate* unmanaged<void>> xGetSystemCall;
    public delegate* unmanaged<sqlite3_vfs*, SqliteUtf8String, SqliteUtf8String> xNextSystemCall;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe readonly struct SqliteUtf8String(byte* pointer)
{
    public readonly byte* Pointer = pointer;

    public static implicit operator SqliteUtf8String(byte* pointer) => new(pointer);

    public string ToManagedString() => Pointer == null ? string.Empty : Marshal.PtrToStringUTF8((IntPtr)Pointer) ?? string.Empty;
}

internal static unsafe class SqliteOffsetVfs
{
    public const string VfsName = "dbv-offset-vfs";

    private static bool _registered;
    private static readonly ConcurrentDictionary<string, long> FileOffsets = new(StringComparer.OrdinalIgnoreCase);
    private static ReadOnlySpan<byte> VfsNameUtf8 => "dbv-offset-vfs"u8;

    private static readonly sqlite3_io_methods StreamIoMethods = new()
    {
        iVersion = 3,
        xClose = &xClose,
        xRead = &xRead,
        xWrite = &xWrite,
        xTruncate = &xTruncate,
        xSync = &xSync,
        xFileSize = &xFileSize,
        xLock = &xLock,
        xUnlock = &xUnlock,
        xCheckReservedLock = &xCheckReservedLock,
        xFileControl = &xFileControl,
        xSectorSize = &xSectorSize,
        xDeviceCharacteristics = &xDeviceCharacteristics,
        xShmMap = &xShmMap,
        xShmLock = &xShmLock,
        xShmBarrier = &xShmBarrier,
        xShmUnmap = &xShmUnmap,
        xFetch = &xFetch,
        xUnfetch = &xUnfetch,
    };

    private static sqlite3_vfs StreamVfs = new()
    {
        iVersion = 1,
        szOsFile = sizeof(sqlite3_file) + sizeof(long),
        mxPathname = 260,
        zName = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in VfsNameUtf8.GetPinnableReference())),
        pAppData = null,
        xOpen = &xOpen,
        xDelete = &xDelete,
        xAccess = &xAccess,
        xFullPathname = &xFullPathname,
        xDlOpen = &xDlOpen,
        xDlError = &xDlError,
        xDlSym = &xDlSym,
        xDlClose = &xDlClose,
        xRandomness = &xRandomness,
        xSleep = &xSleep,
        xCurrentTime = &xCurrentTime,
        xGetLastError = &xGetLastError,
    };

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        var defaultVfsPointer = NativeSqliteMethods.sqlite3_vfs_find(null);
        if (defaultVfsPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法找到默认 SQLite VFS。", null);
        }

        var defaultVfs = (sqlite3_vfs*)defaultVfsPointer;
        StreamVfs.pAppData = (void*)defaultVfsPointer;
        StreamVfs.szOsFile += defaultVfs->szOsFile;

        var registerResult = NativeSqliteMethods.sqlite3_vfs_register((nint)Unsafe.AsPointer(ref StreamVfs), 0);
        if (registerResult != raw.SQLITE_OK)
        {
            throw new InvalidOperationException("注册 SQLite 偏移 VFS 失败。", null);
        }

        _registered = true;
    }

    public static void SetOffset(string filePath, long skipBytes)
    {
        FileOffsets[NormalizePath(filePath)] = Math.Max(0, skipBytes);
    }

    private static sqlite3_file* GetOriginalFile(sqlite3_file* filePointer)
    {
        var basePointer = (byte*)filePointer;
        return (sqlite3_file*)(basePointer + sizeof(sqlite3_file) + sizeof(long));
    }

    private static long GetOffset(sqlite3_file* filePointer) => ((StreamSqlite3File*)filePointer)->Offset;

    private static void SetOffset(sqlite3_file* filePointer, long offset) => ((StreamSqlite3File*)filePointer)->Offset = offset;

    private static string NormalizePath(string filePath)
    {
        try
        {
            return Path.GetFullPath(filePath);
        }
        catch
        {
            return filePath;
        }
    }

    [UnmanagedCallersOnly]
    private static int xOpen(sqlite3_vfs* vfsPointer, SqliteUtf8String fileName, sqlite3_file* filePointer, int flags, int* outFlags)
    {
        try
        {
            var originalVfs = (sqlite3_vfs*)vfsPointer->pAppData;
            var isMainDatabase = (flags & raw.SQLITE_OPEN_MAIN_DB) != 0;
            var isTempDatabase = (flags & (0x00000008 | 0x00000010)) != 0;
            if (!isMainDatabase || isTempDatabase)
            {
                return originalVfs->xOpen(originalVfs, fileName, filePointer, flags, outFlags);
            }

            var originalFile = GetOriginalFile(filePointer);
            new Span<byte>(originalFile, originalVfs->szOsFile).Clear();
            var openResult = originalVfs->xOpen(originalVfs, fileName, originalFile, flags, outFlags);
            if (openResult != raw.SQLITE_OK)
            {
                return openResult;
            }

            var fileOffset = 0L;
            var managedFileName = NormalizePath(fileName.ToManagedString());
            if (!string.IsNullOrWhiteSpace(managedFileName))
            {
                FileOffsets.TryGetValue(managedFileName, out fileOffset);
            }

            filePointer->pMethods = (sqlite3_io_methods*)Unsafe.AsPointer(ref Unsafe.AsRef(in StreamIoMethods));
            SetOffset(filePointer, fileOffset);
            return raw.SQLITE_OK;
        }
        catch
        {
            return raw.SQLITE_IOERR;
        }
    }

    [UnmanagedCallersOnly]
    private static int xClose(sqlite3_file* filePointer) => GetOriginalFile(filePointer)->pMethods->xClose(GetOriginalFile(filePointer));

    [UnmanagedCallersOnly]
    private static int xRead(sqlite3_file* filePointer, void* buffer, int amount, long offset) => GetOriginalFile(filePointer)->pMethods->xRead(GetOriginalFile(filePointer), buffer, amount, offset + GetOffset(filePointer));

    [UnmanagedCallersOnly]
    private static int xWrite(sqlite3_file* filePointer, void* buffer, int amount, long offset) => GetOriginalFile(filePointer)->pMethods->xWrite(GetOriginalFile(filePointer), buffer, amount, offset + GetOffset(filePointer));

    [UnmanagedCallersOnly]
    private static int xTruncate(sqlite3_file* filePointer, long size) => GetOriginalFile(filePointer)->pMethods->xTruncate(GetOriginalFile(filePointer), size + GetOffset(filePointer));

    [UnmanagedCallersOnly]
    private static int xSync(sqlite3_file* filePointer, int flags) => GetOriginalFile(filePointer)->pMethods->xSync(GetOriginalFile(filePointer), flags);

    [UnmanagedCallersOnly]
    private static int xFileSize(sqlite3_file* filePointer, long* sizePointer)
    {
        var result = GetOriginalFile(filePointer)->pMethods->xFileSize(GetOriginalFile(filePointer), sizePointer);
        if (result == raw.SQLITE_OK)
        {
            *sizePointer -= GetOffset(filePointer);
        }

        return result;
    }

    [UnmanagedCallersOnly]
    private static int xLock(sqlite3_file* filePointer, int lockLevel) => GetOriginalFile(filePointer)->pMethods->xLock(GetOriginalFile(filePointer), lockLevel);

    [UnmanagedCallersOnly]
    private static int xUnlock(sqlite3_file* filePointer, int lockLevel) => GetOriginalFile(filePointer)->pMethods->xUnlock(GetOriginalFile(filePointer), lockLevel);

    [UnmanagedCallersOnly]
    private static int xCheckReservedLock(sqlite3_file* filePointer, int* resultPointer) => GetOriginalFile(filePointer)->pMethods->xCheckReservedLock(GetOriginalFile(filePointer), resultPointer);

    [UnmanagedCallersOnly]
    private static int xFileControl(sqlite3_file* filePointer, int operation, void* argument)
    {
        if (operation == 5 && argument != null)
        {
            *(long*)argument += GetOffset(filePointer);
        }

        return GetOriginalFile(filePointer)->pMethods->xFileControl(GetOriginalFile(filePointer), operation, argument);
    }

    [UnmanagedCallersOnly]
    private static int xSectorSize(sqlite3_file* filePointer) => GetOriginalFile(filePointer)->pMethods->xSectorSize(GetOriginalFile(filePointer));

    [UnmanagedCallersOnly]
    private static int xDeviceCharacteristics(sqlite3_file* filePointer) => GetOriginalFile(filePointer)->pMethods->xDeviceCharacteristics(GetOriginalFile(filePointer));

    [UnmanagedCallersOnly]
    private static int xShmMap(sqlite3_file* filePointer, int pageIndex, int pageSize, int extend, void** mappedPointer) => GetOriginalFile(filePointer)->pMethods->xShmMap(GetOriginalFile(filePointer), pageIndex, pageSize, extend, mappedPointer);

    [UnmanagedCallersOnly]
    private static int xShmLock(sqlite3_file* filePointer, int offset, int count, int flags) => GetOriginalFile(filePointer)->pMethods->xShmLock(GetOriginalFile(filePointer), offset, count, flags);

    [UnmanagedCallersOnly]
    private static void xShmBarrier(sqlite3_file* filePointer) => GetOriginalFile(filePointer)->pMethods->xShmBarrier(GetOriginalFile(filePointer));

    [UnmanagedCallersOnly]
    private static int xShmUnmap(sqlite3_file* filePointer, int deleteFlag) => GetOriginalFile(filePointer)->pMethods->xShmUnmap(GetOriginalFile(filePointer), deleteFlag);

    [UnmanagedCallersOnly]
    private static int xFetch(sqlite3_file* filePointer, long offset, int amount, void** mappedPointer) => GetOriginalFile(filePointer)->pMethods->xFetch(GetOriginalFile(filePointer), offset + GetOffset(filePointer), amount, mappedPointer);

    [UnmanagedCallersOnly]
    private static int xUnfetch(sqlite3_file* filePointer, long offset, void* mappedPointer) => GetOriginalFile(filePointer)->pMethods->xUnfetch(GetOriginalFile(filePointer), offset + GetOffset(filePointer), mappedPointer);

    [UnmanagedCallersOnly]
    private static int xDelete(sqlite3_vfs* vfsPointer, SqliteUtf8String fileName, int syncDir) => ((sqlite3_vfs*)vfsPointer->pAppData)->xDelete((sqlite3_vfs*)vfsPointer->pAppData, fileName, syncDir);

    [UnmanagedCallersOnly]
    private static int xAccess(sqlite3_vfs* vfsPointer, SqliteUtf8String fileName, int flags, int* resultPointer) => ((sqlite3_vfs*)vfsPointer->pAppData)->xAccess((sqlite3_vfs*)vfsPointer->pAppData, fileName, flags, resultPointer);

    [UnmanagedCallersOnly]
    private static int xFullPathname(sqlite3_vfs* vfsPointer, SqliteUtf8String fileName, int outputSize, byte* outputPointer) => ((sqlite3_vfs*)vfsPointer->pAppData)->xFullPathname((sqlite3_vfs*)vfsPointer->pAppData, fileName, outputSize, outputPointer);

    [UnmanagedCallersOnly]
    private static void* xDlOpen(sqlite3_vfs* vfsPointer, SqliteUtf8String fileName) => ((sqlite3_vfs*)vfsPointer->pAppData)->xDlOpen((sqlite3_vfs*)vfsPointer->pAppData, fileName);

    [UnmanagedCallersOnly]
    private static void xDlError(sqlite3_vfs* vfsPointer, int bufferSize, SqliteUtf8String errorPointer) => ((sqlite3_vfs*)vfsPointer->pAppData)->xDlError((sqlite3_vfs*)vfsPointer->pAppData, bufferSize, errorPointer);

    [UnmanagedCallersOnly]
    private static delegate* unmanaged<void> xDlSym(sqlite3_vfs* vfsPointer, void* handlePointer, SqliteUtf8String symbolName) => ((sqlite3_vfs*)vfsPointer->pAppData)->xDlSym((sqlite3_vfs*)vfsPointer->pAppData, handlePointer, symbolName);

    [UnmanagedCallersOnly]
    private static void xDlClose(sqlite3_vfs* vfsPointer, void* handlePointer) => ((sqlite3_vfs*)vfsPointer->pAppData)->xDlClose((sqlite3_vfs*)vfsPointer->pAppData, handlePointer);

    [UnmanagedCallersOnly]
    private static int xRandomness(sqlite3_vfs* vfsPointer, int length, char* bufferPointer) => ((sqlite3_vfs*)vfsPointer->pAppData)->xRandomness((sqlite3_vfs*)vfsPointer->pAppData, length, bufferPointer);

    [UnmanagedCallersOnly]
    private static int xSleep(sqlite3_vfs* vfsPointer, int microseconds) => ((sqlite3_vfs*)vfsPointer->pAppData)->xSleep((sqlite3_vfs*)vfsPointer->pAppData, microseconds);

    [UnmanagedCallersOnly]
    private static int xCurrentTime(sqlite3_vfs* vfsPointer, double* timePointer) => ((sqlite3_vfs*)vfsPointer->pAppData)->xCurrentTime((sqlite3_vfs*)vfsPointer->pAppData, timePointer);

    [UnmanagedCallersOnly]
    private static int xGetLastError(sqlite3_vfs* vfsPointer, int bufferSize, char* errorPointer) => ((sqlite3_vfs*)vfsPointer->pAppData)->xGetLastError((sqlite3_vfs*)vfsPointer->pAppData, bufferSize, errorPointer);
}