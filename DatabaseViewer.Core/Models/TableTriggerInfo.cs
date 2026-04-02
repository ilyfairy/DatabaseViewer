namespace DatabaseViewer.Core.Models;

public sealed class TableTriggerInfo
{
    public string Name { get; set; } = string.Empty;

    public string? Timing { get; set; }

    public string? Event { get; set; }
}