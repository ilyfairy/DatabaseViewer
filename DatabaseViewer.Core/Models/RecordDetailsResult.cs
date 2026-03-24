using System.Data;

namespace DatabaseViewer.Core.Models;

public sealed class RecordDetailsResult
{
    public required DataRow Row { get; init; }

    public required TableSchema Schema { get; init; }

    public required RecordIdentity Identity { get; init; }
}