namespace DatabaseViewer.Core.Models;

public sealed class DbXmlSchemaCollectionInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string CollectionName { get; set; } = string.Empty;

    public int XmlNamespaceCount { get; set; }
}