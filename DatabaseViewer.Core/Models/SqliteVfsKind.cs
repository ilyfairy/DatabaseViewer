namespace DatabaseViewer.Core.Models;

/// <summary>
/// Identifies how a SQLite database should be opened at the VFS layer.
/// </summary>
public enum SqliteVfsKind
{
    Default = 0,
    BuiltInOffset = 1,
    Named = 2,
}