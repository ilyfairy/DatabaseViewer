namespace DatabaseViewer.Core.Models;

public sealed class RecordIdentity : IEquatable<RecordIdentity>
{
    public required string TableKey { get; init; }

    public required string PrimaryKeyText { get; init; }

    public string DisplayLabel => $"{TableKey}#{PrimaryKeyText}";

    public bool Equals(RecordIdentity? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(TableKey, other.TableKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(PrimaryKeyText, other.PrimaryKeyText, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is RecordIdentity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TableKey.ToUpperInvariant(), PrimaryKeyText.ToUpperInvariant());
}