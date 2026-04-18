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
        var persisted = await JsonSerializer.DeserializeAsync<List<ConnectionPersistenceModel>>(stream)
            ?? new List<ConnectionPersistenceModel>();

        return persisted.Select(item => new ConnectionDefinition
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
                AuthenticationMode = item.SqlServer.AuthenticationMode,
                TrustServerCertificate = item.SqlServer.TrustServerCertificate,
            },
            MySql = new MySqlConnectionOptions(),
            PostgreSql = new PostgreSqlConnectionOptions(),
            Sqlite = new SqliteConnectionOptions
            {
                OpenMode = item.Sqlite.OpenMode,
                Cipher = new SqliteCipherOptions
                {
                    Enabled = item.Sqlite.Cipher.Enabled,
                    Password = string.IsNullOrWhiteSpace(item.Sqlite.Cipher.EncryptedPassword) ? string.Empty : _codec.Decode(item.Sqlite.Cipher.EncryptedPassword),
                    KeyFormat = item.Sqlite.Cipher.KeyFormat,
                    PageSize = item.Sqlite.Cipher.PageSize,
                    KdfIter = item.Sqlite.Cipher.KdfIter,
                    CipherCompatibility = item.Sqlite.Cipher.CipherCompatibility,
                    PlaintextHeaderSize = item.Sqlite.Cipher.PlaintextHeaderSize,
                    SkipBytes = item.Sqlite.Cipher.SkipBytes,
                    UseHmac = item.Sqlite.Cipher.UseHmac,
                    KdfAlgorithm = item.Sqlite.Cipher.KdfAlgorithm,
                    HmacAlgorithm = item.Sqlite.Cipher.HmacAlgorithm,
                },
            },
            SshTunnel = new SshTunnelOptions
            {
                Enabled = item.Ssh.Enabled,
                AuthenticationMode = item.Ssh.AuthenticationMode,
                Host = item.Ssh.Host,
                Port = item.Ssh.Port > 0 ? item.Ssh.Port : 22,
                Username = item.Ssh.Username,
                Password = string.IsNullOrWhiteSpace(item.Ssh.EncryptedPassword) ? string.Empty : _codec.Decode(item.Ssh.EncryptedPassword),
                PrivateKeyPath = item.Ssh.PrivateKeyPath,
                Passphrase = string.IsNullOrWhiteSpace(item.Ssh.EncryptedPassphrase) ? string.Empty : _codec.Decode(item.Ssh.EncryptedPassphrase),
            },
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
            SqlServer = new ConnectionSqlServerPersistenceModel
            {
                AuthenticationMode = item.SqlServer.AuthenticationMode,
                TrustServerCertificate = item.SqlServer.TrustServerCertificate,
            },
            MySql = new ConnectionMySqlPersistenceModel(),
            PostgreSql = new ConnectionPostgreSqlPersistenceModel(),
            Sqlite = new ConnectionSqlitePersistenceModel
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
                    SkipBytes = item.Sqlite.Cipher.SkipBytes,
                    UseHmac = item.Sqlite.Cipher.UseHmac,
                    KdfAlgorithm = item.Sqlite.Cipher.KdfAlgorithm,
                    HmacAlgorithm = item.Sqlite.Cipher.HmacAlgorithm,
                },
            },
            Ssh = new ConnectionSshPersistenceModel
            {
                Enabled = item.SshTunnel.Enabled,
                AuthenticationMode = item.SshTunnel.AuthenticationMode,
                Host = item.SshTunnel.Host,
                Port = item.SshTunnel.Port,
                Username = item.SshTunnel.Username,
                EncryptedPassword = string.IsNullOrWhiteSpace(item.SshTunnel.Password) ? string.Empty : _codec.Encode(item.SshTunnel.Password),
                PrivateKeyPath = item.SshTunnel.PrivateKeyPath,
                EncryptedPassphrase = string.IsNullOrWhiteSpace(item.SshTunnel.Passphrase) ? string.Empty : _codec.Encode(item.SshTunnel.Passphrase),
            },
        }).ToArray();

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, persisted, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}