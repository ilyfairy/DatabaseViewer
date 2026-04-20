using System.IO;
using System.Text.Json;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

public sealed class ConnectionStore
{
    private readonly Base64DataCodec _codec;
    private readonly string _filePath;

    public ConnectionStore(Base64DataCodec codec)
    {
        _codec = codec;

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DatabaseViewer");

        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "connections.json");
    }

    public async Task<IReadOnlyList<ConnectionDefinition>> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<ConnectionDefinition>();
        }

        await using var stream = File.OpenRead(_filePath);
        var persisted = await JsonSerializer.DeserializeAsync<List<ConnectionPersistenceModel>>(stream, PersistenceJson.ConnectionStoreOptions)
            ?? new List<ConnectionPersistenceModel>();

        return persisted.Select(item =>
        {
            var sqlServer = item.SqlServer ?? new ConnectionSqlServerPersistenceModel();
            var sqlite = item.Sqlite ?? new ConnectionSqlitePersistenceModel();
            var ssh = item.Ssh ?? new ConnectionSshPersistenceModel();

            return new ConnectionDefinition
            {
                Id = item.Id,
                Name = item.Name,
                ProviderType = item.ProviderType,
                Host = item.Host,
                Port = item.Port,
                Username = item.Username,
                Password = _codec.Decode(item.EncryptedPassword),
                SqlServer = new SqlServerConnectionOptions
                {
                    AuthenticationMode = sqlServer.AuthenticationMode,
                    TrustServerCertificate = sqlServer.TrustServerCertificate,
                },
                MySql = new MySqlConnectionOptions(),
                PostgreSql = new PostgreSqlConnectionOptions(),
                Sqlite = new SqliteConnectionOptions
                {
                    OpenMode = sqlite.OpenMode,
                    Cipher = new SqliteCipherOptions
                    {
                        Enabled = sqlite.Cipher.Enabled,
                        Password = string.IsNullOrWhiteSpace(sqlite.Cipher.EncryptedPassword) ? string.Empty : _codec.Decode(sqlite.Cipher.EncryptedPassword),
                        KeyFormat = sqlite.Cipher.KeyFormat,
                        PageSize = sqlite.Cipher.PageSize,
                        KdfIter = sqlite.Cipher.KdfIter,
                        CipherCompatibility = sqlite.Cipher.CipherCompatibility,
                        PlaintextHeaderSize = sqlite.Cipher.PlaintextHeaderSize,
                        UseHmac = sqlite.Cipher.UseHmac,
                        KdfAlgorithm = sqlite.Cipher.KdfAlgorithm,
                        HmacAlgorithm = sqlite.Cipher.HmacAlgorithm,
                    },
                    Vfs = new SqliteVfsOptions
                    {
                        Kind = sqlite.Vfs.Kind,
                        BuiltInOffset = new SqliteBuiltInOffsetVfsOptions
                        {
                            SkipBytes = sqlite.Vfs.BuiltInOffset.SkipBytes,
                        },
                        Named = new SqliteNamedVfsOptions
                        {
                            Name = sqlite.Vfs.Named.Name,
                        },
                    },
                },
                SshTunnel = new SshTunnelOptions
                {
                    Enabled = ssh.Enabled,
                    AuthenticationMode = ssh.AuthenticationMode,
                    Host = ssh.Host,
                    Port = ssh.Port > 0 ? ssh.Port : 22,
                    Username = ssh.Username,
                    Password = string.IsNullOrWhiteSpace(ssh.EncryptedPassword) ? string.Empty : _codec.Decode(ssh.EncryptedPassword),
                    PrivateKeyPath = ssh.PrivateKeyPath,
                    Passphrase = string.IsNullOrWhiteSpace(ssh.EncryptedPassphrase) ? string.Empty : _codec.Decode(ssh.EncryptedPassphrase),
                },
            };
        }).ToArray();
    }

    public async Task SaveAsync(IReadOnlyList<ConnectionDefinition> connections)
    {
        var persisted = connections.Select(item => new ConnectionPersistenceModel
        {
            Id = item.Id,
            Name = item.Name,
            ProviderType = item.ProviderType,
            Host = item.Host,
            Port = item.Port,
            Username = item.Username,
            EncryptedPassword = _codec.Encode(item.Password),
            SqlServer = BuildSqlServerPersistence(item),
            MySql = item.ProviderType == DatabaseProviderType.MySql ? new ConnectionMySqlPersistenceModel() : null,
            PostgreSql = item.ProviderType == DatabaseProviderType.PostgreSql ? new ConnectionPostgreSqlPersistenceModel() : null,
            Sqlite = BuildSqlitePersistence(item),
            Ssh = BuildSshPersistence(item),
        }).ToArray();

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, persisted, PersistenceJson.ConnectionStoreOptions);
    }

    private ConnectionSqlServerPersistenceModel? BuildSqlServerPersistence(ConnectionDefinition item)
    {
        if (item.ProviderType != DatabaseProviderType.SqlServer)
        {
            return null;
        }

        return new ConnectionSqlServerPersistenceModel
        {
            AuthenticationMode = item.SqlServer.AuthenticationMode,
            TrustServerCertificate = item.SqlServer.TrustServerCertificate,
        };
    }

    private ConnectionSqlitePersistenceModel? BuildSqlitePersistence(ConnectionDefinition item)
    {
        if (item.ProviderType != DatabaseProviderType.Sqlite)
        {
            return null;
        }

        return new ConnectionSqlitePersistenceModel
        {
            OpenMode = item.Sqlite.OpenMode,
            Cipher = new ConnectionSqliteCipherPersistenceModel
            {
                Enabled = item.Sqlite.Cipher.Enabled,
                EncryptedPassword = string.IsNullOrWhiteSpace(item.Sqlite.Cipher.Password) ? string.Empty : _codec.Encode(item.Sqlite.Cipher.Password),
                KeyFormat = item.Sqlite.Cipher.KeyFormat,
                PageSize = item.Sqlite.Cipher.PageSize,
                KdfIter = item.Sqlite.Cipher.KdfIter,
                CipherCompatibility = item.Sqlite.Cipher.CipherCompatibility,
                PlaintextHeaderSize = item.Sqlite.Cipher.PlaintextHeaderSize,
                UseHmac = item.Sqlite.Cipher.UseHmac,
                KdfAlgorithm = item.Sqlite.Cipher.KdfAlgorithm,
                HmacAlgorithm = item.Sqlite.Cipher.HmacAlgorithm,
            },
            Vfs = new ConnectionSqliteVfsPersistenceModel
            {
                Kind = item.Sqlite.Vfs.Kind,
                BuiltInOffset = new ConnectionSqliteBuiltInOffsetVfsPersistenceModel
                {
                    SkipBytes = item.Sqlite.Vfs.BuiltInOffset.SkipBytes,
                },
                Named = new ConnectionSqliteNamedVfsPersistenceModel
                {
                    Name = item.Sqlite.Vfs.Named.Name,
                },
            },
        };
    }

    private ConnectionSshPersistenceModel? BuildSshPersistence(ConnectionDefinition item)
    {
        if (!item.SshTunnel.Enabled)
        {
            return null;
        }

        return new ConnectionSshPersistenceModel
        {
            Enabled = item.SshTunnel.Enabled,
            AuthenticationMode = item.SshTunnel.AuthenticationMode,
            Host = item.SshTunnel.Host,
            Port = item.SshTunnel.Port,
            Username = item.SshTunnel.Username,
            EncryptedPassword = string.IsNullOrWhiteSpace(item.SshTunnel.Password) ? string.Empty : _codec.Encode(item.SshTunnel.Password),
            PrivateKeyPath = item.SshTunnel.PrivateKeyPath,
            EncryptedPassphrase = string.IsNullOrWhiteSpace(item.SshTunnel.Passphrase) ? string.Empty : _codec.Encode(item.SshTunnel.Passphrase),
        };
    }
}