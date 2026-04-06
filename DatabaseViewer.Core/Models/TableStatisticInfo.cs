namespace DatabaseViewer.Core.Models;

public sealed class TableStatisticInfo
{
    public string Name { get; set; } = string.Empty;

    public bool IsAutoCreated { get; set; }

    public bool IsUserCreated { get; set; }

    public bool NoRecompute { get; set; }

    public string? FilterDefinition { get; set; }

    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
}