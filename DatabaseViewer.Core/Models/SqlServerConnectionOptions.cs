namespace DatabaseViewer.Core.Models;

public sealed class SqlServerConnectionOptions
{
    public SqlServerAuthenticationMode AuthenticationMode { get; set; } = SqlServerAuthenticationMode.UsernamePassword;

    public bool TrustServerCertificate { get; set; } = true;
}