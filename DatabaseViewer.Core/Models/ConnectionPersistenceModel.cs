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

    public SqliteOpenMode SqliteOpenMode { get; set; } = SqliteOpenMode.ReadWrite;

    public ConnectionSqliteCipherPersistenceModel SqliteCipher { get; set; } = new();

    public ConnectionSshPersistenceModel Ssh { get; set; } = new();
}

public sealed class ConnectionSqliteCipherPersistenceModel
{
    public bool Enabled { get; set; }

    public string EncryptedPassword { get; set; } = string.Empty;

    public SqliteCipherKeyFormat KeyFormat { get; set; } = SqliteCipherKeyFormat.Passphrase;

    public int? PageSize { get; set; }

    public int? KdfIter { get; set; }

    public int? CipherCompatibility { get; set; }

    public int? PlaintextHeaderSize { get; set; }

    public int? SkipBytes { get; set; }

    public bool? UseHmac { get; set; }

    public string KdfAlgorithm { get; set; } = string.Empty;

    public string HmacAlgorithm { get; set; } = string.Empty;
}

public sealed class ConnectionSshPersistenceModel
{
    public bool Enabled { get; set; }

    public SshAuthenticationMode AuthenticationMode { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string EncryptedPassword { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public string EncryptedPassphrase { get; set; } = string.Empty;
}