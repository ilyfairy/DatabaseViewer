namespace DatabaseViewer.Core.Models;

public sealed class ConnectionPersistenceModel
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DatabaseProviderType ProviderType { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Username { get; set; } = string.Empty;

    public string EncryptedPassword { get; set; } = string.Empty;

    public ConnectionSqlServerPersistenceModel SqlServer { get; set; } = new();

    public ConnectionMySqlPersistenceModel MySql { get; set; } = new();

    public ConnectionPostgreSqlPersistenceModel PostgreSql { get; set; } = new();

    public ConnectionSqlitePersistenceModel Sqlite { get; set; } = new();

    public ConnectionSshPersistenceModel Ssh { get; set; } = new();
}

public sealed class ConnectionSqlServerPersistenceModel
{
    public SqlServerAuthenticationMode AuthenticationMode { get; set; } = SqlServerAuthenticationMode.UsernamePassword;

    public bool TrustServerCertificate { get; set; } = true;
}

public sealed class ConnectionMySqlPersistenceModel
{
}

public sealed class ConnectionPostgreSqlPersistenceModel
{
}

public sealed class ConnectionSqlitePersistenceModel
{
    public SqliteOpenMode OpenMode { get; set; } = SqliteOpenMode.ReadWrite;

    public ConnectionSqliteCipherPersistenceModel Cipher { get; set; } = new();
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