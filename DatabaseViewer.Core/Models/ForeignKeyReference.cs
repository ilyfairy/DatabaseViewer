namespace DatabaseViewer.Core.Models;

public sealed class ForeignKeyReference
{
    public string SourceDatabase { get; set; } = string.Empty;

    public string? SourceSchema { get; set; }

    public string SourceTable { get; set; } = string.Empty;

    public string SourceColumn { get; set; } = string.Empty;

    public string TargetDatabase { get; set; } = string.Empty;

    public string? TargetSchema { get; set; }

    public string TargetTable { get; set; } = string.Empty;

    public string TargetColumn { get; set; } = string.Empty;
}