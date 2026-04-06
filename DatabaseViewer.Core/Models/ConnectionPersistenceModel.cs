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

    public bool SshEnabled { get; set; }

    public SshAuthenticationMode SshAuthenticationMode { get; set; }

    public string SshHost { get; set; } = string.Empty;

    public int SshPort { get; set; } = 22;

    public string SshUsername { get; set; } = string.Empty;

    public string EncryptedSshPassword { get; set; } = string.Empty;

    public string SshPrivateKeyPath { get; set; } = string.Empty;

    public string EncryptedSshPassphrase { get; set; } = string.Empty;
}