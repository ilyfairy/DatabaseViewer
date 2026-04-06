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
            TrustServerCertificate = item.TrustServerCertificate,
            SshTunnel = new SshTunnelOptions
            {
                Enabled = item.SshEnabled,
                AuthenticationMode = item.SshAuthenticationMode,
                Host = item.SshHost,
                Port = item.SshPort > 0 ? item.SshPort : 22,
                Username = item.SshUsername,
                Password = string.IsNullOrWhiteSpace(item.EncryptedSshPassword) ? string.Empty : _protector.Unprotect(item.EncryptedSshPassword),
                PrivateKeyPath = item.SshPrivateKeyPath,
                Passphrase = string.IsNullOrWhiteSpace(item.EncryptedSshPassphrase) ? string.Empty : _protector.Unprotect(item.EncryptedSshPassphrase),
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
            SshEnabled = item.SshTunnel.Enabled,
            SshAuthenticationMode = item.SshTunnel.AuthenticationMode,
            SshHost = item.SshTunnel.Host,
            SshPort = item.SshTunnel.Port,
            SshUsername = item.SshTunnel.Username,
            EncryptedSshPassword = string.IsNullOrWhiteSpace(item.SshTunnel.Password) ? string.Empty : _protector.Protect(item.SshTunnel.Password),
            SshPrivateKeyPath = item.SshTunnel.PrivateKeyPath,
            EncryptedSshPassphrase = string.IsNullOrWhiteSpace(item.SshTunnel.Passphrase) ? string.Empty : _protector.Protect(item.SshTunnel.Passphrase),
        }).ToArray();

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, persisted, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}