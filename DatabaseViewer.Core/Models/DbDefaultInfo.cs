namespace DatabaseViewer.Core.Models;

public sealed class DbDefaultInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string DefaultName { get; set; } = string.Empty;

    public string? Definition { get; set; }
}