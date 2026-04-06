namespace DatabaseViewer.Core.Models;

public sealed class DbDatabaseTriggerInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string TriggerName { get; set; } = string.Empty;

    public string? TriggerTiming { get; set; }

    public string? TriggerEvent { get; set; }
}