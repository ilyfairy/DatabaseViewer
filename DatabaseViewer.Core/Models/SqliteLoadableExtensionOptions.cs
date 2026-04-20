namespace DatabaseViewer.Core.Models;

/// <summary>
/// Describes one SQLite loadable extension to load for a connection.
/// </summary>
public sealed class SqliteLoadableExtensionOptions
{
    public string Path { get; set; } = string.Empty;

    public string EntryPoint { get; set; } = string.Empty;

    public SqliteLoadableExtensionPhase Phase { get; set; } = SqliteLoadableExtensionPhase.PreOpen;
}