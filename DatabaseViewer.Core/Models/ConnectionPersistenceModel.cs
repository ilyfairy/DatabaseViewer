namespace DatabaseViewer.Core.Models;

public sealed class ConnectionPersistenceModel
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DatabaseProviderType ProviderType { get; set; }

    public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.UsernamePassword;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Username { get; set; } = string.Empty;

    public string EncryptedPassword { get; set; } = string.Empty;

    public bool TrustServerCertificate { get; set; }
}