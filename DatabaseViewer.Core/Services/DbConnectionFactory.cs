using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

public static class DbConnectionFactory
{
    public static DbConnection Create(ConnectionDefinition definition, string? databaseName = null)
    {
        return definition.ProviderType switch
        {
            DatabaseProviderType.SqlServer => CreateSqlServer(definition, databaseName),
            DatabaseProviderType.MySql => CreateMySql(definition, databaseName),
            DatabaseProviderType.PostgreSql => CreatePostgreSql(definition, databaseName),
            _ => throw new NotSupportedException($"Unsupported provider: {definition.ProviderType}"),
        };
    }

    private static DbConnection CreateSqlServer(ConnectionDefinition definition, string? databaseName)
    {
        var dataSource = BuildSqlServerDataSource(definition);
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = string.IsNullOrWhiteSpace(databaseName) ? "master" : databaseName,
            TrustServerCertificate = definition.TrustServerCertificate,
            Encrypt = true,
            MultipleActiveResultSets = false,
        };

        if (definition.AuthenticationMode == AuthenticationMode.WindowsIntegrated)
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
        var builder = new MySqlConnectionStringBuilder
        {
            Server = definition.Host,
            Port = (uint)definition.Port,
            UserID = definition.Username,
            Password = definition.Password,
            Database = databaseName ?? string.Empty,
            SslMode = MySqlSslMode.Preferred,
        };

        return new MySqlConnection(builder.ConnectionString);
    }

    private static DbConnection CreatePostgreSql(ConnectionDefinition definition, string? databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = definition.Host,
            Port = definition.Port > 0 ? definition.Port : 5432,
            Database = string.IsNullOrWhiteSpace(databaseName) ? "postgres" : databaseName,
            Username = definition.Username,
            Password = definition.Password,
            SslMode = SslMode.Prefer,
        };

        return new NpgsqlConnection(builder.ConnectionString);
    }
}