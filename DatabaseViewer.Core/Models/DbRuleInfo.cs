namespace DatabaseViewer.Core.Models;

public sealed class DbRuleInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string RuleName { get; set; } = string.Empty;

    public string? Definition { get; set; }
}