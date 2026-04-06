namespace DatabaseViewer.Core.Models;

public sealed class SshTunnelOptions
{
    public bool Enabled { get; set; }

    public SshAuthenticationMode AuthenticationMode { get; set; } = SshAuthenticationMode.Password;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public string Passphrase { get; set; } = string.Empty;
}