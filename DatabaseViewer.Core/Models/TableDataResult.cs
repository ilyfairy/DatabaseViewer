using System.Data;

namespace DatabaseViewer.Core.Models;

public sealed class TableDataResult
{
    public required DataTable Data { get; init; }

    public required TableSchema Schema { get; init; }

    public required int Offset { get; init; }

    public required int PageSize { get; init; }

    public bool HasMoreRows { get; init; }
}