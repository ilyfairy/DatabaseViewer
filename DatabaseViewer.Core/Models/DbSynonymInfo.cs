namespace DatabaseViewer.Core.Models;

public sealed class DbSynonymInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string SynonymName { get; set; } = string.Empty;

    public string BaseObjectName { get; set; } = string.Empty;

    public string? TargetServerName { get; set; }

    public string? TargetDatabaseName { get; set; }

    public string? TargetSchemaName { get; set; }

    public string? TargetObjectName { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(SchemaName)
        ? SynonymName
        : $"{SchemaName}.{SynonymName}";
}