namespace DatabaseViewer.Core.Models;

public sealed class SqliteConnectionOptions
{
    public SqliteOpenMode OpenMode { get; set; } = SqliteOpenMode.ReadWrite;

    public SqliteCipherOptions Cipher { get; set; } = new();

    public SqliteVfsOptions Vfs { get; set; } = new();

    public List<SqliteLoadableExtensionOptions> Extensions { get; set; } = [];
}