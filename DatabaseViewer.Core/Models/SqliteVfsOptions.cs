namespace DatabaseViewer.Core.Models;

/// <summary>
/// Stores SQLite virtual file system selection independently from SQLCipher settings.
/// </summary>
public sealed class SqliteVfsOptions
{
    public SqliteVfsKind Kind { get; set; } = SqliteVfsKind.Default;

    public SqliteBuiltInOffsetVfsOptions BuiltInOffset { get; set; } = new();

    public SqliteNamedVfsOptions Named { get; set; } = new();
}

/// <summary>
/// Configuration for the built-in offset VFS shipped with the application.
/// </summary>
public sealed class SqliteBuiltInOffsetVfsOptions
{
    public int? SkipBytes { get; set; }
}

/// <summary>
/// Configuration for selecting a named SQLite VFS that has already been registered in the process.
/// </summary>
public sealed class SqliteNamedVfsOptions
{
    public string Name { get; set; } = string.Empty;
}