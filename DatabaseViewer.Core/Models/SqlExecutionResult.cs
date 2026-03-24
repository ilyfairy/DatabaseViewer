namespace DatabaseViewer.Core.Models;

public sealed class SqlExecutionResult
{
    public required string ExecutedSql { get; init; }

    public int? AffectedRows { get; init; }

    public long ElapsedMs { get; init; }

    public required IReadOnlyList<SqlExecutionResultSet> ResultSets { get; init; }
}

public sealed class SqlExecutionResultSet
{
    public required string Name { get; init; }

    public required IReadOnlyList<SqlExecutionColumn> Columns { get; init; }

    public required IReadOnlyList<Dictionary<string, object?>> Rows { get; init; }

    public required int RowCount { get; init; }

    public required bool Truncated { get; init; }
}

public sealed class SqlExecutionColumn
{
    public required string Name { get; init; }

    public required string Type { get; init; }
}