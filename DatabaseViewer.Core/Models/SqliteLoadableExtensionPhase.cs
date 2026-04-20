namespace DatabaseViewer.Core.Models;

/// <summary>
/// Controls whether a SQLite loadable extension is loaded before opening the database file
/// or after the connection itself is already open.
/// </summary>
public enum SqliteLoadableExtensionPhase
{
    PreOpen = 0,
    PostOpen = 1,
}