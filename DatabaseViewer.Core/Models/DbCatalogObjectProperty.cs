namespace DatabaseViewer.Core.Models;

public sealed class DbCatalogObjectProperty
{
    public string Label { get; set; } = string.Empty;

    public string? Value { get; set; }
}