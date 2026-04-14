namespace DatabaseViewer.Api;

/// <summary>
/// Defines the optional host settings loaded from the program directory.
/// </summary>
public sealed class ApiHostSettings
{
    public const string SectionName = "ApiHost";

    /// <summary>
    /// Configures the Kestrel listen URL, for example http://127.0.0.1:5027 or http://*:5027.
    /// </summary>
    public string? ListenUrl { get; init; }

    /// <summary>
    /// Configures the client IP allow-list using IPs or CIDR ranges.
    /// </summary>
    public string[]? AllowedNetworks { get; init; }
}