namespace DatabaseViewer.Core.Models;

public sealed class DbSequenceInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string SequenceName { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public string StartValue { get; set; } = string.Empty;

    public string IncrementValue { get; set; } = string.Empty;
}