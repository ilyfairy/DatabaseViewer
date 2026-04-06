namespace DatabaseViewer.Core.Models;

public sealed class DbCatalogObjectDetailInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string ObjectName { get; set; } = string.Empty;

    public DbCatalogObjectType ObjectType { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string? Definition { get; set; }

    public IReadOnlyList<DbCatalogObjectProperty> Properties { get; set; } = Array.Empty<DbCatalogObjectProperty>();
}