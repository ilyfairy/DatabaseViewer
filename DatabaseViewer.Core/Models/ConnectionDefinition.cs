namespace DatabaseViewer.Core.Models;

public sealed class ConnectionDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public DatabaseProviderType ProviderType { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public SshTunnelOptions SshTunnel { get; set; } = new();

    public SqlServerConnectionOptions SqlServer { get; set; } = new();

    public MySqlConnectionOptions MySql { get; set; } = new();

    public PostgreSqlConnectionOptions PostgreSql { get; set; } = new();

    public SqliteConnectionOptions Sqlite { get; set; } = new();

    public string DisplayLabel => $"{Name} ({ProviderType})";
}