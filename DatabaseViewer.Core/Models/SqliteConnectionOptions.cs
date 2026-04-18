namespace DatabaseViewer.Core.Models;

public sealed class SqliteConnectionOptions
{
    public SqliteOpenMode OpenMode { get; set; } = SqliteOpenMode.ReadWrite;

    public SqliteCipherOptions Cipher { get; set; } = new();
}