namespace DatabaseViewer.Core.Models;

public sealed class ConnectionDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public DatabaseProviderType ProviderType { get; set; }

    public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.UsernamePassword;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool TrustServerCertificate { get; set; } = true;

    public SshTunnelOptions SshTunnel { get; set; } = new();

    public SqliteOpenMode SqliteOpenMode { get; set; } = SqliteOpenMode.ReadWrite;

    public SqliteCipherOptions SqliteCipher { get; set; } = new();

    public string DisplayLabel => $"{Name} ({ProviderType})";
}