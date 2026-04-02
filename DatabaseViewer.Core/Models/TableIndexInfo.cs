namespace DatabaseViewer.Core.Models;

public sealed class TableIndexInfo
{
    public string Name { get; set; } = string.Empty;

    public bool IsPrimaryKey { get; set; }

    public bool IsUnique { get; set; }

    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
}