namespace DatabaseViewer.Core.Models;

public sealed class DbTableInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string TableName { get; set; } = string.Empty;

    public DbObjectType ObjectType { get; set; } = DbObjectType.Table;

    public string? Comment { get; set; }

    public int? RowCount { get; set; }

    public string QualifiedKey => string.IsNullOrWhiteSpace(SchemaName)
        ? $"{DatabaseName}.{TableName}"
        : $"{DatabaseName}.{SchemaName}.{TableName}";

    public string DisplayName => string.IsNullOrWhiteSpace(SchemaName)
        ? TableName
        : $"{SchemaName}.{TableName}";
}