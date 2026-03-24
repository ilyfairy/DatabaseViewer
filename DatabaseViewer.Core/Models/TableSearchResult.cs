using System.Data;

namespace DatabaseViewer.Core.Models;

public sealed class TableSearchResult
{
    public required DataTable Data { get; init; }

    public required int Offset { get; init; }

    public required int PageSize { get; init; }

    public required int TotalMatches { get; init; }

    public bool HasMoreRows { get; init; }
}