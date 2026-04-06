using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DatabaseViewer.Core.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace DatabaseViewer.Core.Services;

public static class SshTunnelManager
{
    private static readonly ConcurrentDictionary<Guid, TunnelHandle> Handles = new();

    public static (string Host, int Port) ResolveEndpoint(ConnectionDefinition definition)
    {
        if (!definition.SshTunnel.Enabled)
        {
            Release(definition.Id);
            return (definition.Host, definition.Port);
        }

        var handle = Handles.GetOrAdd(definition.Id, _ => new TunnelHandle());
        return handle.EnsureStarted(definition);
    }

    public static void Release(Guid connectionId)
    {
        if (Handles.TryRemove(connectionId, out var handle))
        {
            handle.Dispose();
        }
    }

    private sealed class TunnelHandle : IDisposable
    {
        private readonly object _syncRoot = new();
        private SshClient? _client;
        private ForwardedPortLocal? _port;
        private string _fingerprint = string.Empty;
        private int _localPort;

        public (string Host, int Port) EnsureStarted(ConnectionDefinition definition)
        {
            lock (_syncRoot)
            {
                var fingerprint = BuildFingerprint(definition);
                if (_client?.IsConnected == true && _port?.IsStarted == true && string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return (IPAddress.Loopback.ToString(), _localPort);
                }

                DisposeCurrent();

                _localPort = ReserveLocalPort();
                _client = new SshClient(BuildConnectionInfo(definition));
                _client.Connect();

                _port = new ForwardedPortLocal(
                    IPAddress.Loopback.ToString(),
                    (uint)_localPort,
                    definition.Host,
                    (uint)GetRemotePort(definition));
                _client.AddForwardedPort(_port);
                _port.Start();
                _fingerprint = fingerprint;

                return (IPAddress.Loopback.ToString(), _localPort);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                DisposeCurrent();
            }
        }

        private void DisposeCurrent()
        {
            if (_port is not null)
            {
                try
                {
                    if (_port.IsStarted)
                    {
                        _port.Stop();
                    }
                }
                catch
                {
                }

                _port.Dispose();
                _port = null;
            }

            if (_client is not null)
            {
                try
                {
                    if (_client.IsConnected)
                    {
                        _client.Disconnect();
                    }
                }
                catch
                {
                }

                _client.Dispose();
                _client = null;
            }

            _fingerprint = string.Empty;
            _localPort = 0;
        }

        private static string BuildFingerprint(ConnectionDefinition definition)
        {
            return string.Join('|',
                definition.SshTunnel.Host,
                definition.SshTunnel.Port,
                definition.SshTunnel.Username,
                definition.SshTunnel.Password,
                definition.SshTunnel.AuthenticationMode,
                definition.SshTunnel.PrivateKeyPath,
                definition.SshTunnel.Passphrase,
                definition.Host,
                GetRemotePort(definition));
        }

        private static ConnectionInfo BuildConnectionInfo(ConnectionDefinition definition)
        {
            if (definition.SshTunnel.AuthenticationMode == SshAuthenticationMode.PublicKey)
            {
                var keyPath = ResolvePrivateKeyPath(definition.SshTunnel);
                try
                {
                    var keyFile = string.IsNullOrWhiteSpace(definition.SshTunnel.Passphrase)
                        ? new PrivateKeyFile(keyPath)
                        : new PrivateKeyFile(keyPath, definition.SshTunnel.Passphrase);

                    return new ConnectionInfo(
                        definition.SshTunnel.Host,
                        definition.SshTunnel.Port > 0 ? definition.SshTunnel.Port : 22,
                        definition.SshTunnel.Username,
                        new PrivateKeyAuthenticationMethod(definition.SshTunnel.Username, keyFile));
                }
                catch (SshPassPhraseNullOrEmptyException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"SSH 私钥加载失败：{ex.Message}", ex);
                }
            }

            return new ConnectionInfo(
                definition.SshTunnel.Host,
                definition.SshTunnel.Port > 0 ? definition.SshTunnel.Port : 22,
                definition.SshTunnel.Username,
                new PasswordAuthenticationMethod(definition.SshTunnel.Username, definition.SshTunnel.Password));
        }

        private static string ResolvePrivateKeyPath(SshTunnelOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath))
            {
                var explicitPath = Path.GetFullPath(options.PrivateKeyPath.Trim());
                if (!File.Exists(explicitPath))
                {
                    throw new InvalidOperationException($"SSH 私钥文件不存在：{explicitPath}");
                }

                return explicitPath;
            }

            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sshDirectory = Path.Combine(homeDirectory, ".ssh");
            foreach (var candidate in new[] { "id_ed25519", "id_rsa", "id_ecdsa", "id_dsa" })
            {
                var keyPath = Path.Combine(sshDirectory, candidate);
                if (File.Exists(keyPath))
                {
                    return keyPath;
                }
            }

            throw new InvalidOperationException("未找到可用的 SSH 私钥文件。请在 ~/.ssh 下放置常见私钥，或在连接配置中指定自定义私钥路径。");
        }

        private static int ReserveLocalPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static int GetRemotePort(ConnectionDefinition definition)
        {
            return definition.Port > 0 ? definition.Port : definition.ProviderType switch
            {
                DatabaseProviderType.SqlServer => 1433,
                DatabaseProviderType.MySql => 3306,
                DatabaseProviderType.PostgreSql => 5432,
                _ => 0,
            };
        }
    }
}