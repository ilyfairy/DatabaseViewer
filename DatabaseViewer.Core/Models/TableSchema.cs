namespace DatabaseViewer.Core.Models;

public sealed class TableSchema
{
    public required DbTableInfo Table { get; init; }

    public required IReadOnlyList<ColumnSchema> Columns { get; init; }

    public IReadOnlyList<ForeignKeyReference> IncomingForeignKeys { get; init; } = Array.Empty<ForeignKeyReference>();

    public IReadOnlyList<string> PrimaryKeys => Columns.Where(column => column.IsPrimaryKey).Select(column => column.Name).ToArray();

    public IReadOnlyDictionary<string, ForeignKeyReference> ForeignKeysByColumn => Columns
        .Where(column => column.ForeignKey is not null)
        .ToDictionary(column => column.Name, column => column.ForeignKey!, StringComparer.OrdinalIgnoreCase);
}