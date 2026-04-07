using System.IO;
using System.Text.Json;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

public sealed class ConnectionStore
{
    private readonly WindowsDataProtector _protector;
    private readonly string _filePath;

    public ConnectionStore(WindowsDataProtector protector)
    {
        _protector = protector;

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
            AuthenticationMode = item.AuthenticationMode,
            Host = item.Host,
            Port = item.Port,
            Username = item.Username,
            Password = _protector.Unprotect(item.EncryptedPassword),
            SqliteCipher = new SqliteCipherOptions
            {
                Enabled = item.SqliteCipher.Enabled,
                Password = string.IsNullOrWhiteSpace(item.SqliteCipher.EncryptedPassword) ? string.Empty : _protector.Unprotect(item.SqliteCipher.EncryptedPassword),
                KeyFormat = item.SqliteCipher.KeyFormat,
                PageSize = item.SqliteCipher.PageSize,
                KdfIter = item.SqliteCipher.KdfIter,
                CipherCompatibility = item.SqliteCipher.CipherCompatibility,
                PlaintextHeaderSize = item.SqliteCipher.PlaintextHeaderSize,
                SkipBytes = item.SqliteCipher.SkipBytes,
                UseHmac = item.SqliteCipher.UseHmac,
                KdfAlgorithm = item.SqliteCipher.KdfAlgorithm,
                HmacAlgorithm = item.SqliteCipher.HmacAlgorithm,
            },
            TrustServerCertificate = item.TrustServerCertificate,
            SshTunnel = new SshTunnelOptions
            {
                Enabled = item.Ssh.Enabled,
                AuthenticationMode = item.Ssh.AuthenticationMode,
                Host = item.Ssh.Host,
                Port = item.Ssh.Port > 0 ? item.Ssh.Port : 22,
                Username = item.Ssh.Username,
                Password = string.IsNullOrWhiteSpace(item.Ssh.EncryptedPassword) ? string.Empty : _protector.Unprotect(item.Ssh.EncryptedPassword),
                PrivateKeyPath = item.Ssh.PrivateKeyPath,
                Passphrase = string.IsNullOrWhiteSpace(item.Ssh.EncryptedPassphrase) ? string.Empty : _protector.Unprotect(item.Ssh.EncryptedPassphrase),
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
            AuthenticationMode = item.AuthenticationMode,
            Host = item.Host,
            Port = item.Port,
            Username = item.Username,
            EncryptedPassword = _protector.Protect(item.Password),
            TrustServerCertificate = item.TrustServerCertificate,
            SqliteCipher = new ConnectionSqliteCipherPersistenceModel
            {
                Enabled = item.SqliteCipher.Enabled,
                EncryptedPassword = string.IsNullOrWhiteSpace(item.SqliteCipher.Password) ? string.Empty : _protector.Protect(item.SqliteCipher.Password),
                KeyFormat = item.SqliteCipher.KeyFormat,
                PageSize = item.SqliteCipher.PageSize,
                KdfIter = item.SqliteCipher.KdfIter,
                CipherCompatibility = item.SqliteCipher.CipherCompatibility,
                PlaintextHeaderSize = item.SqliteCipher.PlaintextHeaderSize,
                SkipBytes = item.SqliteCipher.SkipBytes,
                UseHmac = item.SqliteCipher.UseHmac,
                KdfAlgorithm = item.SqliteCipher.KdfAlgorithm,
                HmacAlgorithm = item.SqliteCipher.HmacAlgorithm,
            },
            Ssh = new ConnectionSshPersistenceModel
            {
                Enabled = item.SshTunnel.Enabled,
                AuthenticationMode = item.SshTunnel.AuthenticationMode,
                Host = item.SshTunnel.Host,
                Port = item.SshTunnel.Port,
                Username = item.SshTunnel.Username,
                EncryptedPassword = string.IsNullOrWhiteSpace(item.SshTunnel.Password) ? string.Empty : _protector.Protect(item.SshTunnel.Password),
                PrivateKeyPath = item.SshTunnel.PrivateKeyPath,
                EncryptedPassphrase = string.IsNullOrWhiteSpace(item.SshTunnel.Passphrase) ? string.Empty : _protector.Protect(item.SshTunnel.Passphrase),
            },
        }).ToArray();

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, persisted, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}