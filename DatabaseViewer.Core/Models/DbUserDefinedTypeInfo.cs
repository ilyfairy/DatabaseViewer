namespace DatabaseViewer.Core.Models;

public sealed class DbUserDefinedTypeInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public string BaseTypeName { get; set; } = string.Empty;

    public bool IsTableType { get; set; }
}