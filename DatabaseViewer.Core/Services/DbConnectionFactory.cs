using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using DatabaseViewer.Core.Models;
using SQLitePCL;

namespace DatabaseViewer.Core.Services;

public static class DbConnectionFactory
{
    private static readonly ApplicationSettingsStore SettingsStore = new();
    private static readonly Regex SqliteHexKeyRegex = new("^[0-9A-Fa-f]+$", RegexOptions.Compiled);

    private static readonly Regex SqlitePragmaNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    static DbConnectionFactory()
    {
        Batteries_V2.Init();
    }

    public static DbConnection Create(ConnectionDefinition definition, string? databaseName = null)
    {
        return definition.ProviderType switch
        {
            DatabaseProviderType.SqlServer => CreateSqlServer(definition, databaseName),
            DatabaseProviderType.MySql => CreateMySql(definition, databaseName),
            DatabaseProviderType.PostgreSql => CreatePostgreSql(definition, databaseName),
            DatabaseProviderType.Sqlite => CreateSqlite(definition),
            _ => throw new NotSupportedException($"Unsupported provider: {definition.ProviderType}"),
        };
    }

    private static DbConnection CreateSqlServer(ConnectionDefinition definition, string? databaseName)
    {
        var endpoint = SshTunnelManager.ResolveEndpoint(definition);
        var dataSource = BuildSqlServerDataSource(definition);
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = definition.SshTunnel.Enabled ? $"{endpoint.Host},{endpoint.Port}" : dataSource,
            InitialCatalog = string.IsNullOrWhiteSpace(databaseName) ? "master" : databaseName,
            TrustServerCertificate = definition.SqlServer.TrustServerCertificate,
            Encrypt = true,
            MultipleActiveResultSets = false,
        };

        if (definition.SqlServer.AuthenticationMode == SqlServerAuthenticationMode.WindowsIntegrated)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = definition.Username;
            builder.Password = definition.Password;
        }

        return new SqlConnection(builder.ConnectionString);
    }

    private static string BuildSqlServerDataSource(ConnectionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Host))
        {
            return string.Empty;
        }

        if (definition.Port <= 0 || LooksLikeNamedInstance(definition.Host))
        {
            return definition.Host;
        }

        return $"{definition.Host},{definition.Port}";
    }

    private static bool LooksLikeNamedInstance(string host)
    {
        return host.Contains('\\', StringComparison.Ordinal)
            || host.Contains("(local)", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, ".", StringComparison.Ordinal);
    }

    private static DbConnection CreateMySql(ConnectionDefinition definition, string? databaseName)
    {
        var endpoint = SshTunnelManager.ResolveEndpoint(definition);
        var builder = new MySqlConnectionStringBuilder
        {
            Server = definition.SshTunnel.Enabled ? endpoint.Host : definition.Host,
            Port = (uint)(definition.SshTunnel.Enabled ? endpoint.Port : definition.Port),
            UserID = definition.Username,
            Password = definition.Password,
            Database = databaseName ?? string.Empty,
            SslMode = MySqlSslMode.Preferred,
        };

        return new MySqlConnection(builder.ConnectionString);
    }

    private static DbConnection CreatePostgreSql(ConnectionDefinition definition, string? databaseName)
    {
        var endpoint = SshTunnelManager.ResolveEndpoint(definition);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = definition.SshTunnel.Enabled ? endpoint.Host : definition.Host,
            Port = definition.SshTunnel.Enabled ? endpoint.Port : definition.Port > 0 ? definition.Port : 5432,
            Database = string.IsNullOrWhiteSpace(databaseName) ? "postgres" : databaseName,
            Username = definition.Username,
            Password = definition.Password,
            SslMode = SslMode.Prefer,
        };

        return new NpgsqlConnection(builder.ConnectionString);
    }

    private static DbConnection CreateSqlite(ConnectionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Host))
        {
            throw new InvalidOperationException("SQLite database path is required.");
        }

        var filePath = Path.GetFullPath(definition.Host.Trim());
        var directory = Path.GetDirectoryName(filePath);
        if (!File.Exists(filePath) && definition.Sqlite.OpenMode == Models.SqliteOpenMode.ReadOnly)
        {
            throw new InvalidOperationException("SQLite 只读连接要求数据库文件已存在。", null);
        }

        if (definition.Sqlite.OpenMode != Models.SqliteOpenMode.ReadOnly && !string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sqliteOptions = ResolveSqliteConnectionOptions(definition.Sqlite);
        SqliteLoadableExtensionRegistry.ValidatePreOpenExtensionUsage(sqliteOptions);

        if (sqliteOptions.Vfs.Kind != Models.SqliteVfsKind.Default)
        {
            return new OffsetSqliteConnection(filePath, sqliteOptions);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = sqliteOptions.OpenMode == Models.SqliteOpenMode.ReadOnly ? Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly : Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = !sqliteOptions.Cipher.Enabled,
        };

        return new ConfiguredSqliteConnection(new SqliteConnection(builder.ConnectionString), sqliteOptions);
    }

    private static SqliteConnectionOptions ResolveSqliteConnectionOptions(SqliteConnectionOptions source)
    {
        var settings = SettingsStore.LoadAsync().GetAwaiter().GetResult();
        return new SqliteConnectionOptions
        {
            OpenMode = source.OpenMode,
            Cipher = new SqliteCipherOptions
            {
                Enabled = source.Cipher.Enabled,
                Password = source.Cipher.Password,
                KeyFormat = source.Cipher.KeyFormat,
                PageSize = source.Cipher.PageSize,
                KdfIter = source.Cipher.KdfIter,
                CipherCompatibility = source.Cipher.CipherCompatibility,
                PlaintextHeaderSize = source.Cipher.PlaintextHeaderSize,
                UseHmac = source.Cipher.UseHmac,
                KdfAlgorithm = source.Cipher.KdfAlgorithm,
                HmacAlgorithm = source.Cipher.HmacAlgorithm,
            },
            Vfs = new SqliteVfsOptions
            {
                Kind = source.Vfs.Kind,
                BuiltInOffset = new SqliteBuiltInOffsetVfsOptions
                {
                    SkipBytes = source.Vfs.BuiltInOffset.SkipBytes,
                },
                Named = new SqliteNamedVfsOptions
                {
                    Name = source.Vfs.Named.Name,
                },
            },
            Extensions = settings.SqliteExtensions.Select(extension => new SqliteLoadableExtensionOptions
            {
                Path = extension.Path,
                EntryPoint = extension.EntryPoint,
                Phase = extension.Phase,
            }).ToList(),
        };
    }

    private static async Task ApplySqliteSessionConfigurationAsync(SqliteConnection connection, SqliteConnectionOptions options, CancellationToken cancellationToken)
    {
        if (options.Cipher.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Cipher.Password))
            {
                throw new InvalidOperationException("SQLCipher 已启用，但未配置密码。", null);
            }

            await ExecutePragmaAsync(connection, BuildKeyPragma(options.Cipher), cancellationToken);
            await ExecuteOptionalIntegerPragmaAsync(connection, "cipher_page_size", options.Cipher.PageSize, cancellationToken);
            await ExecuteOptionalIntegerPragmaAsync(connection, "kdf_iter", options.Cipher.KdfIter, cancellationToken);
            await ExecuteOptionalIntegerPragmaAsync(connection, "cipher_compatibility", options.Cipher.CipherCompatibility, cancellationToken);
            await ExecuteOptionalIntegerPragmaAsync(connection, "cipher_plaintext_header_size", options.Cipher.PlaintextHeaderSize, cancellationToken);
            await ExecuteOptionalBooleanPragmaAsync(connection, "cipher_use_hmac", options.Cipher.UseHmac, cancellationToken);
            await ExecuteOptionalTextPragmaAsync(connection, "cipher_kdf_algorithm", options.Cipher.KdfAlgorithm, cancellationToken);
            await ExecuteOptionalTextPragmaAsync(connection, "cipher_hmac_algorithm", options.Cipher.HmacAlgorithm, cancellationToken);
            await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master;", cancellationToken);
        }

        await ExecutePragmaAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        SqliteLoadableExtensionRegistry.LoadPostOpenExtensions(connection, options.Extensions);
    }

    private static async Task ExecuteOptionalIntegerPragmaAsync(SqliteConnection connection, string pragmaName, int? value, CancellationToken cancellationToken)
    {
        if (!value.HasValue)
        {
            return;
        }

        await ExecutePragmaAsync(connection, $"PRAGMA {ValidatePragmaName(pragmaName)} = {value.Value};", cancellationToken);
    }

    private static async Task ExecuteOptionalBooleanPragmaAsync(SqliteConnection connection, string pragmaName, bool? value, CancellationToken cancellationToken)
    {
        if (!value.HasValue)
        {
            return;
        }

        await ExecutePragmaAsync(connection, $"PRAGMA {ValidatePragmaName(pragmaName)} = {(value.Value ? "ON" : "OFF")};", cancellationToken);
    }

    private static async Task ExecuteOptionalTextPragmaAsync(SqliteConnection connection, string pragmaName, string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        await ExecutePragmaAsync(connection, $"PRAGMA {ValidatePragmaName(pragmaName)} = '{EscapeSqliteTextLiteral(value.Trim())}';", cancellationToken);
    }

    private static async Task ExecutePragmaAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteScalarAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static string BuildKeyPragma(SqliteCipherOptions options)
    {
        if (options.KeyFormat == SqliteCipherKeyFormat.Hex)
        {
            var normalizedHex = NormalizeSqliteHexKey(options.Password);
            return $"PRAGMA hexkey = '{normalizedHex}';";
        }

        return $"PRAGMA key = '{EscapeSqliteTextLiteral(options.Password)}';";
    }

    public static string BuildSqliteRekeyCommandText(SqliteCipherKeyFormat keyFormat, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("SQLCipher 新密码不能为空。", null);
        }

        if (keyFormat == SqliteCipherKeyFormat.Hex)
        {
            var normalizedHex = NormalizeSqliteHexKey(password);
            return $"PRAGMA hexrekey = '{normalizedHex}';";
        }

        return $"PRAGMA rekey = '{EscapeSqliteTextLiteral(password)}';";
    }

    internal static string NormalizeSqliteHexKey(string value)
    {
        var normalizedValue = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalizedValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalizedValue = normalizedValue[2..];
        }

        if (normalizedValue.Length == 0 || normalizedValue.Length % 2 != 0 || !SqliteHexKeyRegex.IsMatch(normalizedValue))
        {
            throw new InvalidOperationException("SQLCipher 十六进制密钥必须是偶数长度的十六进制字符串。", null);
        }

        return normalizedValue.ToUpperInvariant();
    }

    internal static string EscapeSqliteTextLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string ValidatePragmaName(string pragmaName)
    {
        if (!SqlitePragmaNameRegex.IsMatch(pragmaName))
        {
            throw new InvalidOperationException($"无效的 SQLite PRAGMA 名称: {pragmaName}", null);
        }

        return pragmaName;
    }

    private sealed class ConfiguredSqliteConnection : DbConnection
    {
        private readonly SqliteConnection _innerConnection;
        private readonly SqliteConnectionOptions _sqliteOptions;
        private bool _sessionConfigured;

        public ConfiguredSqliteConnection(SqliteConnection innerConnection, SqliteConnectionOptions sqliteOptions)
        {
            _innerConnection = innerConnection;
            _sqliteOptions = sqliteOptions;
        }

        [AllowNull]
        public override string ConnectionString
        {
            get => _innerConnection.ConnectionString;
            set => _innerConnection.ConnectionString = value ?? string.Empty;
        }

        public override string Database => _innerConnection.Database;

        public override string DataSource => _innerConnection.DataSource;

        public override string ServerVersion => _innerConnection.ServerVersion;

        public override ConnectionState State => _innerConnection.State;

        public override void ChangeDatabase(string databaseName)
        {
            _innerConnection.ChangeDatabase(databaseName);
        }

        public override void Close()
        {
            _sessionConfigured = false;
            _innerConnection.Close();
        }

        public override async Task CloseAsync()
        {
            _sessionConfigured = false;
            await _innerConnection.CloseAsync();
        }

        public override void Open()
        {
            if (_innerConnection.State != ConnectionState.Open)
            {
                _innerConnection.Open();
            }

            if (_sessionConfigured)
            {
                return;
            }

            try
            {
                ApplySqliteSessionConfigurationAsync(_innerConnection, _sqliteOptions, CancellationToken.None).GetAwaiter().GetResult();
                _sessionConfigured = true;
            }
            catch
            {
                _innerConnection.Close();
                throw;
            }
        }

        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            if (_innerConnection.State != ConnectionState.Open)
            {
                await _innerConnection.OpenAsync(cancellationToken);
            }

            if (_sessionConfigured)
            {
                return;
            }

            try
            {
                await ApplySqliteSessionConfigurationAsync(_innerConnection, _sqliteOptions, cancellationToken);
                _sessionConfigured = true;
            }
            catch
            {
                await _innerConnection.CloseAsync();
                throw;
            }
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => _innerConnection.BeginTransaction(isolationLevel);

        protected override DbCommand CreateDbCommand()
            => _innerConnection.CreateCommand();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerConnection.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _innerConnection.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}