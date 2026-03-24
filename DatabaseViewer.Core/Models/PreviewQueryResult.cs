using System.Data;

namespace DatabaseViewer.Core.Models;

public sealed class PreviewQueryResult
{
    public required DataTable Data { get; init; }

    public bool HasMoreRows { get; init; }
}